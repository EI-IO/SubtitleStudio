using System.Net.Http.Headers;
using System.Text.Json;

namespace SubtitleStudio.Core.Asr;

/// <summary>云端模型列表里的一项。</summary>
public sealed record RemoteModel(string Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// 硅基流动：OpenAI 兼容。
/// 列模型用 GET {base}/models，识别用 POST {base}/audio/transcriptions（multipart）。
/// </summary>
public static class SiliconFlowClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>可能是语音识别的模型：接口给了 type 就用 type，没给就看名字。</summary>
    private static bool LooksLikeAsr(string id, string? type)
    {
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (type.Equals("audio", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!type.Equals("text", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var lower = id.ToLowerInvariant();
        return lower.Contains("sensevoice")
               || lower.Contains("whisper")
               || lower.Contains("asr")
               || lower.Contains("speech")
               || lower.Contains("transcri");
    }

    /// <summary>拉模型列表；audioOnly=true 时只留语音识别相关的（硅基流动用），自定义提供商传 false 全量返回。</summary>
    public static async Task<IReadOnlyList<RemoteModel>> ListModelsAsync(
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken = default,
        bool audioOnly = true)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("还没有填 API Key");

        var url = baseUrl.TrimEnd('/') + "/models";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"拉模型列表失败（HTTP {(int)response.StatusCode}）：{Shorten(body)}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("模型列表格式不认识：" + Shorten(body));

        var result = new List<RemoteModel>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idProp))
                continue;

            var id = idProp.GetString();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var type = item.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;

            if (audioOnly && !LooksLikeAsr(id, type))
                continue;

            result.Add(new RemoteModel(id, id));
        }

        result.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>识别一个 wav，返回整段文本（这个接口不给词级时间戳）。</summary>
    public static async Task<string> TranscribeAsync(
        string baseUrl,
        string apiKey,
        string model,
        string wavPath,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("还没有填 API Key");

        var url = baseUrl.TrimEnd('/') + "/audio/transcriptions";
        using var form = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(wavPath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", Path.GetFileName(wavPath));
        form.Add(new StringContent(model), "model");
        if (!string.IsNullOrWhiteSpace(language))
            form.Add(new StringContent(language), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await Http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"云端识别失败（HTTP {(int)response.StatusCode}）：{Shorten(body)}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("text", out var text))
                return text.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            // 有的实现直接回纯文本
        }

        return body.Trim();
    }

    private static string Shorten(string text, int max = 300) =>
        text.Length <= max ? text : text[..max] + "…";
}
