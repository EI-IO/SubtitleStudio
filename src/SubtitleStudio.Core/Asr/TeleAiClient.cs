using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SubtitleStudio.Core.Media;

namespace SubtitleStudio.Core.Asr;

/// <summary>
/// 天翼星辰（TeleAI）短语音识别。
/// 协议：HTTPS POST <c>/aipaas/voice/v1/asr/short</c>，JSON 里放 Base64 音频（1 分钟以内、Base64 后 ≤4MB）。
/// 鉴权只用 <c>Authorization: Bearer &lt;API Key&gt;</c>，不涉及 AppID / AppKey。
/// 超过 1 分钟的音频由 <see cref="AudioChunker"/> 在静音处切片（上限 58 秒、片间重叠 2 秒），
/// 重叠区的句子以切点为界各归一片——切点落在静音里，不会把词劈成两半。
/// </summary>
public static class TeleAiClient
{
    public const string DefaultBaseUrl = "https://api-maas.teleai.com.cn";
    public const string ShortPath = "/aipaas/voice/v1/asr/short";

    /// <summary>文档限制 1 分钟，留两秒余量。</summary>
    public const double MaxChunkSeconds = 58.0;

    /// <summary>相邻两片重叠的秒数：前一片结尾的句子在后一片开头重出，按起点去重。</summary>
    public const double OverlapSeconds = 2.0;

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
        // 静音处下刀的切片规划；短音频就一片
        var chunks = AudioChunker.Plan(pcm, MaxChunkSeconds, OverlapSeconds, AudioExtractor.SampleRate);
        var total = chunks.Count;
        var segments = new List<AsrSegment>();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        var url = BuildUrl(baseUrl);

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 起点/长度对齐到采样点（2 字节），否则会读出半个采样的碎音
            var offset = ((int)(chunks[i].StartSeconds * bytesPerSecond)) & ~1;
            var length = Math.Min(((int)((chunks[i].EndSeconds - chunks[i].StartSeconds) * bytesPerSecond)) & ~1,
                pcm.Length - offset);
            if (length <= 0)
                break;

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
            var chunkEnd = chunkStart + length / (double)bytesPerSecond;
            var isLast = offset + length >= pcm.Length;

            // 重叠区在两片里都能听到，责任边界取重叠区中点：
            // 起点落在中点之前的句子归本片，之后的归下一片（下一片有它的完整开头），
            // 这样两片之间既不会重复也不会漏句。最后一片收到音频结尾为止。
            var fromSeconds = i > 0 ? chunkStart + OverlapSeconds / 2 : 0;
            var toSeconds = isLast ? double.MaxValue : chunkEnd - OverlapSeconds / 2;
            segments.AddRange(ParseSentences(body, chunkStart, fromSeconds, toSeconds, chunkEnd));

            progress?.Report(new AsrProgress((i + 1) / (double)total, $"天翼星辰 {i + 1}/{total} 段"));
        }

        return new AsrResult("zh", segments);
    }

    /// <summary>
    /// 解析 {"result":{"sentences":[{"texts":"..","begin_time":毫秒,"end_time":毫秒}]}}。
    /// <paramref name="fromSeconds"/>/<paramref name="toSeconds"/> 是绝对时间轴上的保留区间
    /// （按句子起点判断），用于切片重叠区去重；<paramref name="audioEnd"/> 用来钳住片尾的结束时间。
    /// </summary>
    public static IReadOnlyList<AsrSegment> ParseSentences(
        string json,
        double offsetSeconds = 0,
        double fromSeconds = 0,
        double toSeconds = double.MaxValue,
        double audioEnd = double.MaxValue)
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

            var absBegin = offsetSeconds + begin / 1000.0;
            var absEnd = Math.Min(offsetSeconds + end / 1000.0, audioEnd);

            // 重叠区去重：起点不在本片责任区间的句子跳过
            if (absBegin < fromSeconds - 0.001 || absBegin >= toSeconds)
                continue;

            list.Add(new AsrSegment(text.Trim(), absBegin, absEnd));
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
