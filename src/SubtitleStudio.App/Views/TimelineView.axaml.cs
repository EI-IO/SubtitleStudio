using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SubtitleStudio.App.Services;
using SubtitleStudio.App.ViewModels;
using SubtitleStudio.Core.Asr;


namespace SubtitleStudio.App.Views;

/// <summary>时间轴面板：打开文件、开始/停止识别、播放、缩放，以及拼装模型菜单。</summary>
public partial class TimelineView : UserControl
{
    public TimelineView()
    {
        InitializeComponent();
    }

    private AppState? State => (DataContext as TimelineViewModel)?.State;

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (State is { } state)
            UiActions.SafeRun(Loc.T("Error.OpenFile"), state.OpenFileAsync, state);
    }

    private void OnRetranscribeClick(object? sender, RoutedEventArgs e)
    {
        if (State is { } state)
            UiActions.SafeRun(Loc.T("Error.Recognition"), state.RunRecognitionAsync, state);
    }

    private void OnCancelRecognition(object? sender, RoutedEventArgs e) => State?.CancelRecognition();

    private void OnPlayPause(object? sender, RoutedEventArgs e)
    {
        if (State is { } state)
            UiActions.SafeRun(Loc.T("Error.Playback"), state.TogglePlayAsync, state);
    }

    /// <summary>模型菜单：本地模型 ▸ / 硅基流动 ▸ / 天翼星辰 ▸ / 设置。</summary>
    private void OnModelClick(object? sender, RoutedEventArgs e)
    {
        var state = State;
        if (state is null || sender is not Button button)
            return;

        state.RefreshModels();

        var flyout = new MenuFlyout();
        flyout.Items.Add(BuildLocalItem(state));

        var (siliconItem, teleItem) = BuildCloudItems(state);
        flyout.Items.Add(siliconItem);
        flyout.Items.Add(teleItem);

        // 用户在设置里加的 OpenAI 兼容提供商，跟在两个内置云端后面；
        // 跑过连接测试的带模型二级菜单，没跑过的整项点击即选用
        foreach (var provider in state.Settings.CustomProviders)
        {
            var captured = provider;
            var active = state.Backend == AsrBackend.Custom && state.Settings.CustomProviderId == provider.Id;

            if (provider.CachedModels.Count == 0)
            {
                var item = new MenuItem
                {
                    Header = provider.Name,
                    Icon = active ? Check() : null
                };
                item.Click += (_, _) => UiActions.SafeRun(Loc.T("Error.SwitchModel"), async () =>
                {
                    if (!await SettingsPrompt.EnsureAsync(this, state, captured))
                        return;
                    state.UseCustomProvider(captured);
                }, state);
                flyout.Items.Add(item);
                continue;
            }

            var parent = new MenuItem
            {
                Header = provider.Name,
                Icon = active ? Check() : null
            };
            foreach (var modelId in provider.CachedModels)
            {
                var capturedModel = modelId;
                var sub = new MenuItem
                {
                    Header = modelId,
                    Icon = active && string.Equals(provider.Model, modelId, StringComparison.Ordinal) ? Check() : null
                };
                sub.Click += (_, _) => UiActions.SafeRun(Loc.T("Error.SwitchModel"), async () =>
                {
                    if (!await SettingsPrompt.EnsureAsync(this, state, captured))
                        return;
                    state.UseCustomProviderModel(captured, capturedModel);
                }, state);
                parent.Items.Add(sub);
            }
            flyout.Items.Add(parent);
        }

        flyout.Items.Add(new Separator());

        var settings = new MenuItem { Header = Loc.T("Menu.Settings") };
        settings.Click += (_, _) => UiActions.SafeRun(Loc.T("Error.OpenSettings"), async () =>
        {
            await OpenSettingsAsync(state);
        }, state);
        flyout.Items.Add(settings);

        flyout.ShowAt(button);
    }

    private MenuItem BuildLocalItem(AppState state)
    {
        var local = new MenuItem
        {
            Header = Loc.T("Menu.LocalModels"),
            Icon = state.Backend == AsrBackend.Local ? Check() : null
        };

        if (state.LocalModels.Count == 0)
        {
            local.Items.Add(new MenuItem { Header = Loc.T("Menu.NoLocalModel"), IsEnabled = false });
            return local;
        }

        foreach (var model in state.LocalModels)
        {
            var item = new MenuItem
            {
                Header = Loc.T("Menu.LocalModelItem", model.Display, model.SizeLabel),
                Icon = state.SelectedModel?.FullPath == model.FullPath && state.Backend == AsrBackend.Local
                    ? Check()
                    : null
            };
            var captured = model;
            item.Click += (_, _) => state.UseLocalModel(captured);
            local.Items.Add(item);
        }

        return local;
    }

    /// <summary>云端两个后端各一个二级菜单，列出可用（语音识别类）模型。</summary>
    private (MenuItem Silicon, MenuItem Tele) BuildCloudItems(AppState state)
    {
        var silicon = new MenuItem
        {
            Header = Loc.T("Menu.SiliconFlow"),
            Icon = state.Backend == AsrBackend.SiliconFlow ? Check() : null
        };

        if (state.SiliconFlowModels.Count == 0)
        {
            silicon.Items.Add(new MenuItem { Header = Loc.T("Menu.NoCloudModel"), IsEnabled = false });
        }
        else
        {
            foreach (var model in state.SiliconFlowModels)
            {
                var item = new MenuItem
                {
                    Header = model.Label,
                    Icon = state.Backend == AsrBackend.SiliconFlow
                           && string.Equals(state.Settings.SiliconFlowModel, model.Id, StringComparison.Ordinal)
                        ? Check()
                        : null
                };
                var captured = model;
                item.Click += (_, _) => UiActions.SafeRun(Loc.T("Error.SwitchModel"), async () =>
                {
                    if (!await SettingsPrompt.EnsureAsync(this, state, AsrBackend.SiliconFlow))
                        return;
                    state.UseSiliconFlowModel(captured);
                }, state);
                silicon.Items.Add(item);
            }
        }

        silicon.Items.Add(new Separator());
        var refresh = new MenuItem { Header = Loc.T("Menu.RefreshModels") };
        refresh.Click += (_, _) => UiActions.SafeRun(Loc.T("Error.RefreshModels"), async () =>
        {
            if (!await SettingsPrompt.EnsureAsync(this, state, AsrBackend.SiliconFlow))
                return;
            state.Status = await state.RefreshSiliconFlowModelsAsync();
            OnModelClick(null, new RoutedEventArgs());
        }, state);
        silicon.Items.Add(refresh);

        var tele = new MenuItem
        {
            Header = Loc.T("Menu.TeleAi"),
            Icon = state.Backend == AsrBackend.TeleAi ? Check() : null
        };

        // 天翼星辰现在只有短语音识别一个入口，二级菜单里放一项即可
        var teleItem = new MenuItem
        {
            Header = AppState.TeleAiEntry,
            Icon = state.Backend == AsrBackend.TeleAi ? Check() : null
        };
        teleItem.Click += (_, _) => UiActions.SafeRun(Loc.T("Error.SwitchModel"), async () =>
        {
            if (!await SettingsPrompt.EnsureAsync(this, state, AsrBackend.TeleAi))
                return;
            state.UseTeleAi();
        }, state);
        tele.Items.Add(teleItem);

        return (silicon, tele);
    }

    private static TextBlock Check() => new() { Text = "✓" };

    private async Task OpenSettingsAsync(AppState state)
    {
        var owner = Dialogs.OwnerOf(this);
        if (owner is null)
            return;

        var window = new SettingsWindow(state.Settings)
        {
            Saved = state.ApplySettings,
            CloudModelsFetched = state.OnCloudModelsFetched
        };
        await window.ShowDialog(owner);
    }
    private void OnZoomIn(object? sender, RoutedEventArgs e)
    {
        if (State is { } state)
            state.PixelsPerSecond = System.Math.Clamp(state.PixelsPerSecond * 1.4, 8, 1200);
    }

    private void OnZoomOut(object? sender, RoutedEventArgs e)
    {
        if (State is { } state)
            state.PixelsPerSecond = System.Math.Clamp(state.PixelsPerSecond / 1.4, 8, 1200);
    }
}
