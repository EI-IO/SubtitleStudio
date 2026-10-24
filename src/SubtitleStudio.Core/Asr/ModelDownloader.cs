using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;

namespace SubtitleStudio.Core.Asr;

/// <summary>一个下载源。{file} 会被替换成模型文件名。</summary>
public sealed record DownloadSource(string Name, string UrlTemplate);

/// <summary>下载进度。</summary>
public sealed record DownloadProgress(string SourceName, long Received, long Total, double Fraction);

/// <summary>镜像连通性探测结果。</summary>
public sealed record SourceProbe(DownloadSource Source, bool Reachable, long LatencyMs, string? Error);

/// <summary>
/// 本地模型下载器：下载前先并发探测各镜像连通性，通的按延迟排序先下，不通的垫底再试
/// （HEAD 被拦不代表 GET 也不通），全挂了就报出每个源的死因。
/// 支持断点续传（HTTP Range），半成品留在 .part 里，下次启动/重试接着下。
/// </summary>
public static class ModelDownloader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>同一个模型文件同时只允许一个下载在跑，两个写者往同一个 .part 里追加就毁了。</summary>
    private static readonly ConcurrentDictionary<string, byte> InFlight = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>模型目录里没下完的半成品（ggml-*.bin.part），返回文件名（不带 .part）和已下字节数。</summary>
    public static IReadOnlyList<(string FileName, long Bytes)> FindIncomplete(string? directory = null)
    {
        var dir = directory ?? ModelCatalog.DefaultModelDirectory;
        if (!Directory.Exists(dir))
            return Array.Empty<(string, long)>();

        return Directory.EnumerateFiles(dir, "ggml-*.bin.part")
            .Select(p => (Path.GetFileName(p)[..^".part".Length], new FileInfo(p).Length))
            .ToList();
    }

    /// <summary>这个模型此刻是否已有下载在进行（界面据此显示「停止」而不是「下载」）。</summary>
    public static bool IsDownloading(ModelSuggestion model, string? directory = null)
    {
        var dir = directory ?? ModelCatalog.DefaultModelDirectory;
        return InFlight.ContainsKey(Path.Combine(dir, model.FileName));
    }

    /// <summary>并发 HEAD 探测所有镜像：4 秒内响应算通，按延迟排序给下载排优先级。</summary>
    public static async Task<IReadOnlyList<SourceProbe>> ProbeSourcesAsync(
        ModelSuggestion model, CancellationToken cancellationToken = default)
    {
        var probes = Sources.Select(async source =>
        {
            var url = source.UrlTemplate.Replace("{file}", model.FileName, StringComparison.Ordinal);
            var watch = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                request.Headers.UserAgent.ParseAdd("SubtitleStudio/1.0");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                using var response = await Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                return new SourceProbe(source, response.IsSuccessStatusCode, watch.ElapsedMilliseconds,
                    response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                return new SourceProbe(source, false, watch.ElapsedMilliseconds, ex.GetType().Name);
            }
        });

        var results = await Task.WhenAll(probes).ConfigureAwait(false);

        // 通的按延迟升序排前面；不通的放最后兜底（HEAD 被墙不代表 GET 也死）
        return results
            .OrderByDescending(p => p.Reachable)
            .ThenBy(p => p.LatencyMs)
            .ToList();
    }

    /// <summary>
    /// 下载到模型目录；已存在就直接返回。
    /// 半成品写到 .part 文件并记下来源 URL（.part.src），换源续传会拼出坏文件，所以来源不同就重下。
    /// </summary>
    public static async Task<string> DownloadAsync(
        ModelSuggestion model,
        string? directory = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var dir = directory ?? ModelCatalog.DefaultModelDirectory;
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, model.FileName);

        // 成品文件直接用，不再联网验证
        if (File.Exists(target))
            return target;

        if (!InFlight.TryAdd(target, 0))
            throw new InvalidOperationException($"{model.Display} 已经在下载中了");

        try
        {
            var probes = await ProbeSourcesAsync(model, cancellationToken).ConfigureAwait(false);

            var failures = new List<string>();
            Exception? last = null;
            foreach (var probe in probes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var url = probe.Source.UrlTemplate.Replace("{file}", model.FileName, StringComparison.Ordinal);
                    await DownloadOneAsync(probe.Source, url, target, progress, cancellationToken).ConfigureAwait(false);
                    return target;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                    failures.Add($"{probe.Source.Name}: {ex.Message}");
                }
            }

            // 每个源的死因都摆出来，一眼看出是全部连不上还是下到一半断了
            var reason = string.Join("；", failures);

            // TLS 故障单独给出可照着做的检修步骤（原文对用户没意义）
            if (TlsDiagnostics.IsTlsFailure(last))
            {
                throw new InvalidOperationException(
                    $"所有镜像都失败了：{reason}{Environment.NewLine}{TlsDiagnostics.Hint(last)}"
                    + $"{Environment.NewLine}（也可以手动下载 ggml-*.bin 放进 models，或用 SUBTITLESTUDIO_MODEL_MIRRORS 指向内网镜像）",
                    last);
            }

            throw new InvalidOperationException($"所有镜像都失败了：{reason}");
        }
        finally
        {
            InFlight.TryRemove(target, out _);
        }
    }

    private static async Task DownloadOneAsync(
        DownloadSource source,
        string url,
        string target,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partPath = target + ".part";
        var srcPath = partPath + ".src";

        // 半成品必须来自同一个 URL，否则两段内容拼在一起就是坏模型
        var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (existing > 0)
        {
            var previousUrl = File.Exists(srcPath)
                ? (await File.ReadAllTextAsync(srcPath, cancellationToken).ConfigureAwait(false)).Trim()
                : null;
            if (!string.Equals(previousUrl, url, StringComparison.Ordinal))
            {
                existing = 0;
                TryDelete(partPath);
                TryDelete(srcPath);
            }
        }

        var completed = await FetchAsync(source, url, partPath, existing, progress, cancellationToken)
            .ConfigureAwait(false);

        if (!completed)
        {
            // 服务器认为已下完（416）但长度对不上：删掉半成品重新整下
            TryDelete(partPath);
            TryDelete(srcPath);
            var done = await FetchAsync(source, url, partPath, 0, progress, cancellationToken)
                .ConfigureAwait(false);
            if (!done)
                throw new InvalidOperationException($"{source.Name}：下载长度校验失败");
        }

        // 全部写完才转正，避免把半截文件当成品
        File.Move(partPath, target, overwrite: true);
        TryDelete(srcPath);
    }

    /// <summary>下载（或续传）到 partPath；返回 false 表示服务器拒绝了断点续传（416），需要调用方重来。</summary>
    private static async Task<bool> FetchAsync(
        DownloadSource source,
        string url,
        string partPath,
        long existing,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
            request.Headers.Range = new RangeHeaderValue(existing, null);
        request.Headers.UserAgent.ParseAdd("SubtitleStudio/1.0");

        using var response = await Http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // 用 HEAD 拿真实总长：和本地一致才算下完，不一致就是坏半成品
            var expected = await HeadLengthAsync(url, cancellationToken).ConfigureAwait(false);
            return expected > 0 && existing == expected;
        }

        response.EnsureSuccessStatusCode();

        // 记下来源，续传时只能接着同一个 URL 写
        await File.WriteAllTextAsync(partPath + ".src", url, cancellationToken).ConfigureAwait(false);

        var total = (response.Content.Headers.ContentLength ?? 0) + (response.StatusCode == System.Net.HttpStatusCode.PartialContent ? existing : 0);
        var mode = response.StatusCode == System.Net.HttpStatusCode.PartialContent ? FileMode.Append : FileMode.Create;

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(partPath, mode, FileAccess.Write, FileShare.None, 1 << 20, true);

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

        // 服务器给了总长就必须对上，防止连接被中间设备掐断留下「看起来下完」的文件
        if (total > 0 && received != total)
            throw new InvalidOperationException($"{source.Name}：下载不完整（{received}/{total} 字节）");

        return true;
    }

    /// <summary>HEAD 拿 Content-Length；服务器不支持就返回 -1。</summary>
    private static async Task<long> HeadLengthAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            request.Headers.UserAgent.ParseAdd("SubtitleStudio/1.0");
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? response.Content.Headers.ContentLength ?? -1 : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 删不掉就留着，下次来源不匹配时会再清
        }
    }
}
