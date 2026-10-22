namespace SubtitleStudio.Core.Models;

/// <summary>字幕样式。所有尺寸都以 1080p 为基准像素，导出时按目标高度等比缩放。</summary>
public sealed class CueStyle
{
    public const double BaseHeight = 1080d;

    public string FontFamily { get; set; } = "Microsoft YaHei";
    public double FontSize { get; set; } = 64d;
    public bool Bold { get; set; } = true;
    public string PrimaryColor { get; set; } = "#FFFFFFFF";
    public string OutlineColor { get; set; } = "#FF000000";
    public double OutlineWidth { get; set; } = 3d;
    public double Shadow { get; set; } = 0d;
    public double MarginBottom { get; set; } = 64d;
    public string Alignment { get; set; } = "Center";
    public int MaxCharsPerLine { get; set; } = 20;

    public CueStyle Clone() => (CueStyle)MemberwiseClone();

    public static CueStyle Default => new();
}
