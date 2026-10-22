using Avalonia;
using SubtitleStudio.App.ViewModels;
using SubtitleStudio.App.Views;
using SubtitleStudio.Core.Asr;

namespace SubtitleStudio.App.Services;

/// <summary>缺凭证时的统一提示：问一次 → 打开设置 → 返回现在能不能用。</summary>
public static class SettingsPrompt
{
    public static string DisplayName(AsrBackend backend) => backend switch
    {
        AsrBackend.SiliconFlow => Loc.T("Menu.SiliconFlow"),
        AsrBackend.TeleAi => Loc.T("Menu.TeleAi"),
        _ => Loc.T("Menu.LocalModels")
    };

    /// <summary>问用户要不要去设置填凭证；填好返回 true。</summary>
    public static async Task<bool> EnsureAsync(Visual? owner, AppState state, AsrBackend backend)
    {
        if (state.Settings.IsReady(backend))
            return true;

        var window = Dialogs.OwnerOf(owner);
        if (window is null)
            return false;

        var name = DisplayName(backend);
        var go = await Dialogs.ConfirmAsync(
            window,
            Loc.T("Menu.NoKey"),
            $"{name} {Loc.T("Menu.NeedKeySuffix")}",
            Loc.T("Menu.GoSettings"),
            Loc.T("Menu.Later"));

        if (!go)
            return false;

        var settings = new SettingsWindow(state.Settings) { Saved = state.ApplySettings };
        await settings.ShowDialog(window);
        return state.Settings.IsReady(backend);
    }
}
