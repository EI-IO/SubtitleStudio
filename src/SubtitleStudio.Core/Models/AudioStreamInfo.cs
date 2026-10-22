namespace SubtitleStudio.Core.Models;

/// <summary>媒体里的一条音轨（一个 video 文件可以有多个 audio stream）。</summary>
/// <param name="Index">ffmpeg 的全局流索引，用于 <c>-map 0:{Index}</c>。</param>
/// <param name="Ordinal">在音轨里的序号，从 1 开始，用于界面标注「音轨 N」。</param>
public sealed record AudioStreamInfo(
    int Index,
    int Ordinal,
    string Codec,
    int Channels,
    string? Language,
    string? Title)
{
    public string Number => $"音轨 {Ordinal}";

    public string Describe()
    {
        var parts = new List<string> { Number, Codec };
        parts.Add(Channels switch
        {
            <= 0 => "未知声道",
            1 => "单声道",
            2 => "立体声",
            _ => $"{Channels} 声道"
        });

        if (!string.IsNullOrWhiteSpace(Language) && !Language.Equals("und", StringComparison.OrdinalIgnoreCase))
            parts.Add(Language!);

        if (!string.IsNullOrWhiteSpace(Title))
            parts.Add(Title!);

        return string.Join(" · ", parts);
    }
}
