using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace SubtitleStudio.App.Services;

/// <summary>一种界面语言的元信息；Name 是语言自己的名字（简体中文 / English / 日本語）。</summary>
public sealed record LanguageInfo(string Code, string Name);

/// <summary>
/// 界面文案：键 → 文案。内置 zh/en 两份 JSON（编译进程序），
/// 再把可执行文件目录下 lang/*.json 的外部语言包合并进来——加语言只要放一个文件。
/// 语言包统一格式：{ "code": "ja", "name": "日本語", "strings": { ... } }。
/// 切换语言会更新 Application 资源里的 L.* 项，XAML 的 DynamicResource 立即重绑，不用重开窗口。
/// </summary>
public static class Loc
{
    public const string SystemLanguage = "system";

    private static readonly Dictionary<string, LanguageInfo> Meta = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, string>> Languages =
        new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> _current = new(StringComparer.Ordinal);
    private static ResourceDictionary? _resource;

    /// <summary>当前语言代码（zh / en / 外部包代码）。</summary>
    public static string Current { get; private set; } = "zh";

    /// <summary>语言切换后触发；C# 侧拼出来的文案（状态栏、菜单、面板标题）靠它刷新。</summary>
    public static event Action? Changed;

    /// <summary>可用语言，简体中文和 English 排前面，其余按代码排序。</summary>
    public static IReadOnlyList<LanguageInfo> Available
    {
        get
        {
            return Languages.Keys
                .Select(code => Meta.TryGetValue(code, out var info) ? info : new LanguageInfo(code, code))
                .OrderBy(l => l.Code is "zh" ? 0 : l.Code is "en" ? 1 : 2)
                .ThenBy(l => l.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>启动时调用一次：加载内置语言包，再扫描外部 lang 目录。</summary>
    public static void Load()
    {
        Languages.Clear();
        Meta.Clear();

        LoadPack(ReadEmbedded("zh.json"), "zh");
        LoadPack(ReadEmbedded("en.json"), "en");
        LoadFromFiles();

        // 内置包都读不到（理论上不会）时至少保证界面不是全空
        if (Languages.Count == 0)
            Merge("zh", new Dictionary<string, string>());
    }

    /// <summary>切换语言；未知代码回落到系统语言，再不行回落到中文。</summary>
    public static void Use(string? language)
    {
        var code = Normalize(language);
        if (!Languages.TryGetValue(code, out var map))
        {
            code = Languages.ContainsKey("zh") ? "zh" : Languages.Keys.FirstOrDefault() ?? "zh";
            map = Languages.TryGetValue(code, out var fallback) ? fallback : new Dictionary<string, string>();
        }

        Current = code;
        _current = map;
        ApplyResources();
        Changed?.Invoke();
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

    /// <summary>语言自己的名字，例如 zh → 简体中文。</summary>
    public static string NameOf(string code) =>
        Meta.TryGetValue(code, out var info) ? info.Name : code;

    /// <summary>
    /// 扫描可执行文件目录下的 lang/*.json，文件名即语言代码（文件里也声明了 code，以文件内为准）。
    /// 每次调用都会合并，便于运行中补装语言包后重新打开选择窗。
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
                var fallback = Path.GetFileNameWithoutExtension(file).Trim().ToLowerInvariant();
                try
                {
                    LoadPack(File.ReadAllText(file), fallback);
                }
                catch
                {
                    // 单个语言包坏了只跳过它
                }
            }
        }
        catch
        {
            // 语言包坏了不该拖垮启动
        }
    }

    /// <summary>把一份外部条目并进某个语言（供测试 / 扩展用）。</summary>
    public static void Merge(string language, IReadOnlyDictionary<string, string> entries)
    {
        var code = language.Trim().ToLowerInvariant();
        if (code.Length == 0)
            return;

        if (!Languages.TryGetValue(code, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            Languages[code] = map;
        }

        foreach (var (key, value) in entries)
            map[key] = value;

        if (!Meta.ContainsKey(code))
            Meta[code] = new LanguageInfo(code, code);
    }

    private static void LoadPack(string? json, string? fallbackCode)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return;

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        string? code;
        string? name;

        if (root.TryGetProperty("strings", out var strings) && strings.ValueKind == JsonValueKind.Object)
        {
            // 统一格式：{ "code": "...", "name": "...", "strings": { ... } }
            code = GetString(root, "code");
            name = GetString(root, "name");

            foreach (var property in strings.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    entries[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }
        else
        {
            // 兼容老的平铺格式：直接 { "键": "文案" }
            code = GetString(root, "code");
            name = GetString(root, "name");

            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String
                    && property.Name is not ("code" or "name"))
                {
                    entries[property.Name] = property.Value.GetString() ?? string.Empty;
                }
            }
        }

        code = (code ?? fallbackCode)?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code) || entries.Count == 0)
            return;

        Merge(code, entries);
        Meta[code] = new LanguageInfo(code, string.IsNullOrWhiteSpace(name) ? code : name.Trim());
    }

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadEmbedded(string fileName)
    {
        try
        {
            using var stream = AssetLoader.Open(
                new Uri($"avares://SubtitleStudio/Assets/lang/{fileName}", UriKind.Absolute));
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }

    private static string Normalize(string? language)
    {
        var code = (language ?? SystemLanguage).Trim().ToLowerInvariant();

        if (code is "" or SystemLanguage)
        {
            var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            return Languages.ContainsKey(system)
                ? system
                : Languages.ContainsKey("en") ? "en" : "zh";
        }

        // 接受 zh-CN / en-US 这类带地区的代码
        if (!Languages.ContainsKey(code) && code.Length > 2)
        {
            var two = code[..2];
            if (Languages.ContainsKey(two))
                return two;
        }

        return code;
    }

    /// <summary>把当前语言写进 Application 资源（键名加 L. 前缀），XAML 用 DynamicResource 取。</summary>
    private static void ApplyResources()
    {
        var app = Application.Current;
        if (app is null)
            return;

        if (_resource is null)
        {
            _resource = new ResourceDictionary();
            app.Resources.MergedDictionaries.Add(_resource);
        }

        _resource.Clear();
        foreach (var (key, value) in _current)
            _resource["L." + key] = value;
    }
}
