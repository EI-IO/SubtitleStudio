using System.Globalization;
using System.Runtime.InteropServices;
using SkiaSharp;
using SubtitleStudio.Core.Models;

namespace SubtitleStudio.Core.Export;

/// <summary>
/// 用 SkiaSharp 把字幕画成一张带透明通道的图层。
/// 预览和导出共用同一套渲染代码，保证所见即所得。
/// </summary>
public sealed class SubtitleLayerRenderer : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly double _renderScale;
    private readonly bool _centerVertically;
    private readonly SKBitmap _bitmap;
    private readonly SKCanvas _canvas;
    private readonly SKPaint _stroke;
    private readonly SKPaint _fill;

    /// <summary>阴影画笔复用：导出是逐帧调用的热路径，不能每行文字都 new 一个。</summary>
    private readonly SKPaint _shadow;
    private byte[]? _buffer;

    public SubtitleLayerRenderer(int width, int height)
        : this(width, height, height / CueStyle.BaseHeight, false)
    {
    }

    /// <param name="renderScale">字号相对 1080p 基准的倍率。</param>
    /// <param name="centerVertically">紧凑画布（画面就是字幕本身）时垂直居中，否则贴底。</param>
    public SubtitleLayerRenderer(int width, int height, double renderScale, bool centerVertically)
    {
        _width = width;
        _height = height;
        _renderScale = renderScale;
        _centerVertically = centerVertically;
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _bitmap = new SKBitmap(info);
        _canvas = new SKCanvas(_bitmap);
        _stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        _fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _shadow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0, 0, 0, 160) };
        Clear();
    }

    public int Width => _width;
    public int Height => _height;
    public SKBitmap Bitmap => _bitmap;

    /// <summary>用户导入的字体：家族名 → 字体文件路径。由 App 启动时挂上，Core 不依赖 UI。</summary>
    public static Func<string, string?>? FontFileResolver { get; set; }

    /// <summary>字体解析很贵（可能要从磁盘读 ttf），按「家族 + 粗细」缓存。</summary>
    private static readonly Dictionary<(string Family, bool Bold), (SKTypeface Face, bool FromFile)> Typefaces = new();

    /// <summary>缓存上限：超过就整表清掉（文件字体要 Dispose），避免导入很多字体后只增不减。</summary>
    private const int MaxCachedTypefaces = 16;

    private static readonly Lock TypefaceLock = new();

    private static SKTypeface ResolveTypeface(CueStyle style, out bool fromFile)
    {
        var (face, file) = ResolveTypefaceFor(style);
        fromFile = file;
        return face;
    }

    /// <summary>给尺寸计算（OverlayLayout）复用同一套字体解析。</summary>
    public static (SKTypeface Face, bool FromFile) ResolveTypefaceFor(CueStyle style)
    {
        var key = (style.FontFamily, style.Bold);

        lock (TypefaceLock)
        {
            if (Typefaces.TryGetValue(key, out var cached))
                return cached;
        }

        var loaded = LoadTypeface(style);

        lock (TypefaceLock)
        {
            if (Typefaces.Count >= MaxCachedTypefaces)
            {
                // 文件加载的字体是我们独占的，清缓存时一并释放；系统字体不归我们管
                foreach (var entry in Typefaces.Values)
                {
                    if (entry.FromFile)
                        entry.Face.Dispose();
                }

                Typefaces.Clear();
            }

            Typefaces[key] = loaded;
        }

        return loaded;
    }

    private static (SKTypeface Face, bool FromFile) LoadTypeface(CueStyle style)
    {
        var weight = style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;

        var custom = FontFileResolver?.Invoke(style.FontFamily);
        if (!string.IsNullOrEmpty(custom) && File.Exists(custom))
        {
            try
            {
                var loaded = SKTypeface.FromFile(custom);
                if (loaded is not null)
                    return (loaded, true);
            }
            catch
            {
                // 字体文件损坏就退回系统字体
            }
        }

        var system = SKTypeface.FromFamilyName(style.FontFamily, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                     ?? SKTypeface.FromFamilyName("Microsoft YaHei", weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                     ?? SKTypeface.Default;

        return (system, false);
    }

    public void Clear()
    {
        _canvas.Clear(SKColors.Transparent);
    }

    /// <summary>把 time 时刻处于活动状态的字幕画上去。</summary>
    public void Render(IReadOnlyList<SubtitleCue> cues, CueStyle style, double time)
    {
        Clear();
        if (cues.Count == 0)
            return;

        var scale = _renderScale > 0 ? _renderScale : _height / CueStyle.BaseHeight;
        var fontPx = (float)Math.Max(8, style.FontSize * scale);
        var typeface = ResolveTypeface(style, out var fontFromFile);

        _fill.TextSize = fontPx;
        // 文件字体不支持独立的粗体字形，用仿粗体补上
        _fill.FakeBoldText = fontFromFile && style.Bold;
        _fill.Typeface = typeface;
        _fill.Color = ParseColor(style.PrimaryColor);

        _stroke.TextSize = fontPx;
        _stroke.FakeBoldText = fontFromFile && style.Bold;
        _stroke.Typeface = typeface;
        _stroke.Color = ParseColor(style.OutlineColor);
        _stroke.StrokeWidth = (float)Math.Max(0, style.OutlineWidth * scale);
        _stroke.StrokeJoin = SKStrokeJoin.Round;

        var shadow = (float)Math.Max(0, style.Shadow * scale);
        var marginBottom = (float)(style.MarginBottom * scale);
        var maxWidth = _width * 0.92f;

        foreach (var cue in cues)
        {
            if (time + 1e-6 < cue.Start || time > cue.End + 1e-6)
                continue;
            if (string.IsNullOrWhiteSpace(cue.Text))
                continue;

            var lines = WrapText(cue.Text, _fill, maxWidth, style.MaxCharsPerLine);
            if (lines.Count == 0)
                continue;

            var lineHeight = fontPx * 1.32f;

            // 紧凑画布：整块文字垂直居中；普通画布：贴底
            var y = _centerVertically
                ? (_height - lines.Count * lineHeight) / 2f + fontPx * 0.92f
                : _height - marginBottom - (lines.Count - 1) * lineHeight;

            foreach (var line in lines)
            {
                var lineWidth = _fill.MeasureText(line);
                var x = style.Alignment.Equals("Left", StringComparison.OrdinalIgnoreCase)
                    ? (_width - maxWidth) / 2f
                    : (_width - lineWidth) / 2f;

                if (shadow > 0)
                {
                    _shadow.TextSize = fontPx;
                    _shadow.FakeBoldText = fontFromFile && style.Bold;
                    _shadow.Typeface = typeface;
                    _canvas.DrawText(line, x + shadow, y + shadow, _shadow);
                }

                if (_stroke.StrokeWidth > 0)
                    _canvas.DrawText(line, x, y, _stroke);

                _canvas.DrawText(line, x, y, _fill);
                y += lineHeight;
            }
        }

        _canvas.Flush();
    }

    /// <summary>取出 BGRA 原始像素（供 ffmpeg 管道使用）。</summary>
    public byte[] GetBgra()
    {
        var size = _width * _height * 4;
        _buffer ??= new byte[size];
        var pixels = _bitmap.GetPixels();
        if (pixels == IntPtr.Zero)
            return _buffer;
        Marshal.Copy(pixels, _buffer, 0, Math.Min(size, (int)_bitmap.ByteCount));
        if (Unpremultiply)
            UnpremultiplyInPlace(_buffer, _width * _height);
        return _buffer;
    }

    /// <summary>Skia 输出的是预乘 alpha，ffmpeg 需要直通 alpha，否则边缘会发黑。</summary>
    public bool Unpremultiply { get; set; } = true;

    private static readonly int[] AlphaScale = BuildAlphaScale();

    private static int[] BuildAlphaScale()
    {
        var table = new int[256];
        for (var a = 1; a < 256; a++)
            table[a] = 255 * 255 / a;
        return table;
    }

    private static void UnpremultiplyInPlace(byte[] buffer, int pixelCount)
    {
        for (var p = 0; p < pixelCount * 4; p += 4)
        {
            var a = buffer[p + 3];
            if (a == 0 || a == 255)
                continue;
            var s = AlphaScale[a];
            var b = buffer[p] * s / 255;
            var g = buffer[p + 1] * s / 255;
            var r = buffer[p + 2] * s / 255;
            buffer[p] = (byte)(b > 255 ? 255 : b);
            buffer[p + 1] = (byte)(g > 255 ? 255 : g);
            buffer[p + 2] = (byte)(r > 255 ? 255 : r);
        }
    }

    public byte[] RenderToPng(IReadOnlyList<SubtitleCue> cues, CueStyle style, double time)
    {
        Render(cues, style, time);
        using var image = SKImage.FromBitmap(_bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        return data.ToArray();
    }

    private static List<string> WrapText(string text, SKPaint paint, float maxWidth, int maxChars)
    {
        var charsPerLine = Math.Clamp(maxChars <= 0 ? OverlayLayout.HardMaxChars : maxChars,
            4, OverlayLayout.HardMaxChars);
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var current = string.Empty;

            foreach (var ch in paragraph)
            {
                var candidate = current + ch;
                var candidateWidth = paint.MeasureText(candidate);
                var tooLong = candidateWidth > maxWidth || candidate.Length > charsPerLine;
                if (tooLong && current.Length > 0)
                {
                    lines.Add(current.Trim());
                    current = ch.ToString();
                }
                else
                {
                    current = candidate;
                }
            }

            if (current.Trim().Length > 0)
                lines.Add(current.Trim());
        }

        return lines;
    }

    public static SKColor ParseColor(string? hex, SKColor fallback = default)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback == default ? SKColors.White : fallback;

        var s = hex.TrimStart('#');
        try
        {
            return s.Length switch
            {
                8 => new SKColor(
                    byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber),
                    byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber),
                    byte.Parse(s.Substring(6, 2), NumberStyles.HexNumber),
                    byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber)),
                6 => new SKColor(
                    byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber),
                    byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber),
                    byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber)),
                _ => SKColors.White
            };
        }
        catch
        {
            return SKColors.White;
        }
    }

    public void Dispose()
    {
        _canvas.Dispose();
        _bitmap.Dispose();
        _stroke.Dispose();
        _fill.Dispose();
        _shadow.Dispose();
    }
}
