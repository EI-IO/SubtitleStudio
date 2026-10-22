using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SubtitleStudio.App.Docking;
using SubtitleStudio.Core.Asr;
using SubtitleStudio.Core.Media;
using SubtitleStudio.Core.Models;

namespace SubtitleStudio.App.ViewModels;

/// <summary>所有 Dock 面板共享的编辑状态。</summary>
public sealed partial class AppState : ObservableObject
{
    /// <summary>没有任何音轨时的空集合，避免界面绑定到 null。</summary>
    private static readonly ObservableCollection<CueItem> NoCues = new();

    public AppState()
    {
        var factory = new DockFactory(this);
        Layout = factory.CreateLayout();
        factory.InitLayout(Layout);
    }

    public Dock.Model.Controls.IRootDock Layout { get; }

    /// <summary>文件里的所有音轨，每条各自识别、各自出字幕。</summary>
    public ObservableCollection<TrackItem> Tracks { get; } = new();

    /// <summary>当前选中音轨的字幕列表；切轨时整体换掉。</summary>
    public ObservableCollection<CueItem> Cues => SelectedTrack?.Cues ?? NoCues;

    public bool HasCues => Cues.Count > 0;

    [ObservableProperty] private TrackItem? _selectedTrack;
    [ObservableProperty] private MediaInfo? _media;
    [ObservableProperty] private double _currentTime;
    [ObservableProperty] private CueItem? _selectedCue;
    [ObservableProperty] private double _pixelsPerSecond = 100;
    [ObservableProperty] private double _scrollSeconds;
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private CueStyle _style = CueStyle.Default;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "把视频/音频拖进来，或点击「打开文件」";
    [ObservableProperty] private string _busyText = string.Empty;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private double _exportProgress;
    [ObservableProperty] private string _exportText = string.Empty;
    [ObservableProperty] private string _audioInfo = "本地 · 未加载模型";
    [ObservableProperty] private string? _error;

    /// <summary>错误全文；界面只显示开头，悬停看全文，点击复制。</summary>
    public string? ErrorText => Error;

