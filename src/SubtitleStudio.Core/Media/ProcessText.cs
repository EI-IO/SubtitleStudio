namespace SubtitleStudio.Core.Media;

/// <summary>ffmpeg / ffprobe 的 stderr 动辄上百行，界面里只留最后几行真正有用的。</summary>
public static class ProcessText
{
    public static string Tail(string? text, int lines = 8)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "没有更多信息";

        var all = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("frame=", StringComparison.Ordinal) &&
                        !l.StartsWith("size=", StringComparison.Ordinal) &&
                        !l.StartsWith("bitrate=", StringComparison.Ordinal) &&
                        !l.StartsWith("speed=", StringComparison.Ordinal) &&
                        !l.StartsWith("progress=", StringComparison.Ordinal) &&
                        !l.StartsWith("out_time", StringComparison.Ordinal))
            .ToArray();

        if (all.Length == 0)
            return "没有更多信息";

        return string.Join('\n', all.Skip(Math.Max(0, all.Length - lines)));
    }
}
