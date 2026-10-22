using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SubtitleStudio.App.Services;
using SubtitleStudio.App.ViewModels;

namespace SubtitleStudio.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);

        try
        {
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://SubtitleStudio/Assets/icon.png")));
        }
        catch
        {
            // 图标不是关键路径，缺了不影响使用
        }
    }

    /// <summary>启动参数里的媒体文件，OnOpened 时自动载入。</summary>
    public string? StartupFile { get; set; }

    private AppState? State => DataContext as AppState;

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToScreen();

        if (State is not { } state)
            return;

        state.PickMediaFile = PickMediaAsync;
        state.PickFolder = PickFolderAsync;
        state.RevealPath = RevealAsync;
        state.RequestCredentialAsync = backend => SettingsPrompt.EnsureAsync(this, state, backend);

        DockChrome.StartCleanup(this);
        state.Initialize();

        if (!string.IsNullOrWhiteSpace(StartupFile))
            UiActions.SafeRun("打开文件失败", () => state.LoadMediaAsync(StartupFile), state);
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

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path) && State is { } state)
            UiActions.SafeRun("打开文件失败", () => state.LoadMediaAsync(path), state);
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
                state.BusyText = "错误信息已复制到剪贴板";
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
            state.Status = "已经是管理员身份了，换个可写目录试试";
            return;
        }

        var args = state.Media?.Path is { Length: > 0 } path ? new[] { path } : Array.Empty<string>();
        if (Elevation.RelaunchElevated(args))
        {
            state.Status = "已用管理员身份重新启动";
            Close();
        }
        else
        {
            state.Status = "提权被取消";
        }
    }
}
