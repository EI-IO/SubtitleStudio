using System.Globalization;
using System.Text;
using SubtitleStudio.Core.Models;

namespace SubtitleStudio.Core.Subtitles;

/// <summary>
/// SRT / VTT / ASS 三种主流字幕格式的写出器。
/// 时间统一用 毫秒 → "HH:MM:SS,mmm"，VTT 只把逗号换句点；ASS 需要转成 &amp;HAABBGGRR 颜色和 \N 换行。
/// </summary>
public static class SubtitleWriters
{
    public static string WriteSrt(IEnumerable<SubtitleCue> cues)
    {
        var sb = new StringBuilder();
        var index = 1;
        foreach (var cue in cues.OrderBy(c => c.Start))
        {
            sb.Append(index++).Append('\n');
            sb.Append(FormatSrtTime(cue.Start)).Append(" --> ").Append(FormatSrtTime(cue.End)).Append('\n');
            sb.Append(cue.Text.Replace("\r\n", "\n")).Append("\n\n");
        }

        return sb.ToString();
    }

    public static string WriteVtt(IEnumerable<SubtitleCue> cues)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in cues.OrderBy(c => c.Start))
        {
            sb.Append(FormatSrtTime(cue.Start).Replace(',', '.')).Append(" --> ")
              .Append(FormatSrtTime(cue.End).Replace(',', '.')).Append('\n');
            sb.Append(cue.Text.Replace("\r\n", "\n")).Append("\n\n");
        }

        return sb.ToString();
    }

    public static string WriteAss(IEnumerable<SubtitleCue> cues, CueStyle style, int width = 1920, int height = 1080)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine($"PlayResX: {width}");
        sb.AppendLine($"PlayResY: {height}");
        sb.AppendLine("WrapStyle: 0");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, " +
                      "Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, " +
                      "Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine($"Style: Default,{style.FontFamily},{style.FontSize:0.#}," +
                      $"{ToAssColor(style.PrimaryColor)},&H000000FF,{ToAssColor(style.OutlineColor)},&H64000000," +
                      $"{(style.Bold ? -1 : 0)},0,0,0,100,100,0,0,1,{style.OutlineWidth:0.#},{style.Shadow:0.#},2,40,40,{style.MarginBottom:0.#},1");
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var cue in cues.OrderBy(c => c.Start))
        {
            var text = cue.Text
                .Replace("\\", "\\\\")
                .Replace("{", "\\{")
                .Replace("}", "\\}")
                .Replace("\r\n", "\\N")
                .Replace("\n", "\\N");
            sb.AppendLine($"Dialogue: 0,{FormatAssTime(cue.Start)},{FormatAssTime(cue.End)},Default,,0,0,0,,{text}");
        }

        return sb.ToString();
    }

    public static void Save(string content, string path, bool withBom = false)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content, new UTF8Encoding(withBom));
    }

    private static string FormatSrtTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00},{ts.Milliseconds:000}";
    }

    private static string FormatAssTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 10:00}";
    }

    /// <summary>#AARRGGBB → &HAABBGGRR</summary>
    public static string ToAssColor(string argb)
    {
        var hex = argb.TrimStart('#');
        if (hex.Length == 6)
            hex = "FF" + hex;
        if (hex.Length != 8)
            return "&H00FFFFFF";

        var a = hex[..2];
        var r = hex.Substring(2, 2);
        var g = hex.Substring(4, 2);
        var b = hex.Substring(6, 2);
        return $"&H{a}{b}{g}{r}".ToUpperInvariant();
    }

    public static int ParsePixels(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
