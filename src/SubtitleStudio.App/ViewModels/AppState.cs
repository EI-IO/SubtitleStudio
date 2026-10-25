using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Core;
using SubtitleStudio.App.Docking;
using SubtitleStudio.App.Services;
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
        QualityOptions = BuildQualityOptions();
        _quality = QualityOptions[1];

        var factory = new DockFactory(this);
        _layout = factory.CreateLayout();
        factory.InitLayout(_layout);
    }

    private Dock.Model.Controls.IRootDock _layout;

    /// <summary>停靠布局；重置布局时整体换掉，DockControl 会跟着重排。</summary>
    public Dock.Model.Controls.IRootDock Layout
    {
        get => _layout;
        private set => SetProperty(ref _layout, value);
    }

    /// <summary>恢复默认四区布局，并删掉布局存档（重启也是默认）。</summary>
    public void ResetLayout()
    {
        var factory = new DockFactory(this);
        Layout = factory.CreateDefaultLayout();
        factory.InitLayout(Layout);
        DockLayoutStore.TryDelete();
        Status = Loc.T("Status.ResetLayout");
    }

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
    [ObservableProperty] private string _status = Loc.T("Status.Ready");
    [ObservableProperty] private string _busyText = string.Empty;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private double _exportProgress;
    [ObservableProperty] private string _exportText = string.Empty;

    /// <summary>当前这份素材是否完整跑完过一次识别；决定按钮显示「开始识别」还是「重新识别」。</summary>
    [ObservableProperty] private bool _hasRecognized;

    /// <summary>识别按钮的文字：没识别过 / 中途停过 = 开始识别；完整跑完过 = 重新识别。</summary>
    public string RecognizeText => Loc.T(HasRecognized ? "Button.ReRecognize" : "Button.Recognize");

    partial void OnHasRecognizedChanged(bool value) => OnPropertyChanged(nameof(RecognizeText));
    [ObservableProperty] private string _audioInfo = Loc.T("AudioInfo.LocalNone");

    /// <summary>状态栏左边的文件名；没加载时显示「未加载文件」（随语言切换）。</summary>
    public string MediaTitle => Media?.DisplayName ?? Loc.T("Status.NoFile");

    /// <summary>字幕列表面板的标题：有选中轨显示轨名，否则显示面板名。</summary>
    public string CuesHeader => SelectedTrack?.Label ?? Loc.T("Panel.SubtitleList");

    /// <summary>时间轴上的播放头读数（前缀也走语言包）。</summary>
    public string PlayheadText => Loc.T("Timeline.Playhead", CurrentTime);
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
                {
                    var autoModel = new LocalModel(
                        Path.GetFileName(auto), ModelCatalog.DisplayName(auto), auto, new FileInfo(auto).Length);
                    return Loc.T("Label.LocalModelWithSize", autoModel.Display, autoModel.SizeLabel);
                }

                return model is null
                    ? Loc.T("Label.LocalModel")
                    : Loc.T("Label.LocalModelWithSize", model.Display, model.SizeLabel);
            }

            if (Backend == AsrBackend.SiliconFlow)
                return Loc.T("AudioInfo.Silicon", ShortModel(Settings.SiliconFlowModel));

            if (Backend == AsrBackend.Custom)
                return Settings.ActiveCustomProvider() is { } p
                    ? $"{p.Name} · {ShortModel(p.Model)}"
                    : Loc.T("Label.CustomProviderMissing");

            return Loc.T("Label.TeleAiShort");
        }
    }

    private static string ShortModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return Loc.T("Label.NoModelSelected");

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
        Status = Loc.T("Label.LocalModelWithSize", model.Display, model.SizeLabel);
        AudioInfo = Loc.T("AudioInfo.LocalWhisper", model.Display);
        OnPropertyChanged(nameof(ModelLabel));
    }

    /// <summary>换后端。缺 Key 时只提示，不拦截（由调用方决定要不要弹设置）。</summary>
    public void UseBackend(AsrBackend backend)
    {
        Backend = backend;
        Settings.Backend = backend;
        SaveSettings();
        OnPropertyChanged(nameof(ModelLabel));

        AudioInfo = DescribeBackend();

        Status = Settings.IsReady(backend)
            ? ModelLabel
            : Loc.T("Status.NeedCredential", MissingHintText(backend));
    }

    /// <summary>缺凭证的提示（Core 那边是中文，这里按当前语言重说一遍）。</summary>
    private string MissingHintText(AsrBackend backend) => backend switch
    {
        AsrBackend.SiliconFlow => Loc.T("Hint.NeedSilicon"),
        AsrBackend.TeleAi => Loc.T("Hint.NeedTeleAi"),
        AsrBackend.Custom => Settings.ActiveCustomProvider() is { } p
            ? Loc.T("Hint.CustomNotReady", p.Name)
            : Loc.T("Hint.NoCustom"),
        _ => string.Empty
    };

    /// <summary>切到某个自定义提供商（OpenAI 兼容接口）。</summary>
    public void UseCustomProvider(CustomProvider provider)
    {
        Settings.Backend = AsrBackend.Custom;
        Settings.CustomProviderId = provider.Id;
        SaveSettings();
        Backend = AsrBackend.Custom;
        Status = ModelLabel;
        AudioInfo = DescribeBackend();
        OnPropertyChanged(nameof(ModelLabel));
    }

    /// <summary>选自定义提供商的某个模型（设置里连接测试拉到的清单，模型菜单二级项点选）。</summary>
    public void UseCustomProviderModel(CustomProvider provider, string modelId)
    {
        // 菜单项拿的是打开菜单那一刻的副本，写设置要落到 Settings 里的真身上
        var target = Settings.CustomProviders.FirstOrDefault(p => p.Id == provider.Id);
        if (target is not null)
            target.Model = modelId;

        UseCustomProvider(provider);
    }

    /// <summary>设置窗口连接测试拉到模型后回传：硅基流动刷新运行时列表；自定义提供商写进它自己的缓存并落盘。</summary>
    public void OnCloudModelsFetched(string key, IReadOnlyList<RemoteModel> models)
    {
        if (key == "siliconflow")
        {
            SiliconFlowModels.Clear();
            foreach (var model in models)
                SiliconFlowModels.Add(model);
            return;
        }

        var provider = Settings.CustomProviders.FirstOrDefault(p => p.Id == key);
        if (provider is null)
            return;

        provider.CachedModels = models.Select(m => m.Id).ToList();
        SaveSettings();
    }

    /// <summary>状态栏里「当前后端」的一句话描述。</summary>
    private string DescribeBackend() => Backend switch
    {
        AsrBackend.Local => Loc.T("AudioInfo.LocalWhisper", SelectedModel?.Display ?? Loc.T("Label.Auto")),
        AsrBackend.SiliconFlow => Loc.T("AudioInfo.Silicon", ShortModel(Settings.SiliconFlowModel)),
        AsrBackend.Custom => Settings.ActiveCustomProvider() is { } p
            ? $"{p.Name} · {ShortModel(p.Model)}"
            : Loc.T("AudioInfo.CustomMissing"),
        _ => Loc.T("AudioInfo.TeleAi")
    };

    /// <summary>缺凭证时由界面弹出提醒；返回 true 表示已经补好，可以继续识别。</summary>
    public Func<AsrBackend, Task<bool>>? RequestCredentialAsync { get; set; }

    /// <summary>天翼星辰只有一个入口：短语音识别（HTTPS + API Key，1 分钟内，自动切片）。</summary>
    public static string TeleAiEntry => Loc.T("Label.TeleAiEntry");
    /// <summary>切到硅基流动的某个模型。</summary>
    public void UseSiliconFlowModel(RemoteModel model)
    {
        Settings.Backend = AsrBackend.SiliconFlow;
        Settings.SiliconFlowModel = model.Id;
        SaveSettings();
        Backend = AsrBackend.SiliconFlow;
        Status = ModelLabel;
        AudioInfo = Loc.T("AudioInfo.Silicon", ShortModel(model.Id));
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
        AudioInfo = Loc.T("AudioInfo.TeleAi");
        OnPropertyChanged(nameof(ModelLabel));
    }

    /// <summary>拉硅基流动的模型列表（只留语音识别类）。</summary>
    public async Task<string> RefreshSiliconFlowModelsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Settings.SiliconFlowApiKey))
            return Loc.T("Settings.Status.FillKey");

        try
        {
            var models = await Core.Asr.SiliconFlowClient.ListModelsAsync(
                Settings.SiliconFlowBaseUrl, Settings.SiliconFlowApiKey, cancellationToken);

            SiliconFlowModels.Clear();
            foreach (var model in models)
                SiliconFlowModels.Add(model);

            return models.Count == 0
                ? Loc.T("Settings.Status.SiliconEmpty")
                : Loc.T("Settings.Status.SiliconOk", models.Count);
        }
        catch (Exception ex)
        {
            return Loc.T("Settings.Status.ConnectFailed", Core.Asr.TlsDiagnostics.Describe(ex));
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
            ReportError(Loc.T("Error.SaveSettings", ex.Message));
        }

        // 选中的自定义提供商可能在设置里被删了：回退到本地，别让后端指着空气
        if (Settings.Backend == AsrBackend.Custom && Settings.ActiveCustomProvider() is null)
            Settings.Backend = AsrBackend.Local;

        Backend = Settings.Backend;
        RefreshModels();
        OnPropertyChanged(nameof(ModelLabel));
        Status = ModelLabel;
        AudioInfo = DescribeBackend();
    }

    private void SaveSettings()
    {
        try
        {
            AsrSettingsStore.Save(Settings);
        }
        catch (Exception ex)
        {
            ReportError(Loc.T("Error.SaveSettings", ex.Message));
        }
    }

    /// <summary>切换界面语言：立即换文案，并记住选择。</summary>
    public void SetLanguage(string? code)
    {
        Loc.Use(code);
        Settings.Language = Loc.Current;
        SaveSettings();
        OnLanguageChanged();
    }

    /// <summary>把当前语言记下来（首次启动直接关掉选择窗时用）。</summary>
    public void PersistLanguage()
    {
        Settings.Language = Loc.Current;
        SaveSettings();
    }

    /// <summary>语言变了：C# 拼出来的文字全部重算，面板标题也换掉；XAML 的 DynamicResource 由 Loc 自己刷新。</summary>
    public void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(MediaTitle));
        OnPropertyChanged(nameof(CuesHeader));
        OnPropertyChanged(nameof(PlayheadText));
        OnPropertyChanged(nameof(RecognizeText));
        OnPropertyChanged(nameof(ModelLabel));
        OnPropertyChanged(nameof(ExportLayoutHint));

        AudioInfo = DescribeBackend();
        PlayText = Loc.T(IsPlaying ? "Play.Pause" : "Play.Play");
        Status = Loc.T("Status.Ready");
        ApplyQualityLanguage();

        foreach (var track in Tracks)
            track.ApplyLanguage();

        ApplyPanelTitles();

        if (Layout is { } layout)
        {
            foreach (var dockable in Descendants(layout))
            {
                if (dockable is StyleViewModel style)
                    style.ApplyLanguage();
            }
        }
    }

    /// <summary>把停靠面板标题换成当前语言。</summary>
    public void ApplyPanelTitles()
    {
        var keys = new Dictionary<string, string>
        {
            ["SubtitleList"] = "Panel.SubtitleList",
            ["Timeline"] = "Panel.Timeline",
            ["Style"] = "Panel.Style",
            ["Preview"] = "Panel.Preview"
        };

        foreach (var dockable in Descendants(Layout))
        {
            if (dockable.Id is { } id && keys.TryGetValue(id, out var key))
                dockable.Title = Loc.T(key);
        }
    }

    private static IEnumerable<IDockable> Descendants(IDockable dockable)
    {
        yield return dockable;

        if (dockable is IDock container && container.VisibleDockables is { } children)
        {
            foreach (var child in children)
                foreach (var descendant in Descendants(child))
                    yield return descendant;
        }
    }

    /// <summary>记住主窗口的位置与尺寸（写了就不管：MainWindow 关窗时调用，下次启动还原）。</summary>
    public void SaveWindowPlacement(int x, int y, double width, double height, bool maximized)
    {
        Settings.WindowX = x;
        Settings.WindowY = y;
        Settings.WindowWidth = width;
        Settings.WindowHeight = height;
        Settings.WindowMaximized = maximized;
        SaveSettings();
    }

    /// <summary>当前后端配好了没。</summary>
    public bool BackendReady => Settings.IsReady(Backend);

    /// <summary>当前要用的本地模型路径：用户选的优先，否则自动挑最好的。</summary>
    public string? LocalModelPath() => SelectedModel?.FullPath ?? ModelCatalog.FindModel();

    /// <summary>
    /// 导出字幕图层的清晰度档位。列表对象保持稳定，只改显示名——
    /// 换 ItemsSource 会让 ComboBox 先把 SelectedItem 清成 null，导出时会拿到空档位。
    /// </summary>
    public IReadOnlyList<QualityChoice> QualityOptions { get; }

    [ObservableProperty]
    private QualityChoice _quality;

    private static QualityChoice[] BuildQualityOptions() =>
    [
        new(SubtitleStudio.Core.Export.OverlayQuality.Sd, Loc.T("Quality.Sd")),
        new(SubtitleStudio.Core.Export.OverlayQuality.Hd, Loc.T("Quality.Hd")),
        new(SubtitleStudio.Core.Export.OverlayQuality.Uhd, Loc.T("Quality.Uhd"))
    ];

    /// <summary>切语言后刷新下拉里的档位名称，选中项不变。</summary>
    public void ApplyQualityLanguage()
    {
        foreach (var option in BuildQualityOptions())
            QualityOptions.First(q => q.Value == option.Value).Name = option.Name;
        OnPropertyChanged(nameof(ExportLayoutHint));
    }

    /// <summary>导出画布尺寸的一句话说明，显示在预览面板上。</summary>
    public string ExportLayoutHint
    {
        get
        {
            try
            {
                var cues = Cues.Select(c => c.Model).ToList();
                var layout = SubtitleStudio.Core.Export.OverlayLayout.Measure(
                    cues, Style, Quality.Value);
                return Loc.T("Export.LayoutHint", layout.Width, layout.Height, layout.Lines, layout.MaxChars);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    partial void OnQualityChanged(QualityChoice value)
        => OnPropertyChanged(nameof(ExportLayoutHint));

    /// <summary>当前错误是不是权限问题（是的话界面会给一个「以管理员重启」的按钮）。</summary>
    [ObservableProperty] private bool _errorIsPermission;

    /// <summary>由 views 注入的文件/目录选择器。</summary>
    public Func<Task<string?>>? PickMediaFile { get; set; }
    public Func<Task<string?>>? PickFolder { get; set; }
    public Func<string, Task>? RevealPath { get; set; }

    /// <summary>缺少 ffmpeg 时由界面弹提示；返回 true 表示用户已经放好文件。</summary>
    public Func<Task<bool>>? RequestFfmpegAsync { get; set; }

    /// <summary>
    /// 预览画布跟着视频真实分辨率走（硬编码 1280×720 的话，预览里的字号和位置跟导出对不上）。
    /// 最长边压到 1920 以内，4K 素材不至于让预览渲染又慢又吃内存；没加载媒体时用 1280×720。
    /// </summary>
    public int PreviewWidth => PreviewSize().Width;
    public int PreviewHeight => PreviewSize().Height;

    private (int Width, int Height) PreviewSize()
    {
        if (Media is not { Width: > 0, Height: > 0 } media)
            return (1280, 720);

        var scale = Math.Min(1.0, 1920.0 / Math.Max(media.Width, media.Height));
        return (Math.Max(2, (int)Math.Round(media.Width * scale)),
                Math.Max(2, (int)Math.Round(media.Height * scale)));
    }

    public double Duration => Media?.DurationSeconds ?? 0;

    partial void OnMediaChanged(MediaInfo? value)
    {
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(PreviewWidth));
        OnPropertyChanged(nameof(PreviewHeight));
        OnPropertyChanged(nameof(MediaTitle));
    }

    partial void OnSelectedTrackChanged(TrackItem? value)
    {
        OnPropertyChanged(nameof(Cues));
        OnPropertyChanged(nameof(HasCues));
        OnPropertyChanged(nameof(Waveform));
        OnPropertyChanged(nameof(ExportLayoutHint));
        OnPropertyChanged(nameof(CuesHeader));
        SelectedCue = value?.Cues.FirstOrDefault();

        if (value is { Language: { Length: > 0 } })
            Status = Loc.T("Status.TrackSummary", value.LabelShort, value.Cues.Count, value.Language);
        else if (value is not null)
            Status = $"{value.LabelShort} · {value.Status}";

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

/// <summary>清晰度下拉项：Value 给导出用，Name 是当前语言的显示名（可原地更新）。</summary>
public sealed partial class QualityChoice : ObservableObject
{
    public QualityChoice(SubtitleStudio.Core.Export.OverlayQuality value, string name)
    {
        Value = value;
        _name = name;
    }

    public SubtitleStudio.Core.Export.OverlayQuality Value { get; }

    [ObservableProperty] private string _name;
}
