using Microsoft.Win32;

namespace SubtitleStudio.App.Services;

/// <summary>判断导出目录为什么写不进去，并给一句人话解释。</summary>
public static class ExportDiagnostics
{
    public sealed record Cause(string Directory, bool IsPermission, string Explain);

    private const string CfaKey =
        @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access";

    public static Cause Diagnose(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return new Cause(directory, false, "没有指定目录");

        if (!Directory.Exists(directory))
            return new Cause(directory, false, "目录不存在");

        try
        {
            var probe = Path.Combine(directory, $".subtitle-studio-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return new Cause(directory, false, "可写");
        }
        catch (Exception ex)
        {
            return new Cause(directory, true, Explain(directory, ex));
        }
    }

    /// <summary>按 Windows 上的常见原因逐条排查。</summary>
    private static string Explain(string directory, Exception ex)
    {
        if (IsControlledFolderAccessOn())
        {
            var looksProtected = LooksLikeProtectedFolder(directory);
            return looksProtected
                ? $"被 Windows「受控文件夹访问」拦住了（桌面/文档/图片等目录默认受保护）。"
                    + "到「Windows 安全中心 → 病毒和威胁防护 → 勒索软件防护 → 受控文件夹访问」里允许本程序，或用管理员身份运行。"
                : "Windows「受控文件夹访问」开着，写入可能被拦截。";
        }

        if (ex is UnauthorizedAccessException)
            return "当前用户对这个目录没有写权限（ACL 限制），换个目录或以管理员身份运行。";

        if (ex is IOException io && io.Message.Contains("disk", StringComparison.OrdinalIgnoreCase))
            return "磁盘不可写（可能只读或已满）。";

        return ex.Message;
    }

    private static bool IsControlledFolderAccessOn()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(CfaKey);
            var value = key?.GetValue("EnableControlledFolderAccess");
            return value is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeProtectedFolder(string directory)
    {
        var known = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        foreach (var folder in known)
        {
            if (string.IsNullOrWhiteSpace(folder))
                continue;

            if (directory.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
