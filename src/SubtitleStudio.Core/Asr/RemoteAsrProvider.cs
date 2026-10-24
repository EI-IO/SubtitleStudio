using System.Net.Http.Headers;
using System.Text.Json;
using SubtitleStudio.Core.Media;

namespace SubtitleStudio.Core.Asr;

/// <summary>
/// OpenAI 兼容的云端识别：POST {BaseUrl}/audio/transcriptions，
/// 表单里带 file / model / response_format=verbose_json。
/// 长音频（超过 10 分钟）由 <see cref="AudioChunker"/> 在静音处切片逐段上传，
/// 既绕开 25MB 上传上限，也避免超长请求超时后整段重来。
/// </summary>
public sealed class RemoteAsrProvider : IAsrProvider, IDisposable
{
    /// <summary>短于这个时长一次传完，不值得切片。</summary>
    private const double MaxSingleSeconds = 600;

    /// <summary>切片上限：8 分钟的 16k 单声道 PCM 约 15MB，离常见的 25MB 上传限制有富余。</summary>
    private const double MaxChunkSeconds = 480;

    /// <summary>相邻片重叠秒数；重叠区的句子按切点去重（与天翼星辰同一条规则）。</summary>
    private const double OverlapSeconds = 2.0;

    private readonly string _displayName;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _apiKey;
    private readonly HttpClient _http;

    /// <param name="displayName">界面上显示的名字，出错时用作前缀。</param>
    /// <param name="baseUrl">兼容 OpenAI 的根地址，例如 https://api.siliconflow.cn/v1。</param>
    /// <param name="model">模型 id，例如 FunAudioLLM/SenseVoiceSmall。</param>
    /// <param name="apiKey">Bearer 密钥。</param>
    public RemoteAsrProvider(string displayName, string baseUrl, string model, string apiKey, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"{displayName} 还没有配置 API Key");
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException($"{displayName} 没有配置接口地址");

