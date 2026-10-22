using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SubtitleStudio.Core.Media;

namespace SubtitleStudio.Core.Asr;

/// <summary>
/// 天翼星辰（TeleAI）短语音识别。
/// 协议：HTTPS POST <c>/aipaas/voice/v1/asr/short</c>，JSON 里放 Base64 音频（1 分钟以内、Base64 后 ≤4MB）。
/// 鉴权只用 <c>Authorization: Bearer &lt;API Key&gt;</c>，不涉及 AppID / AppKey。
/// 超过 1 分钟的音频按 58 秒切片，逐片识别后把时间轴整体平移再拼接。
/// </summary>
public static class TeleAiClient
{
    public const string DefaultBaseUrl = "https://api-maas.teleai.com.cn";
    public const string ShortPath = "/aipaas/voice/v1/asr/short";

    /// <summary>文档限制 1 分钟，留两秒余量。</summary>
    public const double MaxChunkSeconds = 58.0;

    public static string BuildUrl(string? baseUrl = null)
    {
        var b = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
        return b + ShortPath;
    }

    /// <summary>把整段 16k 单声道 PCM 切片后逐段识别。</summary>
    public static async Task<AsrResult> TranscribeAsync(
        string apiKey,
        string wavPath,
        string? baseUrl = null,
        IProgress<AsrProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("天翼星辰的 API Key 还没填（菜单 → 设置 → 天翼星辰）");

        var pcm = WavPcm.ReadPcm16(wavPath);
        if (pcm.Length == 0)
            throw new InvalidOperationException($"读不到音频数据：{wavPath}");

        // 16k 单声道 s16le = 32000 字节/秒
        const int bytesPerSecond = AudioExtractor.SampleRate * 2;
        var chunkBytes = (int)(MaxChunkSeconds * bytesPerSecond);
        var total = Math.Max(1, (int)Math.Ceiling(pcm.Length / (double)chunkBytes));
        var segments = new List<AsrSegment>();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        var url = BuildUrl(baseUrl);

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var offset = i * chunkBytes;
            var length = Math.Min(chunkBytes, pcm.Length - offset);
            var audio = new byte[length];
            Buffer.BlockCopy(pcm, offset, audio, 0, length);

            var payload = new
            {
                req_id = Guid.NewGuid().ToString("N"),
                audio_data = Convert.ToBase64String(audio),
                sample_rate = AudioExtractor.SampleRate,
                enable_punc = true,
                enable_itn = true
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            string body;
            try
            {
                using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"天翼星辰返回 {(int)response.StatusCode}：{FirstChars(body, 200)}");
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"连不上天翼星辰：{TlsDiagnostics.Describe(ex)}", ex);
            }

            var chunkStart = offset / (double)bytesPerSecond;
            segments.AddRange(ParseSentences(body, chunkStart));

            progress?.Report(new AsrProgress((i + 1) / (double)total, $"天翼星辰 {i + 1}/{total} 段"));
        }

        return new AsrResult("zh", segments);
    }

    /// <summary>解析 {"result":{"sentences":[{"texts":"..","begin_time":毫秒,"end_time":毫秒}]}}。</summary>
    public static IReadOnlyList<AsrSegment> ParseSentences(string json, double offsetSeconds = 0)
    {
        var list = new List<AsrSegment>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var code = root.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var c) ? c : 0;
        if (code != 0 && code != 10000 && code != 101)
        {
            var msg = root.TryGetProperty("message", out var m) ? m.GetString() : null;
            throw new InvalidOperationException($"天翼星辰识别失败（code={code}）：{msg}");
        }

        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            return list;

        if (!result.TryGetProperty("sentences", out var sentences) || sentences.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in sentences.EnumerateArray())
        {
            var text = item.TryGetProperty("texts", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var begin = item.TryGetProperty("begin_time", out var b) && b.TryGetDouble(out var bv) ? bv : 0;
            var end = item.TryGetProperty("end_time", out var e) && e.TryGetDouble(out var ev) ? ev : begin;

            list.Add(new AsrSegment(text.Trim(), offsetSeconds + begin / 1000.0, offsetSeconds + end / 1000.0));
        }

        return list;
    }

    private static string FirstChars(string? text, int count)
    {
        if (string.IsNullOrEmpty(text))
            return "(空)";

        var flat = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flat.Length <= count ? flat : flat[..count] + "…";
    }
}
