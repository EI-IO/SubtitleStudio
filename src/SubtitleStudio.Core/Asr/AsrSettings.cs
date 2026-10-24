using System.Text.Json;
using System.Text.Json.Serialization;

namespace SubtitleStudio.Core.Asr;

/// <summary>识别后端。菜单里就这三项 + 用户自己加的 OpenAI 兼容提供商。</summary>
public enum AsrBackend
{
    /// <summary>本地 whisper.cpp。</summary>
    Local,

    /// <summary>硅基流动（OpenAI 兼容的 /audio/transcriptions）。</summary>
    SiliconFlow,

    /// <summary>天翼星辰（短语音识别，HTTP + API Key）。</summary>
    TeleAi,

    /// <summary>用户自定义的 OpenAI 兼容提供商（具体用哪家看 <see cref="AsrSettings.CustomProviderId"/>）。</summary>
    Custom
}

/// <summary>用户自加的 OpenAI 兼容识别提供商：名称 + 接口地址 + Key + 模型。</summary>
public sealed class CustomProvider
{
    /// <summary>稳定标识（Guid.N），改名不影响选中状态。</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>OpenAI 兼容的 /v1 地址，识别走 {BaseUrl}/audio/transcriptions。</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>识别模型名，例如 whisper-1。</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>连接测试时拉到的模型清单（主界面模型菜单的二级列表用它，下次启动不用重新测）。</summary>
    public List<string> CachedModels { get; set; } = new();

    public CustomProvider Clone() => new()
    {
        Id = Id,
        Name = Name,
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
        Model = Model,
        CachedModels = new List<string>(CachedModels)
    };
}

/// <summary>用户配置：选哪个后端、各自的 Key 与模型。存在本地，不联网。</summary>
public sealed class AsrSettings
{
    public AsrBackend Backend { get; set; } = AsrBackend.Local;

    /// <summary>本地模型的文件名（models 目录下），空表示自动挑。</summary>
    public string LocalModelFile { get; set; } = string.Empty;

    // —— 硅基流动 ——
    public string SiliconFlowBaseUrl { get; set; } = "https://api.siliconflow.cn/v1";

    public string SiliconFlowApiKey { get; set; } = string.Empty;

    public string SiliconFlowModel { get; set; } = "FunAudioLLM/SenseVoiceSmall";

    // —— 天翼星辰（短语音识别，只要 API Key）——
    /// <summary>控制台的 sk- 密钥，请求头 Authorization: Bearer。</summary>
    public string TeleAiApiKey { get; set; } = string.Empty;

    /// <summary>接入点，默认 api-maas.teleai.com.cn。</summary>
    public string TeleAiBaseUrl { get; set; } = TeleAiClient.DefaultBaseUrl;

    /// <summary>识别前先过一道 ffmpeg 降噪（高通去轰声 + afftdn 去底噪）；背景嘈杂的素材明显更准，干净素材也无害。</summary>
    public bool Denoise { get; set; } = true;

    // —— 自定义 OpenAI 兼容提供商 ——
    /// <summary>用户自己加的提供商，顺序即列表/菜单里的顺序。</summary>
    public List<CustomProvider> CustomProviders { get; set; } = new();

    /// <summary>Backend = Custom 时用哪家；指向 <see cref="CustomProviders"/> 里的 Id。</summary>
    public string? CustomProviderId { get; set; }

    /// <summary>界面语言代码（zh / en / lang 目录里的包）。null 表示还没选过，首次启动会弹语言选择窗。</summary>
    public string? Language { get; set; }

    /// <summary>当前选中的自定义提供商；没选或已被删掉时为 null。</summary>
    public CustomProvider? ActiveCustomProvider() =>
        CustomProviders.FirstOrDefault(p => p.Id == CustomProviderId);

    // —— 主窗口位置（关窗时自动记，下次启动还原；屏幕变了会做可见性适配）——
    /// <summary>主窗口左上角（屏幕像素）；null 表示还没记过，用默认居中。</summary>
    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    /// <summary>主窗口尺寸（DIP，与 XAML 里 Width/Height 同单位）；0 表示没记过。</summary>
    public double WindowWidth { get; set; }

    public double WindowHeight { get; set; }

    /// <summary>关闭时是否最大化（最大化时上面记的是还原前的大小）。</summary>
    public bool WindowMaximized { get; set; }

    /// <summary>单个自定义提供商配没配好：地址、Key、模型缺一不可。</summary>
    public static bool IsReady(CustomProvider? provider) =>
        provider is not null
        && !string.IsNullOrWhiteSpace(provider.BaseUrl)
        && !string.IsNullOrWhiteSpace(provider.ApiKey)
        && !string.IsNullOrWhiteSpace(provider.Model);

