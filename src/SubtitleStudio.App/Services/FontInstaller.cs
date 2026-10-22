using System.Text.Json;
using SkiaSharp;

namespace SubtitleStudio.App.Services;

/// <summary>
/// 用户导入的字体：复制到本地缓存目录，记住「家族名 → 文件」映射。
/// 不依赖系统安装，重启后仍在；渲染时由 <see cref="Core.Export.SubtitleLayerRenderer.FontFileResolver"/> 按文件加载。
/// </summary>
public static class FontInstaller
{
    private sealed record Entry(string Family, string File);

    private static readonly List<Entry> Entries = new();
    private static bool _loaded;

    public static string CacheDirectory { get; } = ResolveCacheDirectory();

    private static string IndexPath => Path.Combine(CacheDirectory, "fonts.json");

    /// <summary>已导入的字体家族名（按导入顺序）。</summary>
    public static IReadOnlyList<string> Families
    {
        get
        {
            EnsureLoaded();
            return Entries.Select(e => e.Family).ToList();
        }
    }

    /// <summary>家族名 → 字体文件路径；没有导入过返回 null。</summary>
    public static string? Resolve(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return null;

        EnsureLoaded();
        var hit = Entries.FirstOrDefault(e => string.Equals(e.Family, family, StringComparison.OrdinalIgnoreCase));
        return hit is not null && System.IO.File.Exists(Path.Combine(CacheDirectory, hit.File))
            ? Path.Combine(CacheDirectory, hit.File)
            : null;
    }

    /// <summary>导入一个字体文件，返回字体家族名；失败返回 null。</summary>
    public static string? Install(string sourcePath)
    {
        EnsureLoaded();

        if (!System.IO.File.Exists(sourcePath))
            return null;

        var family = ReadFamilyName(sourcePath);
        if (string.IsNullOrWhiteSpace(family))
            return null;

        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var fileName = Sanitize(family) + Path.GetExtension(sourcePath).ToLowerInvariant();
            var target = Path.Combine(CacheDirectory, fileName);
            System.IO.File.Copy(sourcePath, target, overwrite: true);

            Entries.RemoveAll(e => string.Equals(e.Family, family, StringComparison.OrdinalIgnoreCase));
            Entries.Add(new Entry(family, fileName));
            Save();

            return family;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadFamilyName(string path)
    {
        try
        {
            using var typeface = SKTypeface.FromFile(path);
            return typeface?.FamilyName;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        try
        {
            if (!System.IO.File.Exists(IndexPath))
                return;

            var list = JsonSerializer.Deserialize<List<Entry>>(System.IO.File.ReadAllText(IndexPath));
            if (list is null)
                return;

            foreach (var entry in list)
            {
                if (System.IO.File.Exists(Path.Combine(CacheDirectory, entry.File)))
                    Entries.Add(entry);
            }
        }
        catch
        {
            // 索引坏了就当没有导入过
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            System.IO.File.WriteAllText(IndexPath, JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 存不下不影响本次会话使用
        }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    private static string ResolveCacheDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SubtitleStudio", "fonts"),
            Path.Combine(AppContext.BaseDirectory, "user-fonts"),
            Path.Combine(Path.GetTempPath(), "subtitle-studio-fonts")
        };

        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch
            {
                // 换下一个
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "user-fonts");
    }
}
