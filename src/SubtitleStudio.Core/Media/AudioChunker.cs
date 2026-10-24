namespace SubtitleStudio.Core.Media;

/// <summary>
/// 长音频的切片规划：在静音处下刀，而不是按固定秒数硬切——
/// 硬切会把一个词劈成两半，两半送给识别引擎都听不全，识别率肉眼可见地掉。
/// 直接在 PCM 上做能量分析（20ms 一格看峰值），不用再跑一趟 ffmpeg silencedetect。
/// </summary>
public static class AudioChunker
{
    /// <summary>一片音频的起止秒数；相邻片之间有重叠，重叠区由调用方按中点去重。</summary>
    public readonly record struct Chunk(double StartSeconds, double EndSeconds);

    /// <summary>静音判定：窗口内峰值低于这个振幅（16bit PCM，约 -36dB）就算安静。</summary>
    private const int SilencePeak = 500;

    /// <summary>短于 0.3 秒的安静多半是说话换气，不能当下刀点。</summary>
    private const double MinSilenceSeconds = 0.3;

    /// <summary>能量分析窗口：20ms 一格。</summary>
    private const double WindowSeconds = 0.02;

    /// <summary>
    /// 把整段 16k 单声道 PCM16 规划成若干片：片长尽量用满 <paramref name="maxChunkSeconds"/>，
    /// 切点优先落在静音中点；一段找不到静音才退回硬切。
    /// </summary>
    public static IReadOnlyList<Chunk> Plan(byte[] pcm16, double maxChunkSeconds, double overlapSeconds, int sampleRate)
    {
        var total = pcm16.Length / (sampleRate * 2.0);
        if (total <= maxChunkSeconds)
            return new[] { new Chunk(0, total) };

        var silences = FindSilenceMidpoints(pcm16, sampleRate);
        var cuts = new List<double>();
        var cursor = 0.0;

        while (cursor + maxChunkSeconds < total)
        {
            // 目标切点：让「下一片起点 = 切点 - 半个重叠」之后片长仍不超限，所以目标要减掉整个重叠
            var target = cursor + maxChunkSeconds - overlapSeconds;
            var window = Math.Min(15, maxChunkSeconds * 0.4);

            // 候选里挑最晚的：片长用满，总片数最少
            var cut = silences
                .Where(m => m >= target - window && m <= target)
                .OrderByDescending(m => m)
                .FirstOrDefault();
            if (cut <= cursor + 1)
                cut = target; // 这段没有静音，只能硬切

            cuts.Add(cut);
            cursor = cut;
        }

        // 边界序列 0 = b0 < b1 < … < bn = total；片 i 的实际音频是 [b_i - 半重叠, b_{i+1} + 半重叠]，
        // 责任区间正好 [b_i, b_{i+1})，与 TeleAi 原来的「重叠区中点去重」完全等价
        var half = overlapSeconds / 2;
        var chunks = new List<Chunk>(cuts.Count + 1);
        for (var i = 0; i <= cuts.Count; i++)
        {
            var start = i == 0 ? 0 : cuts[i - 1] - half;
            var end = i == cuts.Count ? total : cuts[i] + half;
            chunks.Add(new Chunk(start, Math.Min(end, total)));
        }

        return chunks;
    }

    /// <summary>返回所有「足够长的安静段」的中点（秒），按时间升序。</summary>
    private static List<double> FindSilenceMidpoints(byte[] pcm, int sampleRate)
    {
        var windowBytes = Math.Max(2, (int)(sampleRate * WindowSeconds) * 2);
        var result = new List<double>();
        var runStart = -1; // 连续安静窗口的起始序号（窗口下标）

        for (var offset = 0; offset + 2 <= pcm.Length; offset += windowBytes)
        {
            var end = Math.Min(offset + windowBytes, pcm.Length);
            var peak = 0;
            for (var i = offset; i + 1 < end; i += 2)
            {
                // short.MinValue 取绝对值会溢出，先转 int
                var s = Math.Abs((short)(pcm[i] | pcm[i + 1] << 8));
                if (s > peak)
                    peak = s;
            }

            var index = offset / windowBytes;
            if (peak < SilencePeak)
            {
                if (runStart < 0)
                    runStart = index;
            }
            else if (runStart >= 0)
            {
                AddIfLongEnough(result, runStart, index, windowBytes, sampleRate);
                runStart = -1;
            }
        }

        if (runStart >= 0)
            AddIfLongEnough(result, runStart, (pcm.Length + windowBytes - 1) / windowBytes, windowBytes, sampleRate);

        return result;
    }

    private static void AddIfLongEnough(List<double> result, int startWindow, int endWindow, int windowBytes, int sampleRate)
    {
        var startSeconds = startWindow * windowBytes / (sampleRate * 2.0);
        var endSeconds = endWindow * windowBytes / (sampleRate * 2.0);
        if (endSeconds - startSeconds >= MinSilenceSeconds)
            result.Add((startSeconds + endSeconds) / 2);
    }
}