        _displayName = displayName;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _apiKey = apiKey;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // 注意：不动 _http.DefaultRequestHeaders——调用方可能共享这个 client，改默认头会污染别人
    }

    public string Name => _displayName;

    public async Task<AsrResult> TranscribeAsync(
        string wavPath,
        AsrOptions options,
        IProgress<AsrProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pcm = WavPcm.ReadPcm16(wavPath);
        const int bytesPerSecond = AudioExtractor.SampleRate * 2;
        var totalSeconds = pcm.Length / (double)bytesPerSecond;

        // 短音频直接整段上传
        if (pcm.Length == 0 || totalSeconds <= MaxSingleSeconds)
            return await TranscribeOnceAsync(wavPath, options, 0, 0, double.MaxValue, progress, cancellationToken)
                .ConfigureAwait(false);

        // 长音频：静音处切片逐段传，重叠区按切点去重
        var chunks = AudioChunker.Plan(pcm, MaxChunkSeconds, OverlapSeconds, AudioExtractor.SampleRate);
        var all = new List<AsrSegment>();
        var language = "auto";

        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var offset = ((int)(chunks[i].StartSeconds * bytesPerSecond)) & ~1;
            var length = Math.Min(((int)((chunks[i].EndSeconds - chunks[i].StartSeconds) * bytesPerSecond)) & ~1,
                pcm.Length - offset);
            if (length <= 0)
                break;

            var slice = new byte[length];
            Buffer.BlockCopy(pcm, offset, slice, 0, length);

            var chunkStart = offset / (double)bytesPerSecond;
            var chunkEnd = chunkStart + length / (double)bytesPerSecond;
            // 责任区间：起点落在切点之前的句子归本片，之后的归下一片（下一片有它的完整开头）
            var fromSeconds = i > 0 ? chunkStart + OverlapSeconds / 2 : 0;
            var toSeconds = i == chunks.Count - 1 ? double.MaxValue : chunkEnd - OverlapSeconds / 2;

            var chunkPath = TempWorkspace.CreateFilePath(wavPath, ".chunk.wav");
            try
            {
                WavPcm.WritePcm16(chunkPath, slice, AudioExtractor.SampleRate);
                var chunkProgress = new Progress<AsrProgress>(p => progress?.Report(
                    new AsrProgress((i + p.Fraction) / chunks.Count, $"云端识别 {i + 1}/{chunks.Count} 段")));

                var part = await TranscribeOnceAsync(chunkPath, options, chunkStart, fromSeconds, toSeconds,
                    chunkProgress, cancellationToken).ConfigureAwait(false);

                all.AddRange(part.Segments);
                if (!string.IsNullOrWhiteSpace(part.Language))
                    language = part.Language;
            }
            finally
            {
                TempWorkspace.TryDelete(chunkPath);
            }
        }

        progress?.Report(new AsrProgress(1, $"识别完成，{all.Count} 段"));
        return new AsrResult(language, all);
    }

    /// <summary>
    /// 上传单个 wav 并解析；<paramref name="offsetSeconds"/> 把分片时间换算回整条时间轴，
    /// [<paramref name="fromSeconds"/>, <paramref name="toSeconds"/>) 是本片的责任区间（起点落在外面就丢掉）。
    /// </summary>
    private async Task<AsrResult> TranscribeOnceAsync(
        string wavPath,
        AsrOptions options,
        double offsetSeconds,
        double fromSeconds,
        double toSeconds,
        IProgress<AsrProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new AsrProgress(0.1, "上传音频…"));

        using var form = new MultipartFormDataContent();
        await using var file = File.OpenRead(wavPath);

        var audio = new StreamContent(file);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", Path.GetFileName(wavPath));
        form.Add(new StringContent(_model), "model");
        form.Add(new StringContent("verbose_json"), "response_format");

        if (!string.IsNullOrWhiteSpace(options.Language) &&
            !string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            form.Add(new StringContent(options.Language), "language");
        }

        progress?.Report(new AsrProgress(0.35, "云端识别中…"));

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/audio/transcriptions")
        {
            Content = form
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _http
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new AsrProgress(0.95, "解析结果…"));

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {Trim(body)}");

        var result = Parse(body);

        if (offsetSeconds > 0 || fromSeconds > 0 || toSeconds < double.MaxValue)
        {
            // 分片模式：时间换算回整条时间轴，重叠区只留责任区间内的句子
            var kept = result.Segments
                .Select(s => new AsrSegment(s.Text, s.Start + offsetSeconds, s.End + offsetSeconds))
                .Where(s => s.Start >= fromSeconds - 0.001 && s.Start < toSeconds)
                .ToList();
            result = new AsrResult(result.Language, kept);
        }

        progress?.Report(new AsrProgress(1, $"识别完成，{result.Segments.Count} 段"));
        return result;
    }

    private static AsrResult Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var segments = new List<AsrSegment>();
        var language = "auto";

        if (root.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String)
            language = lang.GetString() ?? language;

        if (root.TryGetProperty("segments", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var seg in arr.EnumerateArray())
            {
                var text = seg.TryGetProperty("text", out var t) ? (t.GetString() ?? string.Empty).Trim() : string.Empty;
                if (text.Length == 0)
                    continue;

                var start = seg.TryGetProperty("start", out var s) && s.TryGetDouble(out var sv) ? sv : 0;
                var end = seg.TryGetProperty("end", out var e) && e.TryGetDouble(out var ev) ? ev : start + 1;
                segments.Add(new AsrSegment(text, start, end));
            }
        }

        if (segments.Count == 0 && root.TryGetProperty("text", out var whole))
        {
            var text = (whole.GetString() ?? string.Empty).Trim();
            if (text.Length > 0)
            {
                // 只有一整段文字时，按字数粗分，交给 CueSegmenter 再断句
                segments.Add(new AsrSegment(text, 0, Math.Max(1, text.Length * 0.18)));
            }
        }

        return new AsrResult(language, segments);
    }

    private static string Trim(string body) =>
        body.Length <= 300 ? body : body[..300] + "…";

    public void Dispose() => _http.Dispose();
}
