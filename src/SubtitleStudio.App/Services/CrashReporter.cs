using System.Text;

namespace SubtitleStudio.App.Services;

/// <summary>
/// 兜底：把没人接住的异常写进日志，而不是直接弹「应用程序发生异常 0xe0434352」。
/// 日志位置：%LOCALAPPDATA%\SubtitleStudio\crash.log
/// </summary>
public static class CrashReporter
{
    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(LogDirectory(), "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        try
        {
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Write("Dispatcher.UnhandledException", e.Exception);
                e.Handled = true; // 留在界面上：状态栏会给提示，不至于直接退出
            };
        }
        catch
        {
            // 老版本 Avalonia 没有这个事件，忽略
        }
    }

    /// <summary>把异常记到日志，返回给用户看的简短描述。</summary>
    public static string Report(string context, Exception ex)
    {
        Write(context, ex);
        return $"{context}：{Flatten(ex)}";
    }

    public static string Flatten(Exception ex)
    {
        var text = ex.Message;
        var inner = ex.InnerException;
        while (inner is not null)
        {
            text += " ← " + inner.Message;
            inner = inner.InnerException;
        }

        return text.Replace('\n', ' ').Replace('\r', ' ').Trim();
    }

    private static void Write(string context, Exception? ex)
    {
        if (ex is null)
            return;

        try
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ").Append(context).AppendLine();
            sb.AppendLine(ex.ToString());
            sb.AppendLine(new string('-', 80));

            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory());
                File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // 连日志都写不了就算了，别在崩溃处理里再崩一次
        }
    }

    private static string LogDirectory()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
            local = AppContext.BaseDirectory;

        return Path.Combine(local, "SubtitleStudio");
    }
}
