using System.Text;
using SubtitleStudio.Core.Asr;
using SubtitleStudio.Core.Models;

namespace SubtitleStudio.Core.Subtitles;

/// <summary>
/// 把识别结果切成适合当字幕的短句：
/// 优先在标点处断开，其次按最大字数、最大时长硬断，最后保证最小显示时长。
/// </summary>
public static class CueSegmenter
{
    private const string SentenceEnd = "。！？!?…";
    private const string ClauseEnd = "，,、；;：:";

    public static List<SubtitleCue> Segment(
        IEnumerable<AsrSegment> segments,
        int maxChars = 20,
        double maxDuration = 6.0,
        double minDuration = 0.9)
    {
        var cues = new List<SubtitleCue>();

        foreach (var segment in segments)
        {
            var text = (segment.Text ?? string.Empty).Trim();
            if (text.Length == 0 || segment.End - segment.Start <= 0.01)
                continue;

            var tokens = segment.Words is { Count: > 0 }
                ? NormalizeWords(segment.Words!, segment)
                : SyntheticWords(text, segment.Start, segment.End);

            BuildCues(tokens, cues, maxChars, maxDuration);
        }

        FixTiming(cues, minDuration, maxDuration);
        return cues;
    }

    private static List<AsrWord> NormalizeWords(IReadOnlyList<AsrWord> words, AsrSegment segment)
    {
        var result = new List<AsrWord>(words.Count);
        var last = segment.Start;
        foreach (var w in words)
        {
            var t = (w.Text ?? string.Empty).Trim();
            if (t.Length == 0)
                continue;
            // 时间戳必须单调递增：起点不能早于上一个词的结尾，否则字幕会互相重叠
            var start = Math.Max(last, Math.Min(w.Start, segment.End));
            var end = Math.Max(start + 0.02, Math.Min(Math.Max(w.End, w.Start), segment.End));
            result.Add(new AsrWord(t, start, end, w.Probability));
            last = end;
        }

        if (result.Count == 0)
            return SyntheticWords(segment.Text ?? string.Empty, segment.Start, segment.End);
        return result;
    }

    /// <summary>没有词级时间戳时，按字符数把整段时长均分。</summary>
    private static List<AsrWord> SyntheticWords(string text, double start, double end)
    {
        var pieces = SplitPieces(text);
        var total = pieces.Sum(p => Math.Max(1, p.Length));
        var perChar = (end - start) / Math.Max(1, total);
        var words = new List<AsrWord>(pieces.Count);
        var cursor = start;
        foreach (var piece in pieces)
        {
            var span = Math.Max(1, piece.Length) * perChar;
            words.Add(new AsrWord(piece, cursor, Math.Min(end, cursor + span)));
            cursor += span;
        }

        return words;
    }

    /// <summary>中文按字切，英文/数字按单词切，标点跟随前一个片段。</summary>
    private static List<string> SplitPieces(string text)
    {
        var pieces = new List<string>();
        var sb = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                Flush();
                continue;
            }

            if (IsLatin(ch))
            {
                sb.Append(ch);
                continue;
            }

            Flush();
            pieces.Add(ch.ToString());
        }

        Flush();
        return pieces;

        void Flush()
        {
            if (sb.Length > 0)
            {
                pieces.Add(sb.ToString());
                sb.Clear();
            }
        }
    }

    private static bool IsLatin(char ch) =>
        char.IsLetterOrDigit(ch) && ch < 0x2E80 || ch is '\'' or '-' or '.' or '&' or '@' or '#';

    private static void BuildCues(List<AsrWord> tokens, List<SubtitleCue> cues, int maxChars, double maxDuration)
    {
        if (tokens.Count == 0)
            return;

        var sb = new StringBuilder();
        double start = tokens[0].Start;
        double end = tokens[0].Start;

        foreach (var token in tokens)
        {
            var addsSpace = sb.Length > 0 && IsLatin(token.Text[0]) && IsLatin(sb[^1]);
            var extra = token.Text.Length + (addsSpace ? 1 : 0);

            if (sb.Length > 0 && sb.Length + extra > maxChars)
            {
                cues.Add(Make(start, end, sb));
                sb.Clear();
                start = token.Start;
            }

            if (addsSpace)
                sb.Append(' ');
            sb.Append(token.Text);
            end = token.End;

            var lastChar = token.Text[^1];
            var atClause = SentenceEnd.IndexOf(lastChar) >= 0 || ClauseEnd.IndexOf(lastChar) >= 0;
            if (atClause && sb.Length >= 4)
            {
                cues.Add(Make(start, end, sb));
                sb.Clear();
                start = end;
            }
            else if (end - start >= maxDuration)
            {
                cues.Add(Make(start, end, sb));
                sb.Clear();
                start = end;
            }
        }

        if (sb.Length > 0)
            cues.Add(Make(start, end, sb));

        static SubtitleCue Make(double s, double e, StringBuilder text) =>
            new() { Start = s, End = Math.Max(s + 0.2, e), Text = text.ToString().Trim() };
    }

    /// <summary>保证最短显示时长、时间单调递增且不重叠。</summary>
    private static void FixTiming(List<SubtitleCue> cues, double minDuration, double maxDuration)
    {
        cues.RemoveAll(c => string.IsNullOrWhiteSpace(c.Text));

        for (var i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];

            // 起点不能早于上一条的结尾，否则时间轴里会叠在一起
            if (i > 0)
                cue.Start = Math.Max(cue.Start, cues[i - 1].End + 0.02);

            if (cue.End - cue.Start < minDuration)
                cue.End = cue.Start + minDuration;

            if (cue.End - cue.Start > maxDuration)
                cue.End = cue.Start + maxDuration;

            if (i + 1 < cues.Count && cue.End > cues[i + 1].Start)
                cue.End = Math.Max(cue.Start + 0.2, cues[i + 1].Start - 0.02);

            if (cue.End <= cue.Start)
                cue.End = cue.Start + 0.2;
        }

        // 极端情况下（后面全是零长度）再压一遍，保证严格递增
        for (var i = 1; i < cues.Count; i++)
        {
            if (cues[i].Start < cues[i - 1].End)
                cues[i].Start = cues[i - 1].End;
            if (cues[i].End <= cues[i].Start)
                cues[i].End = cues[i].Start + 0.2;
        }

        MergeOrphans(cues);
    }

    /// <summary>只剩标点/括号的碎片并回上一条，避免出现单字 "] " 这种字幕。</summary>
    private static void MergeOrphans(List<SubtitleCue> cues)
    {
        for (var i = cues.Count - 1; i > 0; i--)
        {
            var text = cues[i].Text.Trim();
            if (text.Length == 0 || text.Any(char.IsLetterOrDigit))
                continue;

            cues[i - 1].Text = (cues[i - 1].Text + text).Trim();
            cues.RemoveAt(i);
        }
    }
}
