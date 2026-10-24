namespace SubtitleStudio.Core.Asr;

/// <summary>某个目录里已经存在的本地模型。</summary>
public sealed record LocalModel(string FileName, string Display, string FullPath, long Bytes)
{
    public string SizeLabel => Bytes >= 1024L * 1024 * 1024
        ? $"{Bytes / 1024.0 / 1024 / 1024:0.0} GB"
        : $"{Bytes / 1024.0 / 1024:0} MB";
}

/// <summary>可以放进 models/ 的 ggml 模型（体积 / 用途 / 下载地址）。</summary>
public sealed record ModelSuggestion(string FileName, string Display, string SizeLabel, string Note)
{
    public string Url => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}";
}

/// <summary>管理本地 ggml 模型文件：查找、优先级、可用清单。</summary>
public static class ModelCatalog
{
    /// <summary>质量从高到低，存在哪个用哪个。</summary>
    public static readonly string[] Preference =
    {
        "ggml-large-v3-turbo.bin",
        "ggml-large-v3-turbo-q5_0.bin",
        "ggml-large-v3.bin",
        "ggml-medium.bin",
        "ggml-medium-q5_0.bin",
        "ggml-small.bin",
        "ggml-small-q5_1.bin",
        "ggml-base.bin",
        "ggml-tiny.bin"
    };

    /// <summary>推荐下载清单，界面上的「模型说明」用。Note 是语言包缺键时的英文兜底。</summary>
    public static IReadOnlyList<ModelSuggestion> Suggestions { get; } = new[]
    {
        new ModelSuggestion("ggml-tiny.bin", "tiny", "75 MB", "Smallest and fastest; only for trying the flow — many Chinese typos"),
        new ModelSuggestion("ggml-base.bin", "base", "142 MB", "Lightweight; usable English, rough Chinese"),
        new ModelSuggestion("ggml-small.bin", "small", "466 MB", "Balanced speed and quality; usable Chinese"),
        new ModelSuggestion("ggml-medium.bin", "medium", "1.5 GB", "Clearly better Chinese; runs with 6 GB VRAM"),
        new ModelSuggestion("ggml-large-v3-turbo.bin", "large-v3-turbo", "1.6 GB", "Best Chinese and faster than large — recommended"),
        new ModelSuggestion("ggml-large-v3-turbo-q5_0.bin", "large-v3-turbo-q5_0", "574 MB", "5-bit quantized turbo; saves memory"),
        new ModelSuggestion("ggml-large-v3.bin", "large-v3", "3.1 GB", "Most accurate but slowest; needs 8 GB+ VRAM"),
        new ModelSuggestion("ggml-medium-q5_0.bin", "medium-q5_0", "514 MB", "Quantized medium; for low-spec machines"),
        new ModelSuggestion("ggml-small-q5_1.bin", "small-q5_1", "190 MB", "Quantized small")
    };

    public static string DefaultModelDirectory
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; dir != null && i < 6; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "models");
                if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.bin").Any())
                    return candidate;
            }

            return Path.Combine(AppContext.BaseDirectory, "models");
        }
    }

    /// <summary>列出目录里所有 ggml 模型，按体积从大到小。</summary>
    public static IReadOnlyList<LocalModel> List(string? directory = null)
    {
        directory ??= DefaultModelDirectory;
        if (!Directory.Exists(directory))
            return Array.Empty<LocalModel>();

        return Directory.EnumerateFiles(directory, "ggml-*.bin")
            .Select(path => new LocalModel(
                Path.GetFileName(path),
                DisplayName(path),
                path,
                new FileInfo(path).Length))
            .OrderByDescending(m => m.Bytes)
            .ToList();
    }

    public static string? FindModel(string? directory = null)
    {
        directory ??= DefaultModelDirectory;
        if (!Directory.Exists(directory))
            return null;

        foreach (var name in Preference)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
                return path;
        }

        return Directory.EnumerateFiles(directory, "*.bin").OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
    }

    public static string DisplayName(string modelPath) =>
        Path.GetFileNameWithoutExtension(modelPath).Replace("ggml-", string.Empty, StringComparison.OrdinalIgnoreCase);
}
