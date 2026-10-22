using System.Net.Http.Headers;

namespace SubtitleStudio.Core.Asr;

/// <summary>一个下载源。{file} 会被替换成模型文件名。</summary>
public sealed record DownloadSource(string Name, string UrlTemplate);

/// <summary>下载进度。</summary>
public sealed record DownloadProgress(string SourceName, long Received, long Total, double Fraction);

/// <summary>
/// 本地模型下载器：按镜像顺序试，支持断点续传（HTTP Range）。
/// 不依赖第三方库，失败换下一个源。
/// </summary>
public static class ModelDownloader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>默认镜像，按顺序试。可以用环境变量 SUBTITLESTUDIO_MODEL_MIRRORS 覆盖（分号分隔）。</summary>
    public static IReadOnlyList<DownloadSource> Sources
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("SUBTITLESTUDIO_MODEL_MIRRORS");
            if (!string.IsNullOrWhiteSpace(custom))
            {
                var list = custom
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select((url, i) => new DownloadSource($"镜像 {i + 1}", url))
                    .ToList();

                if (list.Count > 0)
                    return list;
            }

            return new[]
            {
                new DownloadSource("HuggingFace", "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{file}?download=true"),
                new DownloadSource("HF 镜像", "https://hf-mirror.com/ggerganov/whisper.cpp/resolve/main/{file}"),
                new DownloadSource("HuggingFace(ggml-org)", "https://huggingface.co/ggml-org/whisper.cpp/resolve/main/{file}?download=true")
            };
        }
    }

    /// <summary>下载到模型目录；已存在且大小一致就直接返回。</summary>
    public static async Task<string> DownloadAsync(
        ModelSuggestion model,
        string? directory = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var dir = directory ?? ModelCatalog.DefaultModelDirectory;
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, model.FileName);

        Exception? last = null;
        foreach (var source in Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var url = source.UrlTemplate.Replace("{file}", model.FileName, StringComparison.Ordinal);
                await DownloadOneAsync(source, url, target, progress, cancellationToken).ConfigureAwait(false);
                return target;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        var reason = last?.Message ?? "未知错误";

        // TLS 故障单独给出可照着做的检修步骤（原文对用户没意义）
        if (TlsDiagnostics.IsTlsFailure(last))
        {
            throw new InvalidOperationException(
                $"所有镜像都失败了：{reason}{Environment.NewLine}{TlsDiagnostics.Hint(last)}"
                + $"{Environment.NewLine}（也可以手动下载 ggml-*.bin 放进 models，或用 SUBTITLESTUDIO_MODEL_MIRRORS 指向内网镜像）",
                last);
        }

        throw new InvalidOperationException($"所有镜像都失败了：{reason}", last);
    }

    private static async Task DownloadOneAsync(
        DownloadSource source,
        string url,
        string target,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existing = File.Exists(target) ? new FileInfo(target).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);
        request.Headers.UserAgent.ParseAdd("SubtitleStudio/1.0");

        using var response = await Http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            return; // 已经下完了

        response.EnsureSuccessStatusCode();

        var total = (response.Content.Headers.ContentLength ?? 0) + (response.StatusCode == System.Net.HttpStatusCode.PartialContent ? existing : 0);
        var mode = response.StatusCode == System.Net.HttpStatusCode.PartialContent ? FileMode.Append : FileMode.Create;

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(target, mode, FileAccess.Write, FileShare.None, 1 << 20, true);

        var buffer = new byte[1 << 16];
        var received = mode == FileMode.Append ? existing : 0;
        var lastReport = 0L;

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            if (received - lastReport >= 4 * 1024 * 1024)
            {
                lastReport = received;
                progress?.Report(new DownloadProgress(
                    source.Name,
                    received,
                    total,
                    total > 0 ? Math.Clamp(received / (double)total, 0, 1) : 0));
            }
        }

        progress?.Report(new DownloadProgress(source.Name, received, total <= 0 ? received : total, 1));
    }
}
