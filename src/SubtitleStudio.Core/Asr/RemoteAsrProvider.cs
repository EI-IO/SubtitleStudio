using System.Net.Http.Headers;
using System.Text.Json;

namespace SubtitleStudio.Core.Asr;

/// <summary>
/// OpenAI 兼容的云端识别：POST {BaseUrl}/audio/transcriptions，
/// 表单里带 file / model / response_format=verbose_json。
/// </summary>
public sealed class RemoteAsrProvider : IAsrProvider, IDisposable
{
    private readonly string _displayName;
    private readonly string _baseUrl;
    private readonly string _model;
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
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public string Name => _displayName;

    public async Task<AsrResult> TranscribeAsync(
        string wavPath,
        AsrOptions options,
        IProgress<AsrProgress>? progress = null,
        CancellationToken cancellationToken = default)
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

        using var response = await _http
            .PostAsync($"{_baseUrl}/audio/transcriptions", form, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new AsrProgress(0.95, "解析结果…"));

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {Trim(body)}");

        var result = Parse(body);
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
