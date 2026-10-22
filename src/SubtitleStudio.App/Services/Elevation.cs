using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace SubtitleStudio.App.Services;

/// <summary>权限相关：判断是否已是管理员、以管理员身份重启自己。</summary>
[SupportedOSPlatform("windows")]
public static class Elevation
{
    private static bool? _elevated;

    public static bool IsElevated
    {
        get
        {
            if (_elevated is { } cached)
                return cached;

            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                _elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                _elevated = false;
            }

            return _elevated.Value;
        }
    }

    /// <summary>
    /// 用 UAC 提权启动一个新的自己（当前进程随后关闭）。
    /// 返回 false 表示用户点了「否」，或系统不允许提权。
    /// </summary>
    public static bool RelaunchElevated(params string[] arguments)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return false;

            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas"
            };

            foreach (var arg in arguments)
                info.ArgumentList.Add(arg);

            Process.Start(info);
            return true;
        }
        catch
        {
            // 用户在 UAC 弹窗里点了取消
            return false;
        }
    }
}
