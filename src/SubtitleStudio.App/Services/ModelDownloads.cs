using Avalonia.Threading;
using SubtitleStudio.Core.Asr;

namespace SubtitleStudio.App.Services;

/// <summary>
/// 模型下载的进程内登记处：把「谁在下载、下到哪了」从设置窗口里拿出来。
/// 这样关掉设置窗口下载不会断，软件退出后 .part 还在，下次启动还能接着续传。
/// </summary>
public static class ModelDownloads
{
    public sealed class Job
    {
        public required ModelSuggestion Model { get; init; }

        public CancellationTokenSource Cancellation { get; } = new();

        public bool Running { get; set; } = true;

        public bool Succeeded { get; set; }

        /// <summary>失败原因；取消时是一句「下次继续」。</summary>
        public string? Error { get; set; }

        public string Source { get; set; } = string.Empty;

        public long Received { get; set; }

        public long Total { get; set; }

        public double Fraction { get; set; }

        public DateTime FinishedAt { get; set; }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Job> Jobs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>状态有变（开始 / 进度 / 结束）。可能在后台线程触发，订阅方自己切 UI 线程。</summary>
    public static event Action? Changed;

    public static Job? Get(ModelSuggestion model)
    {
        lock (Gate)
            return Jobs.TryGetValue(model.FileName, out var job) ? job : null;
    }

    public static bool IsActive(ModelSuggestion model) => Get(model) is { Running: true };

    /// <summary>开始下载；同一个模型已经在下就什么也不做。</summary>
    public static void Start(ModelSuggestion model)
    {
        Job job;
        lock (Gate)
        {
            if (Jobs.TryGetValue(model.FileName, out var existing) && existing.Running)
                return;

            job = new Job { Model = model };
            Jobs[model.FileName] = job;
        }

        _ = RunAsync(job);
        RaiseChanged();
    }

    /// <summary>停止下载；已经写进 .part 的部分保留，下次接着续传。</summary>
    public static void Cancel(ModelSuggestion model)
    {
        var job = Get(model);
        if (job is not { Running: true })
            return;

        try
        {
            job.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 刚好下完并释放了，当作已经停了
        }
    }

    /// <summary>删除已下载的模型（连同半成品）；正在下载时不允许删。</summary>
    public static bool Delete(ModelSuggestion model)
    {
        lock (Gate)
        {
            if (Jobs.TryGetValue(model.FileName, out var job) && job.Running)
                return false;

            Jobs.Remove(model.FileName);
        }

        var target = Path.Combine(ModelCatalog.DefaultModelDirectory, model.FileName);
        DeleteFile(target);
        DeleteFile(target + ".part");
        DeleteFile(target + ".part.src");
        RaiseChanged();
        return true;
    }

    /// <summary>启动时把上次没下完的模型（ggml-*.bin.part）接着下，返回恢复的数量。</summary>
    public static int ResumeIncomplete()
    {
        var started = 0;
        foreach (var (fileName, _) in ModelDownloader.FindIncomplete())
        {
            var model = ModelCatalog.Suggestions.FirstOrDefault(m =>
                string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));
            if (model is null)
                continue;

            Start(model);
            started++;
        }

        return started;
    }

    private static async Task RunAsync(Job job)
    {
        var progress = new Progress<DownloadProgress>(p =>
        {
            job.Source = p.SourceName;
            job.Received = p.Received;
            job.Total = p.Total;
            job.Fraction = p.Fraction;
            RaiseChanged();
        });

        try
        {
            await Task.Run(() => ModelDownloader.DownloadAsync(
                job.Model, null, progress, job.Cancellation.Token));
            job.Succeeded = true;
        }
        catch (OperationCanceledException)
        {
            job.Error = Loc.T("Settings.Model.StoppedShort");
        }
        catch (Exception ex)
        {
            job.Error = ex.Message;
        }
        finally
        {
            job.FinishedAt = DateTime.Now;
            job.Running = false;
            try
            {
                job.Cancellation.Dispose();
            }
            catch
            {
                // 释放失败无所谓
            }

            RaiseChanged();
        }
    }

    private static void RaiseChanged()
    {
        var handler = Changed;
        if (handler is null)
            return;

        if (Dispatcher.UIThread.CheckAccess())
            handler();
        else
            Dispatcher.UIThread.Post(handler);
    }

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 删不掉就留着，下次下载还会覆盖
        }
    }
}
