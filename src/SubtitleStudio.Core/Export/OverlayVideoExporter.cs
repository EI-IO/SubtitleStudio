using System.Diagnostics;
using System.Globalization;
using SubtitleStudio.Core.Media;
using SubtitleStudio.Core.Models;

namespace SubtitleStudio.Core.Export;

public enum OverlayFormat
{
    /// <summary>带 alpha 的 WebM（VP9），主流剪辑软件都认。</summary>
    WebmVp9Alpha,

    /// <summary>带 alpha 的 MOV（ProRes 4444），体积大但兼容性最好。</summary>
    MovProRes4444,

    /// <summary>普通 MP4（无透明通道，背景为黑色）。</summary>
    Mp4H264
}

public sealed record OverlayExportRequest(
    string OutputPath,
    OverlayFormat Format,
    int Width,
    int Height,
    double Fps,
    double Duration,
    IReadOnlyList<SubtitleCue> Cues,
    CueStyle Style)
{
    /// <summary>字号倍率；0 表示按画布高度 / 1080 推算。</summary>
    public double RenderScale { get; init; }

    /// <summary>紧凑画布（画面就是字幕本身）时文字垂直居中。</summary>
    public bool CenterVertically { get; init; }
}

public static class OverlayVideoExporter
{
    public static string ExtensionOf(OverlayFormat format) => format switch
    {
        OverlayFormat.WebmVp9Alpha => ".webm",
        OverlayFormat.MovProRes4444 => ".mov",
        _ => ".mp4"
    };

    /// <summary>把透明字幕图层逐帧喂给 ffmpeg，编码成可直接叠加的视频。</summary>
    public static async Task ExportAsync(
        OverlayExportRequest request,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = FfmpegTools.FfmpegPath
                     ?? throw new InvalidOperationException("找不到 ffmpeg，请把 ffmpeg 放到 tools/ffmpeg/bin 目录。");

        var width = Math.Max(2, request.Width - request.Width % 2);
        var height = Math.Max(2, request.Height - request.Height % 2);
        var fps = Math.Clamp(request.Fps, 1, 120);
        var totalFrames = Math.Max(1, (int)Math.Ceiling(request.Duration * fps));

        var dir = Path.GetDirectoryName(request.OutputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            EnsureWritable(dir);
        }

        var args = new List<string>
        {
            "-hide_banner", "-y",
            "-f", "rawvideo",
            "-pixel_format", "bgra",
            "-video_size", $"{width}x{height}",
            "-framerate", fps.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", "-",
            "-an"
        };

        switch (request.Format)
        {
            case OverlayFormat.WebmVp9Alpha:
                args.AddRange(new[]
                {
                    "-c:v", "libvpx-vp9",
                    "-pix_fmt", "yuva420p",
                    "-auto-alt-ref", "0",
                    "-b:v", "0",
                    "-crf", "30",
                    "-row-mt", "1",
                    "-deadline", "good",
                    "-cpu-used", "2"
                });
                break;

            case OverlayFormat.MovProRes4444:
                args.AddRange(new[]
                {
                    "-c:v", "prores_ks",
                    "-profile:v", "4444",
                    "-pix_fmt", "yuva444p10le",
                    "-vendor", "apl0"
                });
                break;

            default:
                args.AddRange(new[]
                {
                    "-c:v", "libx264",
                    "-pix_fmt", "yuv420p",
                    "-preset", "medium",
                    "-crf", "20",
                    "-movflags", "+faststart"
                });
                break;
        }

        args.Add("-progress");
        args.Add("pipe:1");
        args.Add("-nostats");
        args.Add(request.OutputPath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var a in args)
            process.StartInfo.ArgumentList.Add(a);

        process.Start();

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var progressTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                    long.TryParse(line["out_time_us=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
                {
                    var done = us / 1_000_000d / request.Duration;
                    progress?.Report(new ExportProgress(Math.Clamp(done, 0, 1), $"编码中… {done * 100:0}%"));
                }
            }
        }, cancellationToken);

        var renderScale = request.RenderScale > 0 ? request.RenderScale : height / CueStyle.BaseHeight;
        using var renderer = new SubtitleLayerRenderer(width, height, renderScale, request.CenterVertically);
        var stdin = process.StandardInput.BaseStream;

        // ffmpeg 可能因为输出路径不可写、已经跑起来等原因提前退出，
        // 这时写 stdin 会抛「管道已结束」。这里把它记下来，换成能看懂的错误。
        Exception? writeFailure = null;
        try
        {
            for (var i = 0; i < totalFrames; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var time = i / fps;
                renderer.Render(request.Cues, request.Style, time);
                var pixels = renderer.GetBgra();
                await stdin.WriteAsync(pixels.AsMemory(), cancellationToken).ConfigureAwait(false);

                if (i % 10 == 0)
                {
                    var fraction = (double)i / totalFrames;
                    progress?.Report(new ExportProgress(fraction, $"渲染帧 {i + 1}/{totalFrames}"));
                }
            }

            await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            FfmpegTools.TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            writeFailure = ex;
        }
        finally
        {
            try { stdin.Close(); } catch { /* 进程已经退出 */ }
        }

        if (writeFailure is not null)
            FfmpegTools.TryKill(process);

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        try { await progressTask.ConfigureAwait(false); } catch { /* 进程结束时读取循环可能被取消 */ }
        var stderr = await stderrTask.ConfigureAwait(false);

        if (writeFailure is not null)
        {
            TryDelete(request.OutputPath);
            var reason = writeFailure is IOException
                ? "编码器提前退出（管道已关闭）"
                : writeFailure.Message;
            throw new InvalidOperationException(
                $"导出失败：{reason}\nffmpeg 输出：\n{ProcessText.Tail(stderr)}", writeFailure);
        }

        if (process.ExitCode != 0 || !File.Exists(request.OutputPath))
        {
            TryDelete(request.OutputPath);
            throw new InvalidOperationException(
                $"导出失败 (exit {process.ExitCode})\nffmpeg 输出：\n{ProcessText.Tail(stderr)}");
        }

        progress?.Report(new ExportProgress(1, "导出完成"));
    }

    /// <summary>目标目录真的能写吗？很多「管道已结束」其实是这儿的问题。</summary>
    private static void EnsureWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".ss-write-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe)) { }
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new IOException($"导出目录不可写：{dir}（{ex.Message}）", ex);
        }
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
            // 删不掉就留着，用户自己处理
        }
    }
}

public sealed record ExportProgress(double Fraction, string Message);
