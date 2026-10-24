using System.Globalization;

namespace SubtitleStudio.Core.Media;

/// <summary>把某一条音轨单独导出成音频文件（默认 m4a/AAC，剪辑软件直接能用）。</summary>
public static class AudioExporter
{
    public const string DefaultExtension = ".m4a";

    public static async Task<string> ExportTrackAsync(
        string inputPath,
        string outputPath,
        int audioStreamIndex,
        double totalSeconds,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = FfmpegTools.FfmpegPath
                     ?? throw new InvalidOperationException("找不到 ffmpeg，请把 ffmpeg 放到 tools/ffmpeg/bin 目录。");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var map = FfmpegTools.StreamMap(audioStreamIndex);
        var codec = outputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            ? new[] { "-c:a", "pcm_s16le" }
            : new[] { "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart" };

        var args = new List<string>
        {
            "-hide_banner", "-y",
            "-i", inputPath,
            "-map", map,
            "-vn"
        };
        args.AddRange(codec);
        args.AddRange(new[] { "-progress", "pipe:1", "-nostats", outputPath });

        using var process = FfmpegTools.Start(ffmpeg, args, redirectStdout: true);

        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (!line.StartsWith("out_time_us=", StringComparison.Ordinal) || totalSeconds <= 0)
                    continue;

                if (long.TryParse(line["out_time_us=".Length..], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var us))
                {
                    progress?.Report(Math.Clamp(us / 1_000_000d / totalSeconds, 0, 1));
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消时杀掉 ffmpeg，避免孤儿进程继续写目标文件
            FfmpegTools.TryKill(process);
            try { await stderrTask.ConfigureAwait(false); } catch { /* 进程被杀后读取随之结束 */ }
            TempWorkspace.TryDelete(outputPath);
            throw;
        }

        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            TryDelete(outputPath);
            throw new InvalidOperationException(
                $"导出音轨失败 (exit {process.ExitCode})\nffmpeg 输出：\n{ProcessText.Tail(stderr)}");
        }

        progress?.Report(1);
        return outputPath;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 删不掉就算了
        }
    }
}
