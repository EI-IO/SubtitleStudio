using SubtitleStudio.App.ViewModels;

namespace SubtitleStudio.App.Services;

/// <summary>
/// 界面里所有 async void 事件都从这里走一遍：
/// 抛出什么都不会变成「应用程序发生异常 0xe0434352」，而是记日志 + 状态栏提示。
/// </summary>
public static class UiActions
{
    public static async void SafeRun(string context, Func<Task> action, AppState? state = null)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // 用户主动取消，不算错误
        }
        catch (Exception ex)
        {
            var message = CrashReporter.Report(context, ex);
            try
            {
                if (state is not null)
                {
                    state.Status = message;
                    state.ReportError(message);
                }
            }
            catch
            {
                // 界面已经没了，日志里有就够了
            }
        }
    }
}
