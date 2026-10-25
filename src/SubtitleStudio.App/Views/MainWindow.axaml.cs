using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using SubtitleStudio.App.Services;
using SubtitleStudio.App.ViewModels;
using SubtitleStudio.Core.Asr;

namespace SubtitleStudio.App.Views;

public partial class MainWindow : Window
{
    /// <summary>上次正常（非最大化）状态下的位置与尺寸，最大化关窗时记的是这套。</summary>
    private PixelPoint? _lastNormalPos;
    private Size? _lastNormalSize;

    /// <summary>构造时还原到了上次的位置就不再做小屏收缩，否则互相打架。</summary>
    private bool _placementRestored;

    /// <summary>语言下拉里「更多语言…」这条的哨兵代码。</summary>
    private const string MoreLanguages = "__more__";

    /// <summary>重建下拉时屏蔽 SelectionChanged，避免自我触发。</summary>
    private bool _languageSyncing;

    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        // 语言一变就刷新下拉项（XAML 文案由 Loc 的 DynamicResource 自己更新）
        Loc.Changed += BuildLanguageBox;
        BuildLanguageBox();

        try
        {
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://SubtitleStudio/Assets/icon.png")));
        }
        catch
        {
            // 图标不是关键路径，缺了不影响使用
        }

        // 窗口显示前还原上次的位置（读的是磁盘设置，和 AppState 那份内容一致）
        RestorePlacement(AsrSettingsStore.Load());

