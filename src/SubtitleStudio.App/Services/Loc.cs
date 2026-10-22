using System.Globalization;
using System.Text.Json;

namespace SubtitleStudio.App.Services;

/// <summary>
/// 极简国际化：键 → 文案。
/// 目前内置中/英两套，之后要加语种只要往 <see cref="Languages"/> 里加一份字典，
/// 或把同结构的 JSON 放到 <c>lang/</c> 目录由 <see cref="LoadFromFiles"/> 合并进来。
/// </summary>
public static class Loc
{
    public const string SystemLanguage = "system";

    private static readonly Dictionary<string, string> Zh = new(StringComparer.Ordinal)
    {
        ["Menu.LocalModels"] = "本地模型",
        ["Menu.SiliconFlow"] = "硅基流动",
        ["Menu.TeleAi"] = "天翼星辰",
        ["Menu.Settings"] = "设置",
        ["Menu.NoLocalModel"] = "models 里还没有模型，去「设置」里下载",
        ["Menu.RefreshModels"] = "刷新模型列表",
        ["Menu.NoCloudModel"] = "点「刷新模型列表」获取",
        ["Menu.NoKey"] = "还没配置",
        ["Menu.GoSettings"] = "去设置",
        ["Menu.Later"] = "稍后",
        ["Menu.NeedKeySuffix"] = "还没填，要现在去设置里填吗？",
        ["Status.NeedKey"] = "还没配置 {0} 的凭证，点「设置」填一下",
        ["Tracks.Delete.Title"] = "删除音轨",
        ["Tracks.Delete.Message"] = "要删除 {0} 吗？这条音轨和它的 {1} 条字幕会一起删掉，删除后不可恢复。",
        ["Tracks.Delete.Yes"] = "删除",
        ["Tracks.Delete.Cancel"] = "取消",
        ["Tracks.Clear.Title"] = "清空字幕",
        ["Tracks.Clear.Message"] = "要清空 {0} 的 {1} 条字幕吗？清空后不可恢复。",
        ["Tracks.Clear.Yes"] = "清空",
        ["Common.Cancel"] = "取消",
        ["Common.Save"] = "保存",
        ["Settings.Title"] = "设置",
        ["Style.TextColor"] = "文字颜色",
        ["Style.OutlineColor"] = "描边颜色",
    };

    private static readonly Dictionary<string, string> En = new(StringComparer.Ordinal)
    {
        ["Menu.LocalModels"] = "Local models",
        ["Menu.SiliconFlow"] = "SiliconFlow",
        ["Menu.TeleAi"] = "TeleAI",
        ["Menu.Settings"] = "Settings",
        ["Menu.NoLocalModel"] = "No model in models/ — download one in Settings",
        ["Menu.RefreshModels"] = "Refresh model list",
        ["Menu.NoCloudModel"] = "Click \"Refresh model list\" to fetch",
        ["Menu.NoKey"] = "Not configured",
        ["Menu.GoSettings"] = "Open settings",
        ["Menu.Later"] = "Later",
        ["Menu.NeedKeySuffix"] = "has no credential yet. Open settings now?",
        ["Status.NeedKey"] = "No credential for {0} — open Settings to fill it in",
        ["Tracks.Delete.Title"] = "Delete track",
        ["Tracks.Delete.Message"] = "Delete {0}? Its {1} subtitle(s) will be removed as well. This cannot be undone.",
        ["Tracks.Delete.Yes"] = "Delete",
        ["Tracks.Delete.Cancel"] = "Cancel",
        ["Tracks.Clear.Title"] = "Clear subtitles",
        ["Tracks.Clear.Message"] = "Clear {1} subtitle(s) of {0}? This cannot be undone.",
        ["Tracks.Clear.Yes"] = "Clear",
        ["Common.Cancel"] = "Cancel",
        ["Common.Save"] = "Save",
        ["Settings.Title"] = "Settings",
        ["Style.TextColor"] = "Text color",
        ["Style.OutlineColor"] = "Outline color",
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Languages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["zh"] = Zh,
            ["en"] = En
        };

    private static Dictionary<string, string> _current = Zh;

    /// <summary>当前语言代码（zh / en）。</summary>
    public static string Current { get; private set; } = "zh";

    /// <summary>可用语言列表。</summary>
    public static IReadOnlyList<string> Available => Languages.Keys.ToList();

    /// <summary>切换语言；未知语言回落到中文。</summary>
    public static void Use(string? language)
    {
        var code = (language ?? SystemLanguage).Trim();

        if (code.Equals(SystemLanguage, StringComparison.OrdinalIgnoreCase))
            code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

        if (Languages.TryGetValue(code, out var map))
        {
            _current = map;
            Current = code;
        }
        else
        {
            _current = Zh;
            Current = "zh";
        }
    }

    /// <summary>取文案；缺键时原样返回键名，方便发现漏翻。</summary>
    public static string T(string key, params object?[] args)
    {
        var text = _current.TryGetValue(key, out var value) ? value : key;

        if (args.Length == 0)
            return text;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }

    /// <summary>
    /// 扫描可执行文件目录下的 lang/*.json（如 lang/ja.json），文件名即语言代码，
    /// 内容形如 { "键": "文案" }，合并进对应语言。缺文件就跳过。
    /// </summary>
    public static void LoadFromFiles()
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "lang");
            if (!Directory.Exists(dir))
                return;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(file).Trim().ToLowerInvariant();
                if (code.Length == 0)
                    continue;

                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                if (entries is { Count: > 0 })
                    Merge(code, entries);
            }
        }
        catch
        {
            // 语言包坏了不该拖垮启动
        }
    }

    /// <summary>把外部 JSON（{ "键": "文案" }）合并进当前语言，便于后期加语种。</summary>
    public static void Merge(string language, IReadOnlyDictionary<string, string> entries)
    {
        if (!Languages.TryGetValue(language, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            Languages[language] = map;
        }

        foreach (var (key, value) in entries)
            map[key] = value;
    }
}
