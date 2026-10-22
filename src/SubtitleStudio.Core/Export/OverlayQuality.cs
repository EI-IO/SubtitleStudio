namespace SubtitleStudio.Core.Export;

/// <summary>导出字幕图层的清晰度档位：只影响像素密度，不改变版面。</summary>
public enum OverlayQuality
{
    /// <summary>标清：约 0.5 倍，文件最小，适合快速预览。</summary>
    Sd,

    /// <summary>高清：1 倍，日常交付用。</summary>
    Hd,

    /// <summary>超清：2 倍像素密度，放大会更干净。</summary>
    Uhd
}

public static class OverlayQualityExtensions
{
    /// <summary>相对基准字号的缩放倍率。</summary>
    public static double ScaleOf(this OverlayQuality quality) => quality switch
    {
        OverlayQuality.Sd => 0.5,
        OverlayQuality.Uhd => 2.0,
        _ => 1.0
    };

    public static string DisplayName(this OverlayQuality quality) => quality switch
    {
        OverlayQuality.Sd => "标清",
        OverlayQuality.Uhd => "超清",
        _ => "高清"
    };
}

/// <summary>下拉框用的档位项。</summary>
public sealed record QualityOption(OverlayQuality Value, string Name)
{
    public static IReadOnlyList<QualityOption> All { get; } = new[]
    {
        new QualityOption(OverlayQuality.Sd, "标清"),
        new QualityOption(OverlayQuality.Hd, "高清"),
        new QualityOption(OverlayQuality.Uhd, "超清")
    };

    public override string ToString() => Name;
}