        // 最大化时 Bounds/Position 报的是铺满屏的值，要单独记还原前的正常尺寸
        PositionChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal)
                _lastNormalPos = e.Point;
        };
        SizeChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal)
                _lastNormalSize = e.NewSize;
        };
    }

    /// <summary>启动参数里的媒体文件，OnOpened 时自动载入。</summary>
    public string? StartupFile { get; set; }

    private AppState? State => DataContext as AppState;

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (!_placementRestored)
            FitToScreen();

        if (State is not { } state)
            return;

        state.PickMediaFile = PickMediaAsync;
        state.PickFolder = PickFolderAsync;
        state.RevealPath = RevealAsync;
        state.RequestCredentialAsync = backend => SettingsPrompt.EnsureAsync(this, state, backend);
        state.RequestFfmpegAsync = () => FfmpegSetup.PromptAsync(this);

        DockChrome.StartCleanup(this);
        state.Initialize();

        // slim 版不带 ffmpeg：启动就先提醒，避免用户拖进视频后才看到一句报错。
        // 带启动文件时由 LoadMediaAsync 负责提示，免得连着弹两次
        if (string.IsNullOrWhiteSpace(StartupFile) && !await FfmpegSetup.PromptAsync(this))
            state.Status = Loc.T("Status.FfmpegMissing");

        if (!string.IsNullOrWhiteSpace(StartupFile))
            UiActions.SafeRun(Loc.T("Error.OpenFile"), () => state.LoadMediaAsync(StartupFile), state);
    }

    /// <summary>用可用语言重建底栏下拉，末尾附加「更多语言…」。</summary>
    private void BuildLanguageBox()
    {
        _languageSyncing = true;
        try
        {
            var choices = Loc.Available
                .Select(l => new LanguageChoice(l.Code, l.Name))
                .ToList();
            choices.Add(new LanguageChoice(MoreLanguages, Loc.T("Language.More")));

            LanguageBox.ItemsSource = choices;
            LanguageBox.SelectedItem = choices.FirstOrDefault(c => c.Code == Loc.Current) ?? choices[0];
        }
        finally
        {
            _languageSyncing = false;
        }
    }

    /// <summary>底栏语言下拉：直接切语言；选「更多语言…」则打开平铺选择窗（首次使用也是它）。</summary>
    private async void OnLanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_languageSyncing || LanguageBox.SelectedItem is not LanguageChoice choice)
            return;

        if (choice.Code != MoreLanguages)
        {
            // 等下拉收起、事件走完再换语言（换语言会重建下拉项）
            Avalonia.Threading.Dispatcher.UIThread.Post(() => State?.SetLanguage(choice.Code));
            return;
        }

        BuildLanguageBox();
        var code = await LanguagePickerWindow.PickAsync(this);
        if (!string.IsNullOrWhiteSpace(code))
            State?.SetLanguage(code);
    }

    /// <summary>关窗时把位置写进设置（最大化时记还原前的位置），下次启动原样恢复；停靠布局也一并存盘。</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (State is { } state)
        {
            var maximized = WindowState == WindowState.Maximized;
            var pos = maximized ? _lastNormalPos ?? Position : Position;
            var size = maximized ? _lastNormalSize ?? new Size(Width, Height) : new Size(Width, Height);
            state.SaveWindowPlacement(pos.X, pos.Y, size.Width, size.Height, maximized);
            Docking.DockLayoutStore.Save(state.Layout);
        }

        base.OnClosing(e);
    }

    /// <summary>底栏「重置布局」：四个面板回到默认排布。</summary>
    private void OnResetLayoutClick(object? sender, RoutedEventArgs e) => State?.ResetLayout();


    /// <summary>
    /// 还原上次的窗口位置。屏幕环境可能变了（拔副屏、改分辨率、DPI 变化），
    /// 所以尺寸先夹到当前工作区，再校验窗口在任意屏幕上还有足够的可见面积，不够就放弃还原。
    /// </summary>
    private void RestorePlacement(AsrSettings settings)
    {
        if (settings.WindowX is not { } x || settings.WindowY is not { } y
            || settings.WindowWidth <= 0 || settings.WindowHeight <= 0)
            return;

        var pos = new PixelPoint(x, y);
        var screen = Screens.ScreenFromPoint(pos) ?? Screens.Primary;
        var scale = screen is null || screen.Scaling <= 0 ? 1.0 : screen.Scaling;

        // 尺寸按 DIP 存，夹到目标屏的工作区（屏幕变小也能完整放下）
        var w = settings.WindowWidth;
        var h = settings.WindowHeight;
        if (screen is not null)
        {
            w = Math.Min(w, screen.WorkingArea.Width / scale - 40);
            h = Math.Min(h, screen.WorkingArea.Height / scale - 80);
        }

        // 可见性校验按像素算：至少露出 160px 宽、48px 高（够到标题栏就能拖回来）
        var rectPx = new PixelRect(pos, new PixelSize((int)(w * scale), (int)(h * scale)));
        var visible = Screens.All.Count == 0 || Screens.All.Any(sc =>
        {
            var inter = sc.WorkingArea.Intersect(rectPx);
            return inter.Width >= 160 && inter.Height >= 48;
        });
        if (!visible)
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = w;
        Height = h;
        Position = pos;
        _lastNormalPos = pos;
        _lastNormalSize = new Size(w, h);
        if (settings.WindowMaximized)
            WindowState = WindowState.Maximized;
        _placementRestored = true;
    }

    /// <summary>小屏（1366×768、1536×864）上默认尺寸会超出工作区，按屏幕收缩。</summary>
    private void FitToScreen()
    {
        if (Screens.Primary is not { } screen)
            return;

        var area = screen.WorkingArea;
        var scale = RenderScaling <= 0 ? 1.0 : RenderScaling;

        // 留出标题栏和任务栏的余量
        var maxWidth = area.Width / scale - 40;
        var maxHeight = area.Height / scale - 80;

        if (Width > maxWidth)
            Width = maxWidth;
        if (Height > maxHeight)
            Height = maxHeight;
    }

    /// <summary>只接受文件拖入，路径与扩展名交给媒体加载流程判断。</summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    /// <summary>拖入媒体后直接加载并抽音轨；识别仍要用户手动开始。</summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path) && State is { } state)
            UiActions.SafeRun(Loc.T("Error.OpenFile"), () => state.LoadMediaAsync(path), state);
    }

    /// <summary>点错误提示：把完整错误复制到剪贴板。</summary>
    private async void OnErrorClick(object? sender, PointerPressedEventArgs e)
    {
        if (State is not { Error: { Length: > 0 } error } state)
            return;

        try
        {
            if (GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(error);
                state.BusyText = Loc.T("Status.ErrorCopied");
            }
        }
        catch
        {
            // 剪贴板拿不到就算了
        }
    }

    /// <summary>权限类错误：以管理员身份重新启动自己。</summary>
    private void OnElevateClick(object? sender, RoutedEventArgs e)
    {
        var state = State;
        if (state is null || !OperatingSystem.IsWindows())
            return;

        if (Elevation.IsElevated)
        {
            state.Status = Loc.T("Status.AlreadyAdmin");
            return;
        }

        var args = state.Media?.Path is { Length: > 0 } path ? new[] { path } : Array.Empty<string>();
        if (Elevation.RelaunchElevated(args))
        {
            state.Status = Loc.T("Status.Elevated");
            Close();
        }
        else
        {
            state.Status = Loc.T("Status.ElevationCancelled");
        }
    }
}

/// <summary>底栏语言下拉的一项：Code 为 <c>__more__</c> 时表示「更多语言…」。</summary>
public sealed record LanguageChoice(string Code, string Name);
