using System.Globalization;
using System.Text.Json;
using SubtitleStudio.Core.Models;

namespace SubtitleStudio.Core.Media;

/// <summary>
/// 用 ffprobe 读媒体信息。除了时长/分辨率，重点是**把所有音轨都列出来**，
/// 后面每条音轨单独抽音频、单独识别、单独出字幕。
/// </summary>
public static class MediaProbe
{
    public static async Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        var ffprobe = FfmpegTools.FfprobePath
                      ?? throw new InvalidOperationException("找不到 ffprobe，请把 ffmpeg 放到 tools/ffmpeg/bin 目录。");

        var (code, stdout, stderr) = await RunCaptureAsync(ffprobe, new[]
        {
            "-v", "error",
            "-print_format", "json",
            "-show_format",
            "-show_streams",
            path
        }, cancellationToken).ConfigureAwait(false);

        if (code != 0)
            throw new InvalidOperationException($"ffprobe 读取失败:\n{stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        // ffprobe 的 duration 是字符串，用不变文化解析，避免中文区域把小数点当千分位
        double duration = 0;
        if (root.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var dur) &&
            double.TryParse(dur.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            duration = d;
        }

        int width = 0, height = 0, audioIndex = -1;
        double fps = 0;
        var audioStreams = new List<AudioStreamInfo>();

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var type = stream.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                if (type == "audio")
                {
                    var index = stream.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;
                    var codec = stream.TryGetProperty("codec_name", out var c) ? c.GetString() ?? "audio" : "audio";
                    var channels = stream.TryGetProperty("channels", out var ch) ? ch.GetInt32() : 0;
                    string? language = null, title = null;
                    if (stream.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
                    {
                        if (tags.TryGetProperty("language", out var lang))
                            language = lang.GetString();
                        if (tags.TryGetProperty("title", out var ti))
                            title = ti.GetString();
                    }

                    audioStreams.Add(new AudioStreamInfo(index, audioStreams.Count + 1, codec, channels, language, title));

                    if (audioIndex < 0)
                    {
                        audioIndex = index;
                        if (duration <= 0 &&
                            stream.TryGetProperty("duration", out var sd) &&
                            double.TryParse(sd.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var sdd))
                        {
                            duration = sdd;
                        }
                    }
                }
                else if (type == "video" && width == 0)
                {
                    // 只取第一条视频流；avg_frame_rate 有时是 0/0，退回 r_frame_rate
                    width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                    height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                    fps = ParseRate(stream.TryGetProperty("avg_frame_rate", out var r) ? r.GetString() : null);
                    if (fps <= 0)
                        fps = ParseRate(stream.TryGetProperty("r_frame_rate", out var r2) ? r2.GetString() : null);
                }
            }
        }

        if (duration <= 0)
            throw new InvalidOperationException("无法读取媒体时长。");

        return new MediaInfo(path, duration, width, height, fps > 0 ? fps : 30, audioIndex >= 0, audioIndex)
        {
            AudioStreams = audioStreams
        };
    }

    private static double ParseRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;
        var parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den > 0)
        {
            return num / den;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var single) ? single : 0;
    }

    private static async Task<(int Code, string StdOut, string StdErr)> RunCaptureAsync(
        string exe,
        IEnumerable<string> args,
        CancellationToken cancellationToken)
    {
        using var process = FfmpegTools.Start(exe, args, redirectStdout: true);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }
}
