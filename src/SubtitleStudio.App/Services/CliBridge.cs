using System.Diagnostics;
using System.Text.Json;
using SubtitleStudio.Core.Asr;
using SubtitleStudio.Core.Media;

namespace SubtitleStudio.App.Services;

/// <summary>识别结果里的一段话（与 CLI 的 --asr-json 输出对齐）。</summary>
public sealed record CliSegment(string Text, double Start, double End);

public sealed record CliAsrResult(string? Language, List<CliSegment> Segments);

/// <summary>
/// 把识别这一步丢进 CLI 子进程跑。
/// 关键收益：whisper.cpp 在子进程里，「停止」直接杀进程就行，
/// 不用等一条长音频跑完（在主进程里中途拆原生库会撞访问违例）。
/// </summary>
public static class CliBridge
{
    private static string? _cached;

    /// <summary>
    /// 找 SubtitleStudio.Cli.exe：先看程序目录（发布形态），再按 App 自身路径推导
    /// 同配置/同目标框架的 CLI 构建输出（开发形态）。
    /// 不在 bin 下盲目递归搜，避免捡到旧构建或别的架构的产物。
    /// </summary>
    public static string? Locate()
    {
        if (_cached is not null)
            return File.Exists(_cached) ? _cached : null;

        var exe = OperatingSystem.IsWindows() ? "SubtitleStudio.Cli.exe" : "SubtitleStudio.Cli";

        var direct = Path.Combine(AppContext.BaseDirectory, exe);
        if (File.Exists(direct))
            return _cached = direct;

        // App 输出目录形如 .../SubtitleStudio.App/bin/Debug/net10.0/，
        // 往上三层拿到项目目录，CLI 的同配置产物就在旁边的 SubtitleStudio.Cli 下。
        var tfm = new DirectoryInfo(AppContext.BaseDirectory);
        var config = tfm.Parent;
        var bin = config?.Parent;
        var appProject = bin?.Parent;

        if (appProject is not null
            && appProject.Name.Equals("SubtitleStudio.App", StringComparison.OrdinalIgnoreCase)
            && bin!.Name.Equals("bin", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(
                appProject.Parent!.FullName, "SubtitleStudio.Cli", "bin", config!.Name, tfm.Name, exe);
            if (File.Exists(candidate))
                return _cached = candidate;
        }

        return null;
    }

    /// <summary>跑一次识别；取消时直接杀进程树。</summary>
    public static async Task<CliAsrResult> TranscribeAsync(
        string wavPath,
        string modelPath,
        string? language,
        IProgress<AsrProgress>? progress,
        CancellationToken cancellationToken)
    {
        var cli = Locate() ?? throw new FileNotFoundException(Loc.T("Error.CliMissing"));

        // 结果文件放在临时工作区里（%TEMP% 在受限环境下可能写不进去），和 wav 同一个目录
        var jsonPath = TempWorkspace.CreateFilePath(wavPath, ".json");

        var start = new ProcessStartInfo(cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("--input");
        start.ArgumentList.Add(wavPath);
        start.ArgumentList.Add("--model");
        start.ArgumentList.Add(modelPath);
        start.ArgumentList.Add("--asr-json");
        start.ArgumentList.Add(jsonPath);
        if (!string.IsNullOrWhiteSpace(language))
        {
            start.ArgumentList.Add("--lang");
            start.ArgumentList.Add(language);
        }

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var errors = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            var line = e.Data;
            if (line is null)
                return;

            if (line.StartsWith("PROGRESS ", StringComparison.Ordinal) &&
                double.TryParse(line["PROGRESS ".Length..], out var fraction))
            {
                progress?.Report(new AsrProgress(Math.Clamp(fraction, 0, 1), Loc.T("Busy.RecognizingPercent", fraction * 100)));
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                lock (errors)
                    errors.Add(e.Data!);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode != 0 || !File.Exists(jsonPath))
        {
            string tail;
            lock (errors)
                tail = string.Join("\n", errors.TakeLast(6));
            throw new InvalidOperationException(
                $"{Loc.T("Error.CliExit", process.ExitCode)}{Environment.NewLine}{tail}".TrimEnd());
        }

        try
        {
            await using var stream = File.OpenRead(jsonPath);
            var payload = await JsonSerializer.DeserializeAsync<CliAsrResult>(stream, JsonOptions);
            if (payload is null)
                throw new InvalidOperationException(Loc.T("Error.CliEmpty"));

            progress?.Report(new AsrProgress(1, Loc.T("Busy.RecognitionDone")));
            return payload;
        }
        finally
        {
            TryDelete(jsonPath);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static void TryKill(Process process)
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 临时文件删不掉不影响主流程
        }
    }
}
