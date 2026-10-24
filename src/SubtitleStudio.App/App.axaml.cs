using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SubtitleStudio.App.Services;
using SubtitleStudio.App.ViewModels;
using SubtitleStudio.App.Views;
using SubtitleStudio.Core.Asr;

namespace SubtitleStudio.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        CrashReporter.Install();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 用户导入的字体按文件加载，不进系统字体库
            SubtitleStudio.Core.Export.SubtitleLayerRenderer.FontFileResolver = FontInstaller.Resolve;

            // 语言要在建窗口前定下来：XAML 里的 DynamicResource 第一次就能取到正确文案
            var settings = AsrSettingsStore.Load();
            Loc.Load();

            var envLanguage = Environment.GetEnvironmentVariable("SUBTITLESTUDIO_LANG");
            Loc.Use(string.IsNullOrWhiteSpace(envLanguage) ? settings.Language : envLanguage);

            // 从没选过语言（也没被环境变量指定）→ 首次启动弹平铺选择窗
            var firstRun = string.IsNullOrWhiteSpace(envLanguage) && string.IsNullOrWhiteSpace(settings.Language);

            var state = new AppState();
            var window = new MainWindow { DataContext = state };
            // 命令行带文件参数时直接打开（便于"右键 → 打开方式"或自动化验证）
            var startupFile = desktop.Args?.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(startupFile) && File.Exists(startupFile))
                window.StartupFile = Path.GetFullPath(startupFile);
            desktop.MainWindow = window;

            if (firstRun)
            {
                window.Opened += async (_, _) =>
                {
                    var code = await LanguagePickerWindow.PickAsync(window);
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        state.SetLanguage(code);
                    }
                    else
                    {
                        // 直接关掉也算选过了：记下当前（跟随系统）的语言，下次不再弹
                        state.PersistLanguage();
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