    public AsrSettings Clone() => new()
    {
        Backend = Backend,
        LocalModelFile = LocalModelFile,
        SiliconFlowBaseUrl = SiliconFlowBaseUrl,
        SiliconFlowApiKey = SiliconFlowApiKey,
        SiliconFlowModel = SiliconFlowModel,
        TeleAiApiKey = TeleAiApiKey,
        TeleAiBaseUrl = TeleAiBaseUrl,
        Denoise = Denoise,
        // 列表要深拷贝：设置窗口改的是副本，写盘前不碰正在用的配置
        CustomProviders = CustomProviders.Select(p => p.Clone()).ToList(),
        CustomProviderId = CustomProviderId,
        Language = Language,
        WindowX = WindowX,
        WindowY = WindowY,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        WindowMaximized = WindowMaximized
    };

    /// <summary>这个后端现在能不能直接用。</summary>
    public bool IsReady(AsrBackend backend) => backend switch
    {
        AsrBackend.SiliconFlow => !string.IsNullOrWhiteSpace(SiliconFlowApiKey),
        AsrBackend.TeleAi => !string.IsNullOrWhiteSpace(TeleAiApiKey),
        AsrBackend.Custom => IsReady(ActiveCustomProvider()),
        _ => true
    };

    /// <summary>缺什么，用于选中后提示用户去设置里填。</summary>
    public string MissingHint(AsrBackend backend) => backend switch
    {
        AsrBackend.SiliconFlow => "还没有填硅基流动的 API Key",
        AsrBackend.TeleAi => "还没有填天翼星辰的 API Key",
        AsrBackend.Custom => ActiveCustomProvider() is { } p
            ? $"「{p.Name}」还没有配好（需要接口地址、API Key、模型名）"
            : "还没有添加自定义提供商",
        _ => string.Empty
    };
}

/// <summary>设置的读写。放在 %LOCALAPPDATA%\SubtitleStudio\settings.json。</summary>
public static class AsrSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // 枚举写成 "SiliconFlow" 这种字符串，别存成数字
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>配置文件路径；目录不可写（杀软/受控文件夹访问会拦）时依次退到下一个候选。</summary>
    public static string FilePath
    {
        get
        {
            // 想放到别处（比如便携版 U 盘）时用这个环境变量指定
            var custom = Environment.GetEnvironmentVariable("SUBTITLESTUDIO_SETTINGS");
            if (!string.IsNullOrWhiteSpace(custom))
                return custom;
            foreach (var dir in CandidateDirectories())
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    // 只建目录不算数：有的安全软件让建目录成功、写文件才拦，
                    // 必须真写一笔探针再删掉，才认为这个目录可用
                    var probe = Path.Combine(dir, ".write-probe");
                    File.WriteAllText(probe, string.Empty);
                    File.Delete(probe);
                    return Path.Combine(dir, "settings.json");
                }
                catch
                {
                    // 换下一个
                }
            }

            return Path.Combine(Path.GetTempPath(), "subtitle-studio", "settings.json");
        }
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
            yield return Path.Combine(local, "SubtitleStudio");

        // 受控文件夹 / 只读安装目录时的备选：文档目录更像用户自己的地盘
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
            yield return Path.Combine(documents, "SubtitleStudio");

        yield return Path.Combine(AppContext.BaseDirectory, "config");
        yield return Path.Combine(Path.GetTempPath(), "subtitle-studio");
    }

    public static AsrSettings Load()
    {
        try
        {
            // 环境变量指定的地方最优先
            var custom = Environment.GetEnvironmentVariable("SUBTITLESTUDIO_SETTINGS");
            if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
                return Read(custom);

            // 可写位置里有就用它（保存也写这里，不会读到别处过期的副本）
            var path = FilePath;
            if (File.Exists(path))
                return Read(path);

            // 可写位置还没有：找任何候选目录里已有的副本（目录可能只读，但读一般没事）
            foreach (var dir in CandidateDirectories())
            {
                var existing = Path.Combine(dir, "settings.json");
                if (File.Exists(existing))
                    return Read(existing);
            }

            return new AsrSettings();
        }
        catch
        {
            // 配置坏了就当没配过，不要让程序起不来
            return new AsrSettings();
        }
    }

    private static AsrSettings Read(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AsrSettings>(json, Options) ?? new AsrSettings();
    }

    public static void Save(AsrSettings settings)
    {
        var path = FilePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
