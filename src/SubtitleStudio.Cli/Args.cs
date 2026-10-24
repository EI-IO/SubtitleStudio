namespace SubtitleStudio.Cli;

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

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public bool Has(string key) => _flags.Contains(key) || _values.ContainsKey(key);
}
