using System.Text.Json;
using System.Text.Json.Serialization;

namespace SubtitleStudio.Core.Asr;

/// <summary>识别后端。菜单里就这三项。</summary>
public enum AsrBackend
{
    /// <summary>本地 whisper.cpp。</summary>
    Local,

    /// <summary>硅基流动（OpenAI 兼容的 /audio/transcriptions）。</summary>
    SiliconFlow,

    /// <summary>天翼星辰（短语音识别，HTTP + API Key）。</summary>
    TeleAi
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

    public AsrSettings Clone() => new()
    {
        Backend = Backend,
        LocalModelFile = LocalModelFile,
        SiliconFlowBaseUrl = SiliconFlowBaseUrl,
        SiliconFlowApiKey = SiliconFlowApiKey,
        SiliconFlowModel = SiliconFlowModel,
        TeleAiApiKey = TeleAiApiKey,
        TeleAiBaseUrl = TeleAiBaseUrl
    };

    /// <summary>这个后端现在能不能直接用。</summary>
    public bool IsReady(AsrBackend backend) => backend switch
    {
        AsrBackend.SiliconFlow => !string.IsNullOrWhiteSpace(SiliconFlowApiKey),
        AsrBackend.TeleAi => !string.IsNullOrWhiteSpace(TeleAiApiKey),
        _ => true
    };

    /// <summary>缺什么，用于选中后提示用户去设置里填。</summary>
    public string MissingHint(AsrBackend backend) => backend switch
    {
        AsrBackend.SiliconFlow => "还没有填硅基流动的 API Key",
        AsrBackend.TeleAi => "还没有填天翼星辰的 API Key",
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

    /// <summary>配置文件路径；目录不可写时退到临时目录。</summary>
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
            var path = FilePath;
            if (!File.Exists(path))
                return new AsrSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AsrSettings>(json, Options) ?? new AsrSettings();
        }
        catch
        {
            // 配置坏了就当没配过，不要让程序起不来
            return new AsrSettings();
        }
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
