# 按窗口截图（类似录屏软件的「窗口捕获」）：找进程主窗口 → 取真实可见矩形 → 抓屏。
# 用法: .\shot-window.ps1 -Out ..\gui.png [-ProcessName SubtitleStudio] [-Delay 0]
param(
    [string]$Out = 'shot.png',
    [string]$ProcessName = 'SubtitleStudio',
    [int]$Id = 0,          # 指定 PID，避免旧实例抢先被截到
    [int]$Delay = 0
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not ('W32Shot' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W32Shot {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
}
"@
}

[void][W32Shot]::SetProcessDPIAware()

$proc = if ($Id -gt 0) {
    Get-Process -Id $Id -ErrorAction SilentlyContinue
} else {
    Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}
if (-not $proc -or $proc.MainWindowHandle -eq 0) { throw "找不到进程 $ProcessName 的主窗口" }

$hwnd = $proc.MainWindowHandle

if ($Delay -gt 0) { Start-Sleep -Milliseconds $Delay }

[void][W32Shot]::ShowWindow($hwnd, 9)   # SW_RESTORE
[void][W32Shot]::BringWindowToTop($hwnd)

# 前台窗口锁：先挂到当前前台线程上，再抢前台
# 注意：无人值守/锁屏时 GetForegroundWindow 可能返回 0，必须跳过附着
$fg = [W32Shot]::GetForegroundWindow()
$fgPid = 0
$fgThread = 0
$curThread = [W32Shot]::GetCurrentThreadId()
if ($fg -ne [IntPtr]::Zero) {
    $fgThread = [W32Shot]::GetWindowThreadProcessId($fg, [ref]$fgPid)
}
if ($fgThread -ne 0 -and $fgThread -ne $curThread) { [void][W32Shot]::AttachThreadInput($curThread, $fgThread, $true) }
[void][W32Shot]::SetForegroundWindow($hwnd)
if ($fgThread -ne 0 -and $fgThread -ne $curThread) { [void][W32Shot]::AttachThreadInput($curThread, $fgThread, $false) }
Start-Sleep -Milliseconds 500

$isForeground = ([W32Shot]::GetForegroundWindow() -eq $hwnd)

# DWMWA_EXTENDED_FRAME_BOUNDS = 9：不含阴影的真实窗口边界
$rect = New-Object W32Shot+RECT
$size = [System.Runtime.InteropServices.Marshal]::SizeOf($rect)
$hr = [W32Shot]::DwmGetWindowAttribute($hwnd, 9, [ref]$rect, $size)
if ($hr -ne 0 -or ($rect.Right - $rect.Left) -le 0) {
    $rect = New-Object W32Shot+RECT
    [void][W32Shot]::DwmGetWindowAttribute($hwnd, 9, [ref]$rect, $size)
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw "窗口尺寸异常：${width}x${height}" }

$bmp = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($bmp)

if ($isForeground) {
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
} else {
    # 抢不到前台就自己渲染窗口内容（PW_RENDERFULLCONTENT）
    $hdc = $g.GetHdc()
    [void][W32Shot]::PrintWindow($hwnd, $hdc, 2)
    $g.ReleaseHdc($hdc)
}

$g.Dispose()
# PowerShell 5.1（.NET Framework）没有 GetFullPath(path, basePath) 重载
$full = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path (Get-Location).Path $Out }
$full = [System.IO.Path]::GetFullPath($full)
$bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

"$full ($width x $height, foreground=$isForeground)"
