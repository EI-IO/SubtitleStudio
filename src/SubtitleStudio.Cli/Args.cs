namespace SubtitleStudio.Cli;

/// <summary>极简参数解析：<c>--key value</c> 记为带值项，<c>--flag</c> 记为开关；不认识的参数直接忽略。</summary>
internal sealed class Args
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public static Args Parse(string[] argv)
    {
        var result = new Args();
        for (var i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (!a.StartsWith("--"))
                continue;
            var key = a[2..];
            var hasValue = i + 1 < argv.Length && !argv[i + 1].StartsWith("--");
            if (hasValue)
            {
                result._values[key] = argv[++i];
            }
            else
            {
                result._flags.Add(key);
            }
        }

        return result;
    }

    /// <summary>取带值参数的文本；没写或是开关形式时返回 null。</summary>
    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    /// <summary>开关或带值参数都算「出现过」，供只关心开/关的选项判断。</summary>
    public bool Has(string key) => _flags.Contains(key) || _values.ContainsKey(key);
}
