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
            return new Cause(directory, false, Loc.T("Export.Reason.NoDir"));

        if (!Directory.Exists(directory))
            return new Cause(directory, false, Loc.T("Export.Reason.Missing"));

        try
        {
            var probe = Path.Combine(directory, $".subtitle-studio-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return new Cause(directory, false, Loc.T("Export.Reason.Writable"));
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
            return Loc.T(looksProtected ? "Export.Reason.Controlled" : "Export.Reason.ControlledMaybe");
        }

        if (ex is UnauthorizedAccessException)
            return Loc.T("Export.Reason.Acl");

        if (ex is IOException io && io.Message.Contains("disk", StringComparison.OrdinalIgnoreCase))
            return Loc.T("Export.Reason.Disk");

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
