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
        AsrBackend.Custom => Loc.T("Label.CustomProvider"),
        _ => Loc.T("Menu.LocalModels")
    };

    /// <summary>问用户要不要去设置填凭证；填好返回 true。</summary>
    public static async Task<bool> EnsureAsync(Visual? owner, AppState state, AsrBackend backend)
    {
        if (state.Settings.IsReady(backend))
            return true;

        var name = DisplayName(backend);
        var go = await AskGoSettings(owner, name);
        if (!go)
            return false;

        var window = Dialogs.OwnerOf(owner);
        if (window is null)
            return false;

        var settings = new SettingsWindow(state.Settings) { Saved = state.ApplySettings };
        await settings.ShowDialog(window);
        return state.Settings.IsReady(backend);
    }

    /// <summary>自定义提供商版本：按 Id 复查（设置里可能改名、也可能被删掉）。</summary>
    public static async Task<bool> EnsureAsync(Visual? owner, AppState state, CustomProvider provider)
    {
        if (AsrSettings.IsReady(provider))
            return true;

        var go = await AskGoSettings(owner, provider.Name);
        if (!go)
            return false;

        var window = Dialogs.OwnerOf(owner);
        if (window is null)
            return false;

        var settings = new SettingsWindow(state.Settings) { Saved = state.ApplySettings };
        await settings.ShowDialog(window);

        var updated = state.Settings.CustomProviders.FirstOrDefault(p => p.Id == provider.Id);
        return AsrSettings.IsReady(updated);
    }

    private static async Task<bool> AskGoSettings(Visual? owner, string name)
    {
        var window = Dialogs.OwnerOf(owner);
        if (window is null)
            return false;

        return await Dialogs.ConfirmAsync(
            window,
            Loc.T("Menu.NoKey"),
            $"{name} {Loc.T("Menu.NeedKeySuffix")}",
            Loc.T("Menu.GoSettings"),
            Loc.T("Menu.Later"));
    }
}
