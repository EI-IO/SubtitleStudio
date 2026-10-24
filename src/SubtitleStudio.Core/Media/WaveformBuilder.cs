using System.Globalization;

namespace SubtitleStudio.Core.Media;

/// <summary>波形峰值数据，Min/Max 均已归一化到 [-1, 1]。</summary>
public sealed class WaveformData
{
    public required float[] Min { get; init; }
    public required float[] Max { get; init; }
    public required int PeaksPerSecond { get; init; }
    public required double Duration { get; init; }
}

/// <summary>
/// 生成时间轴用的波形峰值。为了让长视频也能秒出，解码时降到 8 kHz 单声道 s16le，
/// 按「每秒 peaksPerSecond 个桶」统计每桶的 min/max，画的时候直接按桶取点。
/// </summary>
public static class WaveformBuilder
{
    private const int DecodeSampleRate = 8000;

    public static async Task<WaveformData> BuildAsync(
        string inputPath,
        double durationSeconds,
        int peaksPerSecond = 100,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default,
        int audioStreamIndex = -1)
    {
        var ffmpeg = FfmpegTools.FfmpegPath
                     ?? throw new InvalidOperationException("找不到 ffmpeg。");

        peaksPerSecond = Math.Clamp(peaksPerSecond, 10, 400);
        var bucketSize = Math.Max(1, DecodeSampleRate / peaksPerSecond);
        var expectedBuckets = Math.Max(1, (int)Math.Ceiling(durationSeconds * peaksPerSecond));
        var map = FfmpegTools.StreamMap(audioStreamIndex);

        var mins = new List<float>(expectedBuckets);
        var maxs = new List<float>(expectedBuckets);

        using var process = FfmpegTools.Start(ffmpeg, new[]
        {
            "-hide_banner", "-v", "error", "-nostdin",
            "-i", inputPath,
            "-map", map,
            "-vn", "-ac", "1",
            "-ar", DecodeSampleRate.ToString(CultureInfo.InvariantCulture),
            "-f", "s16le",
            "-"
        }, redirectStdout: true);

        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var stream = process.StandardOutput.BaseStream;
        var buffer = new byte[1 << 16];
        var leftover = 0;
        long samplesRead = 0;

        float curMin = 0f, curMax = 0f;
        var curCount = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(buffer.AsMemory(leftover), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                var total = leftover + read;
                var usable = total - (total % 2);
                for (var i = 0; i < usable; i += 2)
                {
                    var sample = (short)(buffer[i] | (buffer[i + 1] << 8)) / 32768f;
                    if (sample < curMin) curMin = sample;
                    if (sample > curMax) curMax = sample;
                    samplesRead++;

                    if (++curCount >= bucketSize)
                    {
                        mins.Add(curMin);
                        maxs.Add(curMax);
                        curMin = curMax = 0f;
                        curCount = 0;
                    }
                }

                leftover = total - usable;
                if (leftover > 0)
                    buffer[0] = buffer[usable];

                if (expectedBuckets > 0)
                    progress?.Report(Math.Clamp((double)mins.Count / expectedBuckets, 0, 1));
            }
        }
        catch (OperationCanceledException)
        {
            // 取消时杀掉 ffmpeg，长视频的解码不会留在后台空跑
            FfmpegTools.TryKill(process);
            try { await stderrTask.ConfigureAwait(false); } catch { /* 进程被杀后读取随之结束 */ }
            throw;
        }

        if (curCount > 0)
        {
            mins.Add(curMin);
            maxs.Add(curMax);
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        _ = await stderrTask.ConfigureAwait(false);

        if (mins.Count == 0)
            throw new InvalidOperationException("无法解码音频轨，可能是纯视频文件。");

        // 安静素材（-18dB 的配乐之类）在时间轴上几乎是一条线，按峰值自适应放大，最多 8 倍
        var peak = 0.05f;
        for (var i = 0; i < mins.Count; i++)
        {
            var a = Math.Abs(mins[i]);
            if (a > peak) peak = a;
            var b = Math.Abs(maxs[i]);
            if (b > peak) peak = b;
        }

        var gain = Math.Min(1f / peak, 8f);
        for (var i = 0; i < mins.Count; i++)
        {
            mins[i] = Math.Clamp(mins[i] * gain, -1f, 1f);
            maxs[i] = Math.Clamp(maxs[i] * gain, -1f, 1f);
        }

        progress?.Report(1);
        return new WaveformData
        {
            Min = mins.ToArray(),
            Max = maxs.ToArray(),
            PeaksPerSecond = peaksPerSecond,
            Duration = durationSeconds
        };
    }
}
