using Avalonia;

namespace SubtitleStudio.App;

/// <summary>桌面程序入口：建立 Avalonia 应用并进入桌面生命周期；启动流程在 <see cref="App"/>。</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
