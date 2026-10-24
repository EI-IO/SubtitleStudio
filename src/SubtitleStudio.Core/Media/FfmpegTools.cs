using System.Diagnostics;
using System.Text;

namespace SubtitleStudio.Core.Media;

/// <summary>定位并调用 ffmpeg / ffprobe。</summary>
public static class FfmpegTools
{
    private static string? _ffmpeg;
    private static string? _ffprobe;

    public static string? FfmpegPath
    {
        get => _ffmpeg ??= Locate("ffmpeg");
        set => _ffmpeg = value;
    }

    public static string? FfprobePath
    {
        get => _ffprobe ??= Locate("ffprobe");
        set => _ffprobe = value;
    }

    public static bool Available => !string.IsNullOrEmpty(FfmpegPath) && !string.IsNullOrEmpty(FfprobePath);

    private static string ExeName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private static string? Locate(string name)
    {
        var overridePath = Environment.GetEnvironmentVariable($"SUBTITLESTUDIO_{name.ToUpperInvariant()}");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        var exe = ExeName(name);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        // 向上最多找 6 层，覆盖 bin/Debug/net10.0 这种结构
        for (var i = 0; dir != null && i < 6; i++, dir = dir.Parent)
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(dir.FullName, "tools", "ffmpeg", "bin", exe),
                         Path.Combine(dir.FullName, "tools", "ffmpeg", exe),
                         Path.Combine(dir.FullName, exe)
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var part in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(part.Trim(), exe);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // 忽略非法 PATH 项
            }
        }

        return null;
    }

    /// <summary>
    /// 组装 -map 参数：ffmpeg 的「全局流索引」写法（0:3 = 第一个输入的第 3 号流）。
    /// 索引小于 0 时退回「第一条音轨」。
    /// </summary>
    public static string StreamMap(int audioStreamIndex) =>
        audioStreamIndex < 0 ? "0:a:0" : $"0:{audioStreamIndex}";
    public static Process Start(string exe, IEnumerable<string> args, bool redirectStdout = false)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = redirectStdout,
            RedirectStandardInput = false,
            StandardErrorEncoding = Encoding.UTF8
        };

        // 只有确实重定向了 stdout 才能设编码，否则 .NET 直接抛异常
        if (redirectStdout)
            psi.StandardOutputEncoding = Encoding.UTF8;

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return Process.Start(psi) ?? throw new InvalidOperationException($"无法启动 {exe}");
    }

    /// <summary>运行并返回 (exitCode, stderr)。取消时杀掉进程，不留孤儿 ffmpeg。</summary>
    public static async Task<(int Code, string StdErr)> RunAsync(
        string exe,
        IEnumerable<string> args,
        CancellationToken cancellationToken = default)
    {
        using var process = Start(exe, args);
        // stderr 不带取消令牌：进程被杀后管道关闭，读取自然结束
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            try { await stderrTask.ConfigureAwait(false); } catch { /* 忽略 */ }
            throw;
        }

        return (process.ExitCode, await stderrTask.ConfigureAwait(false));
    }

    /// <summary>尽力杀掉进程树（取消/失败收尾用），任何失败都吞掉。</summary>
    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // 已经退出
        }
    }

    public static async Task EnsureSuccessAsync(string exe, IEnumerable<string> args, CancellationToken ct = default)
    {
        var (code, stderr) = await RunAsync(exe, args, ct).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} 执行失败 (exit {code}):\n{stderr}");
    }
}
