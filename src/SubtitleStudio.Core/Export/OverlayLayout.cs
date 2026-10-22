using SkiaSharp;
using SubtitleStudio.Core.Models;

namespace SubtitleStudio.Core.Export;

/// <summary>紧凑字幕图的尺寸：宽度取最长一行字幕，高度取行数，不再贴合原视频画幅。</summary>
public sealed record OverlayLayout(int Width, int Height, double Scale, int Lines, int MaxChars)
{
    /// <summary>单条字幕最多这么多个字，超出的在识别阶段就该断开。</summary>
    public const int HardMaxChars = 20;

    /// <summary>四周留白（相对字号的比例）。</summary>
    private const double PadXRatio = 0.45;
    private const double PadYRatio = 0.30;
    private const double LineHeightRatio = 1.32;

    /// <summary>按字幕内容算画布大小；没有任何字幕时给一个占位尺寸。</summary>
    public static OverlayLayout Measure(
        IReadOnlyList<SubtitleCue> cues,
        CueStyle style,
        OverlayQuality quality = OverlayQuality.Hd)
    {
        var scale = quality.ScaleOf();
        var fontPx = (float)Math.Max(6, style.FontSize * scale);
        var maxChars = Math.Clamp(style.MaxCharsPerLine <= 0 ? HardMaxChars : style.MaxCharsPerLine, 4, HardMaxChars);
        var (typeface, fromFile) = SubtitleLayerRenderer.ResolveTypefaceFor(style);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            TextSize = fontPx,
            Typeface = typeface,
            FakeBoldText = style.Bold && fromFile
        };

        var widest = 0f;
        var maxLines = 1;

        foreach (var cue in cues)
        {
            var lines = SplitLines(cue.Text, maxChars);
            if (lines.Count > maxLines)
                maxLines = lines.Count;

            foreach (var line in lines)
            {
                var width = paint.MeasureText(line);
                if (width > widest)
                    widest = width;
            }
        }

        var padX = (float)(fontPx * PadXRatio);
        var padY = (float)(fontPx * PadYRatio);
        var lineHeight = fontPx * (float)LineHeightRatio;

        var widthPx = (int)Math.Ceiling(widest + padX * 2);
        var heightPx = (int)Math.Ceiling(maxLines * lineHeight + padY * 2);

        // 视频编码要求偶数尺寸；再夹一个合理区间，避免空字幕时出现 1 像素的画布
        widthPx = Math.Clamp(widthPx, 160, 3840);
        heightPx = Math.Clamp(heightPx, 72, 2160);

        return new OverlayLayout(
            widthPx - widthPx % 2,
            heightPx - heightPx % 2,
            scale,
            maxLines,
            maxChars);
    }

    /// <summary>按字数折行：中文逐字，英文尽量在空格处断开。</summary>
    public static List<string> SplitLines(string text, int maxChars)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return lines;

        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var rest = paragraph.Trim();
            while (rest.Length > maxChars)
            {
                var cut = maxChars;

                var space = rest.LastIndexOf(' ', Math.Min(maxChars, rest.Length - 1));
                if (space > maxChars / 2)
                    cut = space;

                lines.Add(rest[..cut].Trim());
                rest = rest[cut..].TrimStart();
            }

            if (rest.Length > 0)
                lines.Add(rest);
        }

        return lines;
    }

    /// <summary>把画布尺寸说清楚，界面上直接显示。</summary>
    public string Describe() => $"{Width}×{Height} · {Lines} 行 · {MaxChars} 字/行";
}
