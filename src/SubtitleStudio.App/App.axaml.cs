using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SubtitleStudio.App.ViewModels;
using SubtitleStudio.App.Views;

namespace SubtitleStudio.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services.CrashReporter.Install();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 用户导入的字体按文件加载，不进系统字体库
            SubtitleStudio.Core.Export.SubtitleLayerRenderer.FontFileResolver = Services.FontInstaller.Resolve;

            var state = new AppState();
            var window = new MainWindow { DataContext = state };
            // 命令行带文件参数时直接打开（便于"右键 → 打开方式"或自动化验证）
            var startupFile = desktop.Args?.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(startupFile) && File.Exists(startupFile))
                window.StartupFile = Path.GetFullPath(startupFile);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();

        // 语言：SUBTITLESTUDIO_LANG 优先，否则跟随系统
        Services.Loc.LoadFromFiles();
        Services.Loc.Use(Environment.GetEnvironmentVariable("SUBTITLESTUDIO_LANG"));
    }
}
