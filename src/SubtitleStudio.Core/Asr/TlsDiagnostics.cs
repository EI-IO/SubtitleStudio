using System.Net.Http;
using System.Security.Authentication;

namespace SubtitleStudio.Core.Asr;

/// <summary>
/// 把 Windows Schannel 的典型故障翻译成能照着做的中文提示。
/// 这类错误（SEC_E_NO_CREDENTIALS / 安全包中没有可用的凭证）跟程序无关，
/// 是系统 TLS 栈建不出客户端凭证，用户自己看到英文原文通常一头雾水。
/// </summary>
public static class TlsDiagnostics
{
    private static readonly string[] Markers =
    {
        "安全包中没有可用的凭证",
        "SEC_E_NO_CREDENTIALS",
        "0x8009030e",
        "The SSL connection could not be established",
        "Authentication failed"
    };

    public static bool IsTlsFailure(Exception? error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is AuthenticationException)
                return true;

            foreach (var marker in Markers)
            {
                if (e.Message.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>失败时追加在原始错误后面的检修步骤。</summary>
    public static string Hint(Exception? error) => IsTlsFailure(error)
        ? """

          —— 这是 Windows 的 TLS 栈（Schannel）建不出客户端凭证，不是本程序的问题。按顺序试：
          1) 确认不是跑在沙箱/容器里：whoami /groups 里出现 "Low Mandatory Level" 或 "AppContainer" 就是这个原因，换普通桌面进程运行；
          2) 用管理员身份执行：net stop cryptsvc / net start cryptsvc，再 net start KeyIso；
          3) 检查目录权限：%APPDATA%\Microsoft\Crypto 与 C:\ProgramData\Microsoft\Crypto\RSA\MachineKeys 需要当前用户/SYSTEM 可读写；
          4) 若 Security Packages 被清空：reg query "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" /v "Security Packages"
             正常应含 kerberos、msv1_0、schannel、tspkg、pku2u；
          5) netsh winsock reset 后重启，仍不行再 sfc /scannow；
          6) 关掉会拦 HTTPS 的安全软件（360/火绒/卡巴）的网页防护再试。
          """.TrimEnd()
        : string.Empty;

    /// <summary>拼一段可读的错误文本：原始消息 + 必要的检修提示。</summary>
    public static string Describe(Exception? error)
    {
        if (error is null)
            return "未知错误";

        var message = error.Message;
        var hint = Hint(error);
        return string.IsNullOrEmpty(hint) ? message : message + Environment.NewLine + hint;
    }

    /// <summary>把网络异常按 TLS / 超时 / 其他归类，便于界面给出不同措辞。</summary>
    public static string Classify(Exception? error)
    {
        if (IsTlsFailure(error))
            return "tls";

        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is TaskCanceledException or TimeoutException)
                return "timeout";
        }

        return "other";
    }
}
