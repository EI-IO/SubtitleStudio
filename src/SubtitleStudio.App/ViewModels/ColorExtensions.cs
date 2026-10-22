using Avalonia.Media;
using SkiaSharp;

namespace SubtitleStudio.App.ViewModels;

/// <summary>十六进制颜色文本 ↔ Avalonia 颜色。</summary>
internal static class HexColor
{
    /// <summary>支持 #RGB 之外的 #RRGGBB / #AARRGGBB；解析失败返回 false。</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = Colors.White;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var hex = text.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8))
            return false;

        if (!uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
            return false;

        color = hex.Length == 8
            ? Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }
}

/// <summary>SkiaSharp 与 Avalonia 的颜色互转。</summary>
internal static class ColorExtensions
{
    public static Color ToAvalonia(this SKColor color) =>
        Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
}
