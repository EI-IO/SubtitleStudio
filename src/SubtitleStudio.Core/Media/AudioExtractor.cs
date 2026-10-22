using System.Globalization;

namespace SubtitleStudio.Core.Media;

public static class AudioExtractor
{
    public const int SampleRate = 16000;

    /// <summary>抽取一条音轨为 16kHz / 单声道 / 16bit PCM wav（Whisper 的输入格式）。</summary>
    /// <param name="audioStreamIndex">ffmpeg 全局流索引；小于 0 表示取第一条音轨（0:a:0）。</param>
    public static async Task<string> ExtractWavAsync(
        string inputPath,
        string outputWavPath,
        double totalSeconds,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        int audioStreamIndex = -1)
    {
        var ffmpeg = FfmpegTools.FfmpegPath
                     ?? throw new InvalidOperationException("找不到 ffmpeg，请把 ffmpeg 放到 tools/ffmpeg/bin 目录。");

        var dir = Path.GetDirectoryName(outputWavPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var map = FfmpegTools.StreamMap(audioStreamIndex);

        using var process = FfmpegTools.Start(ffmpeg, new[]
        {
            "-hide_banner", "-y",
            "-i", inputPath,
            "-map", map,
            "-vn",
            "-ac", "1",
            "-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
            "-c:a", "pcm_s16le",
            "-progress", "pipe:1",
            "-nostats",
            outputWavPath
        }, redirectStdout: true);

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        // ffmpeg 的 -progress 每行一条 key=value，读到 null 说明进程结束了
        while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                long.TryParse(line["out_time_us=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) &&
                totalSeconds > 0)
            {
                progress?.Report(Math.Clamp(us / 1_000_000d / totalSeconds, 0, 1));
            }
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0 || !File.Exists(outputWavPath))
            throw new InvalidOperationException($"抽取音轨失败 (exit {process.ExitCode})：{ProcessText.Tail(stderr)}");

        progress?.Report(1);
        return outputWavPath;
    }
}
