# 诊断 Windows 的 TLS（Schannel）为什么连不上 HTTPS。
# 用法：右键「使用 PowerShell 运行」，或在普通 PowerShell 里执行 .\diagnose-tls.ps1
# 只读检查 + 一次真实握手，不会修改任何东西。

$ErrorActionPreference = 'Continue'
function Line($text) { Write-Host $text }
function Section($title) { Write-Host "`n=== $title ===" -ForegroundColor Cyan }

Section '1. 当前进程的完整性级别'
$groups = & whoami /groups 2>$null
$il = ($groups | Select-String -Pattern 'Mandatory Label|强制标签').Line
if ($il) { Line ("  " + $il.Trim()) }
if ($il -match 'Low|低|AppContainer') {
    Write-Host '  ✗ 这是 Low / AppContainer 沙箱进程：Schannel 建凭证要往 %APPDATA%\Microsoft\Crypto 写临时密钥容器，' -ForegroundColor Yellow
    Write-Host '    低完整性级别写不进去，就会报 SEC_E_NO_CREDENTIALS。换普通桌面进程（资源管理器双击）即可。' -ForegroundColor Yellow
}

Section '2. 相关服务'
foreach ($name in 'KeyIso', 'CryptSvc', 'WinHttpAutoProxySvc', 'Dnscache') {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($svc) {
        $color = if ($svc.Status -eq 'Running') { 'Green' } else { 'Red' }
        Write-Host ("  {0,-20} {1}" -f $svc.Name, $svc.Status) -ForegroundColor $color
    } else {
        Write-Host ("  {0,-20} 不存在" -f $name) -ForegroundColor Red
    }
}

Section '3. 密钥目录权限（Schannel 需要能写）'
foreach ($dir in @("$env:APPDATA\Microsoft\Crypto", 'C:\ProgramData\Microsoft\Crypto\RSA\MachineKeys')) {
    if (-not (Test-Path $dir)) { Write-Host "  $dir 不存在" -ForegroundColor Yellow; continue }
    $probe = Join-Path $dir '.tls-write-probe'
    try {
        Set-Content -Path $probe -Value 'x' -ErrorAction Stop
        Remove-Item $probe -Force
        Write-Host "  ✓ 可写  $dir" -ForegroundColor Green
    } catch {
        Write-Host "  ✗ 不可写 $dir" -ForegroundColor Red
        Write-Host "    $($_.Exception.Message.Split([char]10)[0])" -ForegroundColor DarkGray
    }
}

Section '4. LSA 安全包与 Schannel 注册'
$pkg = & reg query 'HKLM\SYSTEM\CurrentControlSet\Control\Lsa' /v 'Security Packages' 2>$null
$pkg
if ($pkg -match 'REG_MULTI_SZ\s*$' -or $pkg -match 'REG_MULTI_SZ\s+""') {
    Write-Host '  ✗ Security Packages 是空的。正常应包含 kerberos、msv1_0、schannel、tspkg、pku2u。' -ForegroundColor Yellow
    Write-Host '    修复（管理员）：先导出备份，再把这几个包写回去，然后重启。' -ForegroundColor Yellow
}
& reg query 'HKLM\SYSTEM\CurrentControlSet\Control\Lsa\SspiCache\Schannel' 2>$null | Select-Object -First 6

Section '5. 代理设置（有代理时先确认代理本身能用）'
& netsh winhttp show proxy 2>$null
Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -ErrorAction SilentlyContinue |
    Select-Object ProxyEnable, ProxyServer, AutoConfigURL | Format-List

Section '6. 真实握手测试（.NET / Schannel）'
Add-Type -TypeDefinition @'
using System;
using System.Net.Http;
public static class TlsProbe {
    public static string Get(string url) {
        try {
            using var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var r = h.GetAsync(url).GetAwaiter().GetResult();
            return "OK " + (int)r.StatusCode;
        } catch (Exception ex) {
            var s = ex.GetType().Name + ": " + ex.Message;
            for (var i = ex.InnerException; i != null; i = i.InnerException)
                s += " <- " + i.GetType().Name + ": " + i.Message;
            return s;
        }
    }
}
'@
foreach ($url in 'https://www.baidu.com', 'https://api.siliconflow.cn/v1/models') {
    $result = [TlsProbe]::Get($url)
    $color = if ($result -like 'OK*' -or $result -match 'Unauthorized|401') { 'Green' } else { 'Red' }
    Write-Host ("  {0}`n    {1}" -f $url, $result) -ForegroundColor $color
}

Section '7. 对照：用 OpenSSL 栈（Python）测同一地址'
$python = (Get-Command python -ErrorAction SilentlyContinue).Source
if ($python) {
    & $python -c @'
import urllib.request
for u in ("https://api.siliconflow.cn/v1/models",):
    try:
        r = urllib.request.urlopen(u, timeout=15)
        print("  python:", u, r.status)
    except Exception as e:
        print("  python:", u, type(e).__name__, str(e)[:80])
'@
} else {
    Write-Host '  没找到 python，跳过'
}

Write-Host "`n完成。若第 6 项失败而第 7 项成功，就是 Windows Schannel 的问题；两项都失败则是网络本身不通。" -ForegroundColor Cyan
