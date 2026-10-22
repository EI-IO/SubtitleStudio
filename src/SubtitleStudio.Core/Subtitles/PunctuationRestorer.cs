using System.Text;
using SubtitleStudio.Core.Asr;

namespace SubtitleStudio.Core.Subtitles;

/// <summary>
/// whisper 的中文输出经常一逗到底、甚至完全没有标点。这里按"词间停顿"补标点：
/// 停顿超过 0.8 秒补句号，超过 0.3 秒补逗号；没有词级时间戳时按字数和句读靠猜。
/// </summary>
public static class PunctuationRestorer
{
    private const string Comma = "，";
    private const string Period = "。";

    private static readonly char[] Trailing =
    {
        '。', '！', '？', '；', '，', '、', '.', '!', '?', ';', ',', ':', '：', '…', '~', '～'
    };

    /// <summary>给每个片段补标点，返回新的片段（时间戳不变）。</summary>
    public static IReadOnlyList<AsrSegment> Restore(
        IEnumerable<AsrSegment> segments,
        double commaPause = 0.3,
        double periodPause = 0.8,
        int chunkChars = 14)
    {
        var result = new List<AsrSegment>();
        foreach (var segment in segments)
        {
            var text = (segment.Text ?? string.Empty).Trim();
            if (text.Length == 0)
                continue;

            // 音效标注（[Bell]、[笑声]）不加标点，加了反而怪
            if (IsBracketOnly(text))
            {
                result.Add(segment with { Text = text });
                continue;
            }

            var punctuated = segment.Words is { Count: > 1 }
                ? FromWords(segment, commaPause, periodPause)
                : FromText(text, chunkChars);

            result.Add(segment with { Text = punctuated });
        }

        return result;
    }

    /// <summary>整段就是一个括号标注，比如 [Bell]、(笑声)。</summary>
    private static bool IsBracketOnly(string text)
    {
        var t = text.Trim();
        if (t.Length < 2)
            return false;

        var open = t[0] is '[' or '（' or '(' or '【';
        var close = t[^1] is ']' or '）' or ')' or '】';
        return open && close;
    }

    /// <summary>有词级时间戳时按真实停顿标点。</summary>
    private static string FromWords(AsrSegment segment, double commaPause, double periodPause)
    {
        var words = segment.Words!;
        var sb = new StringBuilder();

        for (var i = 0; i < words.Count; i++)
        {
            var word = (words[i].Text ?? string.Empty).Trim();
            if (word.Length == 0)
                continue;

            sb.Append(word);

            if (i + 1 >= words.Count)
                break;

            var gap = words[i + 1].Start - words[i].End;
            if (HasTrailing(word))
                continue;

            if (gap >= periodPause)
                sb.Append(Period);
            else if (gap >= commaPause)
                sb.Append(Comma);
        }

        return EnsureEnding(sb.ToString());
    }

    /// <summary>没有词级时间戳：长句按字数切成若干口气，再补句号。</summary>
    private static string FromText(string text, int chunkChars)
    {
        if (chunkChars < 4)
            chunkChars = 4;

        var sb = new StringBuilder(text.Length + 8);
        var run = 0;

        foreach (var ch in text)
        {
            if (HasTrailingChar(ch) || IsCjkPunctuation(ch))
            {
                sb.Append(ch);
                run = 0;
                continue;
            }

            sb.Append(ch);
            if (!char.IsWhiteSpace(ch) && char.IsLetterOrDigit(ch))
                run++;

            // 已经到一口气的长度，且下一个字不是标点，就断开
            if (run >= chunkChars)
            {
                sb.Append(Comma);
                run = 0;
            }
        }

        var output = sb.ToString();
        // 连续的逗号收成一个
        while (output.Contains("，，", StringComparison.Ordinal))
            output = output.Replace("，，", "，", StringComparison.Ordinal);

        return EnsureEnding(output);
    }

    private static string EnsureEnding(string text)
    {
        var trimmed = text.Trim().TrimEnd('，', '、', ',');
        if (trimmed.Length == 0)
            return trimmed;

        return HasTrailing(trimmed) ? trimmed : trimmed + Period;
    }

    private static bool HasTrailing(string text) =>
        text.Length > 0 && Array.IndexOf(Trailing, text[^1]) >= 0;

    private static bool HasTrailingChar(char ch) => Array.IndexOf(Trailing, ch) >= 0;

    private static bool IsCjkPunctuation(char ch) =>
        ch is '，' or '。' or '、' or '；' or '：' or '！' or '？' or '…' or '《' or '》' or '“' or '”' or '（' or '）';
}