    /// <summary>错误的一句话版本，太长就截断。</summary>
    public string ErrorShort
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Error))
                return string.Empty;

            // 多行错误压成一行，再截断（完整内容在 ToolTip 里）
            var flat = string.Join(' ', Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return flat.Length <= 38 ? flat : flat[..38] + "…";
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    partial void OnErrorChanged(string? value)
    {
        OnPropertyChanged(nameof(ErrorShort));
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasError));
    }

    /// <summary>记录一条错误；界面会截断显示，悬停可看全文。</summary>
    public void ReportError(string message, bool permission = false)
    {
        Error = message;
        ErrorIsPermission = permission;
    }

    public void ClearError()
    {
        Error = null;
        ErrorIsPermission = false;
    }


    /// <summary>用户设置（后端、Key、模型），从磁盘读一次、改动后写回。</summary>
    public AsrSettings Settings { get; private set; } = AsrSettingsStore.Load();

    /// <summary>当前后端：本地模型 / 硅基流动 / 天翼星辰。</summary>
    [ObservableProperty] private AsrBackend _backend = AsrBackend.Local;

    /// <summary>models/ 里现成的本地模型。</summary>
    public ObservableCollection<LocalModel> LocalModels { get; } = new();

    /// <summary>当前选中的本地模型；为 null 时用目录里最好的那个。</summary>
    [ObservableProperty] private LocalModel? _selectedModel;

    /// <summary>云端拉到的模型列表（设置窗口用）。</summary>
    public ObservableCollection<RemoteModel> SiliconFlowModels { get; } = new();

    /// <summary>模型菜单按钮上显示的字。</summary>
    public string ModelLabel
    {
        get
        {
            if (Backend == AsrBackend.Local)
            {
                var model = SelectedModel;
                if (model is null && ModelCatalog.FindModel() is { } auto)
                    return $"本地模型 · {ModelCatalog.DisplayName(auto)}";

                return model is null ? "本地模型" : $"本地模型 · {model.Display}（{model.SizeLabel}）";
            }

            if (Backend == AsrBackend.SiliconFlow)
                return "硅基流动 · " + ShortModel(Settings.SiliconFlowModel);

            return "天翼星辰 · 短语音识别";
        }
    }

    private static string ShortModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return "未选模型";

        var slash = model.LastIndexOf('/');
        return slash >= 0 && slash + 1 < model.Length ? model[(slash + 1)..] : model;
    }

    /// <summary>重新扫描 models/ 目录。</summary>
    public void RefreshModels()
    {
        LocalModels.Clear();
        foreach (var model in ModelCatalog.List())
            LocalModels.Add(model);

        if (SelectedModel is not null && LocalModels.All(m => m.FullPath != SelectedModel.FullPath))
            SelectedModel = null;

        if (SelectedModel is null && !string.IsNullOrWhiteSpace(Settings.LocalModelFile))
        {
            SelectedModel = LocalModels.FirstOrDefault(m =>
                string.Equals(Path.GetFileName(m.FullPath), Settings.LocalModelFile, StringComparison.OrdinalIgnoreCase));
        }

        OnPropertyChanged(nameof(ModelLabel));
    }

    /// <summary>切到某个本地模型。</summary>
    public void UseLocalModel(LocalModel model)
    {
        SelectedModel = model;
        Settings.Backend = AsrBackend.Local;
        Settings.LocalModelFile = Path.GetFileName(model.FullPath);
        SaveSettings();
        Backend = AsrBackend.Local;
        Status = $"本地模型：{model.Display}（{model.SizeLabel}）";
        AudioInfo = "本地 Whisper · " + model.Display;
        OnPropertyChanged(nameof(ModelLabel));
    }

    /// <summary>换后端。缺 Key 时只提示，不拦截（由调用方决定要不要弹设置）。</summary>
    public void UseBackend(AsrBackend backend)
    {
        Backend = backend;
        Settings.Backend = backend;
        SaveSettings();
        OnPropertyChanged(nameof(ModelLabel));

        AudioInfo = backend switch
        {
            AsrBackend.Local => "本地 Whisper · " + (SelectedModel?.Display ?? "自动"),
            AsrBackend.SiliconFlow => "硅基流动 · " + ShortModel(Settings.SiliconFlowModel),
            _ => "天翼星辰 · 短语音识别"
        };

        Status = Settings.IsReady(backend) ? ModelLabel : Settings.MissingHint(backend) + "，点「设置」填一下";
    }

    /// <summary>缺凭证时由界面弹出提醒；返回 true 表示已经补好，可以继续识别。</summary>
    public Func<AsrBackend, Task<bool>>? RequestCredentialAsync { get; set; }

    /// <summary>天翼星辰只有一个「短语音识别」入口（HTTPS + API Key）。</summary>
    /// <summary>天翼星辰只有一个入口：短语音识别（HTTPS + API Key，1 分钟内，自动切片）。</summary>
    public const string TeleAiEntry = "短语音识别（1 分钟内，自动切片）";
    /// <summary>切到硅基流动的某个模型。</summary>
    public void UseSiliconFlowModel(RemoteModel model)
    {
        Settings.Backend = AsrBackend.SiliconFlow;
        Settings.SiliconFlowModel = model.Id;
        SaveSettings();
        Backend = AsrBackend.SiliconFlow;
        Status = ModelLabel;
        AudioInfo = "硅基流动 · " + ShortModel(model.Id);
        OnPropertyChanged(nameof(ModelLabel));
    }

    /// <summary>切到天翼星辰（短语音识别）。</summary>
    public void UseTeleAi()
    {
        if (Backend == AsrBackend.TeleAi) return;

        Settings.Backend = AsrBackend.TeleAi;
        SaveSettings();
        Backend = AsrBackend.TeleAi;
        Status = ModelLabel;
        AudioInfo = "天翼星辰 · 短语音识别";
        OnPropertyChanged(nameof(ModelLabel));
    }

    /// <summary>拉硅基流动的模型列表（只留语音识别类）。</summary>
    public async Task<string> RefreshSiliconFlowModelsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Settings.SiliconFlowApiKey))
            return "先填 API Key 再刷新";

        try
        {
            var models = await Core.Asr.SiliconFlowClient.ListModelsAsync(
                Settings.SiliconFlowBaseUrl, Settings.SiliconFlowApiKey, cancellationToken);

            SiliconFlowModels.Clear();
            foreach (var model in models)
                SiliconFlowModels.Add(model);

            return models.Count == 0 ? "没有查到语音识别模型" : $"已获取 {models.Count} 个模型";
        }
        catch (Exception ex)
        {
            return "获取失败：" + Core.Asr.TlsDiagnostics.Describe(ex);
        }
    }

    /// <summary>设置窗口保存后调用：重新读盘并刷新界面。</summary>
    public void ApplySettings(AsrSettings settings)
    {
        Settings = settings.Clone();
        try
        {
            AsrSettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            ReportError("保存设置失败：" + ex.Message);
        }

        Backend = Settings.Backend;
        RefreshModels();
        OnPropertyChanged(nameof(ModelLabel));
        Status = ModelLabel;
        AudioInfo = Backend switch
        {
            AsrBackend.Local => "本地 Whisper · " + (SelectedModel?.Display ?? "自动"),
            AsrBackend.SiliconFlow => "硅基流动 · " + ShortModel(Settings.SiliconFlowModel),
            _ => "天翼星辰 · 短语音识别"
        };
    }

    private void SaveSettings()
    {
        try
        {
            AsrSettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            ReportError("保存设置失败：" + ex.Message);
        }
    }

    /// <summary>当前后端配好了没。</summary>
    public bool BackendReady => Settings.IsReady(Backend);

    /// <summary>当前要用的本地模型路径：用户选的优先，否则自动挑最好的。</summary>
    public string? LocalModelPath() => SelectedModel?.FullPath ?? ModelCatalog.FindModel();

    /// <summary>模型的建议下载清单。</summary>
    /// <summary>导出字幕图层的清晰度档位。</summary>
    public IReadOnlyList<SubtitleStudio.Core.Export.QualityOption> QualityOptions
        => SubtitleStudio.Core.Export.QualityOption.All;

    [ObservableProperty]
    private SubtitleStudio.Core.Export.QualityOption _quality =
        SubtitleStudio.Core.Export.QualityOption.All[1];

    /// <summary>导出画布尺寸的一句话说明，显示在预览面板上。</summary>
    public string ExportLayoutHint
    {
        get
        {
            try
            {
                var cues = Cues.Select(c => c.Model).ToList();
                var layout = SubtitleStudio.Core.Export.OverlayLayout.Measure(
                    cues, Style, Quality?.Value ?? SubtitleStudio.Core.Export.OverlayQuality.Hd);
                return layout.Describe();
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    partial void OnQualityChanged(SubtitleStudio.Core.Export.QualityOption value)
        => OnPropertyChanged(nameof(ExportLayoutHint));

    /// <summary>当前错误是不是权限问题（是的话界面会给一个「以管理员重启」的按钮）。</summary>
    [ObservableProperty] private bool _errorIsPermission;

    /// <summary>由 views 注入的文件/目录选择器。</summary>
    public Func<Task<string?>>? PickMediaFile { get; set; }
    public Func<Task<string?>>? PickFolder { get; set; }
    public Func<string, Task>? RevealPath { get; set; }

    public int PreviewWidth => 1280;
    public int PreviewHeight => 720;

    public double Duration => Media?.DurationSeconds ?? 0;

    partial void OnMediaChanged(MediaInfo? value)
    {
        OnPropertyChanged(nameof(Duration));
    }

    partial void OnSelectedTrackChanged(TrackItem? value)
    {
        OnPropertyChanged(nameof(Cues));
        OnPropertyChanged(nameof(HasCues));
        OnPropertyChanged(nameof(Waveform));
        OnPropertyChanged(nameof(ExportLayoutHint));
        SelectedCue = value?.Cues.FirstOrDefault();

        if (value is { Language: { Length: > 0 } })
            Status = $"{value.Info.Number} · {value.Cues.Count} 条字幕 · 语言 {value.Language}";
        else if (value is not null)
            Status = $"{value.Info.Number} · {value.Status}";

        SchedulePreview();
    }

    /// <summary>选中音轨的波形（时间轴画当前轨时用）。</summary>
    public WaveformData? Waveform => SelectedTrack?.Waveform;

    /// <summary>字幕集合或轨道变化后通知界面刷新。</summary>
    public void NotifyCuesChanged()
    {
        OnPropertyChanged(nameof(Cues));
        OnPropertyChanged(nameof(HasCues));
        OnPropertyChanged(nameof(Waveform));
        OnPropertyChanged(nameof(ExportLayoutHint));
    }
}
