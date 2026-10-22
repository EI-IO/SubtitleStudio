using System.Text;

namespace SubtitleStudio.Core.Asr;

/// <summary>幻觉过滤参数。</summary>
public sealed record HallucinationOptions
{
    /// <summary>每秒字数上限：正常中文语速 4–8 字/秒，超过这个基本是凭空编的。</summary>
    public double MaxCharsPerSecond { get; init; } = 14;

    /// <summary>同一句话在相邻片段里最多连续出现几次。</summary>
    public int MaxRepeat { get; init; } = 3;

    /// <summary>丢掉这些"非语音"标注（静音、纯音乐、掌声），它们不是台词。</summary>
    public bool DropBracketOnly { get; init; } = true;

    /// <summary>丢掉单字符重复超过这么多次的片段（"啊啊啊啊啊啊啊"）。</summary>
    public int MaxSameCharRun { get; init; } = 8;
}

/// <summary>
/// Whisper 的经典幻觉：静音/纯音乐段凭空编出整句、片头片尾的套话、整段重复。
/// 识别后先过这一层，再进断句。
/// </summary>
public static class HallucinationFilter
{
    /// <summary>训练数据里带出来的固定套路，出现即丢。</summary>
    private static readonly string[] SpamPhrases =
    {
        "请不吝点赞",
        "订阅我的频道",
        "订阅转发打赏",
        "明镜与点点",
        "字幕由amara",
        "字幕由",
        "amara.org",
        "谢谢观看",
        "感谢观看",
        "谢谢大家观看",
        "thankyouforwatching",
        "thanksforwatching",
        "pleasesubscribe",
        "subtitlesby",
        "字幕组",
        "本文由",
        "（完）",
        "(完)"
    };

    /// <summary>括号里是这些"没有台词"的标注时才丢，像 [Bell] 这种真的音效标注留着。</summary>
    private static readonly string[] SilentTags =
    {
        "blank_audio", "blankaudio", "silence", "静音", "无声音",
        "music", "音乐", "背景音乐", "bgm",
        "applause", "掌声", "laughter", "笑声", "laughs",
        "inaudible", "unintelligible", "无法识别",
        "no speech", "nospeech", "空音频"
    };

    public static IReadOnlyList<AsrSegment> Apply(
        IEnumerable<AsrSegment> segments,
        HallucinationOptions? options = null)
    {
        options ??= new HallucinationOptions();
        var source = segments as IReadOnlyList<AsrSegment> ?? segments.ToList();
        var kept = new List<AsrSegment>(source.Count);

        string? previousKey = null;
        var repeat = 0;

        foreach (var segment in source)
        {
            var text = (segment.Text ?? string.Empty).Trim();
            if (text.Length == 0)
                continue;

            var duration = Math.Max(0.05, segment.End - segment.Start);
            var key = Normalize(text);

            if (key.Length == 0)
                continue;

            if (options.DropBracketOnly && IsBracketOnly(text) && IsSilentTag(key))
                continue;

            if (IsSpam(key))
                continue;

            if (HasLongCharRun(text, options.MaxSameCharRun))
                continue;

            // 语速异常：字数远超时长
            if (CountContentChars(text) / duration > options.MaxCharsPerSecond)
                continue;

            // 连续重复句：保留前 MaxRepeat 次
            if (previousKey == key)
            {
                repeat++;
                if (repeat >= options.MaxRepeat)
                    continue;
            }
            else
            {
                previousKey = key;
                repeat = 1;
            }

            kept.Add(segment);
        }

        return kept;
    }

    /// <summary>去掉空白、标点、括号后的比较键，用于判重。</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
                continue;
            if (char.IsPunctuation(ch) || char.IsSymbol(ch))
                continue;
            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    private static bool IsBracketOnly(string text)
    {
        var t = text.Trim();
        if (t.Length < 2)
            return false;

        var open = t[0] is '[' or '（' or '(' or '【';
        var close = t[^1] is ']' or '）' or ')' or '】';
        if (!open || !close)
            return false;

        // 里面只要还有成句的字就算正常内容
        return CountContentChars(t) <= 20;
    }

    /// <summary>括号标注是不是"静音/纯音乐/掌声"这类没有台词的东西。</summary>
    private static bool IsSilentTag(string normalizedKey)
    {
        foreach (var tag in SilentTags)
        {
            if (normalizedKey == Normalize(tag))
                return true;
        }

        return false;
    }

    private static bool IsSpam(string normalizedKey)
    {
        foreach (var phrase in SpamPhrases)
        {
            var n = Normalize(phrase);
            if (n.Length > 0 && normalizedKey.Contains(n, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasLongCharRun(string text, int maxRun)
    {
        if (maxRun <= 1)
            return false;

        var run = 1;
        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] == text[i - 1] && !char.IsWhiteSpace(text[i]))
            {
                run++;
                if (run > maxRun)
                    return true;
            }
            else
            {
                run = 1;
            }
        }

        return false;
    }

    private static int CountContentChars(string text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                count++;
        }

        return count;
    }
}
