<#
.SYNOPSIS
    用 Windows UI Automation 把界面上的按钮逐个点一遍，顺手截图。
.DESCRIPTION
    比按像素点更稳：按钮按名字找，弹窗按标题找；只有自绘的
    时间轴图标（导出音频 / 删除音轨）才按坐标推算。
.EXAMPLE
    .\ui-test.ps1 -Media ..\test\test.mp4 -Shots ..\..\..\.tools\shots
#>
param(
    [string]$Media = '',
    [string]$Shots = "$PSScriptRoot\..\..\..\.tools\shots",
    [switch]$KeepOpen
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class UiWin {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
}
"@
[UiWin]::SetProcessDPIAware() | Out-Null

New-Item -ItemType Directory -Force -Path $Shots | Out-Null

$exe = Join-Path $PSScriptRoot '..\src\SubtitleStudio.App\bin\Debug\net10.0\SubtitleStudio.exe'
$exe = (Resolve-Path $exe).Path
$args = @()
if ($Media) { $args += (Resolve-Path $Media).Path }

$exportDir = Join-Path $Shots 'exports'
New-Item -ItemType Directory -Force -Path $exportDir | Out-Null
# 上一轮的产物会让「有没有导出文件」永远为真
Remove-Item (Join-Path $exportDir '*') -Force -ErrorAction SilentlyContinue
$env:SUBTITLESTUDIO_EXPORT_DIR = $exportDir
$proc = Start-Process -FilePath $exe -ArgumentList $args -WorkingDirectory (Split-Path $exe) -PassThru
$root = [System.Windows.Automation.AutomationElement]::RootElement
$script:procId = $proc.Id
$script:skippedClicks = 0
$script:allWindows = @()
$A = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function Update-Windows {
    $script:allWindows = @($root.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition))
}

function Get-Window([int]$milliseconds = 25000) {
    $deadline = (Get-Date).AddMilliseconds($milliseconds)
    do {
        $cond = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $proc.Id)
        $w = $root.FindFirst($TS::Children, $cond)
        if ($w) { return $w }
        Start-Sleep -Milliseconds 400
    } while ((Get-Date) -lt $deadline)
    throw '没等到主窗口'
}

function Get-Dialog($container, [string]$title, [int]$milliseconds = 5000) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $title)),
        (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))
    $deadline = (Get-Date).AddMilliseconds($milliseconds)
    do {
        $found = $container.FindFirst($TS::Descendants, $cond)
        if ($found) { return $found }

        # 弹出层（菜单 / 对话框）在独立窗口里，兜底到本进程所有顶层窗口
        Update-Windows
        foreach ($w in $script:allWindows) {
            if ($w.Current.ProcessId -ne $script:procId) { continue }
            if ($w.Current.IsOffscreen) { continue }
            $found = $w.FindFirst($TS::Descendants, $cond)
            if ($found) { return $found }
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Get-ConfirmWindow([string]$yesLabel, [int]$milliseconds = 20000) {
    # 实测确认框不是独立顶层窗口，而是渲染进主窗口的 UIA 子树，
    # 所以不能按「宽度≤1200 的小窗口」过滤；主窗口平时没有「取消」按钮，
    # 「取消+确认词同时存在」足够判定确认框已弹出。
    $deadline = (Get-Date).AddMilliseconds($milliseconds)
    do {
        Update-Windows
        foreach ($w in $script:allWindows) {
            if ($w.Current.ProcessId -ne $script:procId -or $w.Current.IsOffscreen) { continue }
            $hasCancel = $w.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, '取消')))
            $hasYes = $w.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $yesLabel)))
            if ($hasCancel -and $hasYes) { return $w }
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $null
}

function Find-ByName($container, [string]$name, $type = $null) {
    $conds = @(New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name))
    if ($type) { $conds += New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $type) }
    $cond = if ($conds.Count -eq 1) { $conds[0] } else { New-Object System.Windows.Automation.AndCondition($conds) }
    $hit = $container.FindFirst($TS::Descendants, $cond)
    if ($hit) { return $hit }

    Update-Windows
    foreach ($w in $script:allWindows) {
        if ($w.Current.ProcessId -ne $script:procId -or $w.Current.IsOffscreen) { continue }
        $hit = $w.FindFirst($TS::Descendants, $cond)
        if ($hit) { return $hit }
    }
    return $null
}

function Invoke-Button($container, [string]$name) {
    $btn = Find-ByName $container $name ([System.Windows.Automation.ControlType]::Button)
    if (-not $btn) { return "MISS 按钮:$name" }
    $pattern = $null
    if ($btn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        $pattern.Invoke()
        Start-Sleep -Milliseconds 500
        return "OK   按钮:$name"
    }
    return "SKIP 按钮:$name 不支持 Invoke"
}

function Get-Elements($container, [int]$milliseconds = 8000) {
    # 菜单是弹出层，出现前要多等两轮
    $deadline = (Get-Date).AddMilliseconds($milliseconds)
    do {
        $hits = @()
        Update-Windows
        # 只扫本进程的窗口：桌面上别的程序也有菜单栏，混进来会干扰名字匹配
        $ours = @($script:allWindows | Where-Object { $_.Current.ProcessId -eq $script:procId -and -not $_.Current.IsOffscreen })
        foreach ($c in @($container) + $ours) {
            try {
                $all = $c.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
                $hits += @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem -and $_.Current.Name -notmatch '^System' })
            } catch {
                # 弹层在这轮被关掉了，跳过这个容器
            }
        }
        if ($hits.Count -gt 0) { return $hits }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return @()
}

function Get-Buttons($container) {
    try {
        $all = $container.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        return @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button })
    } catch {
        # 窗口刚好被关掉时会抛 ElementNotAvailable，当作没找到
        return @()
    }
}

function Invoke-Element($element) {
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        $pattern.Invoke()
        return $true
    }
    return $false
}

function Get-Texts($container) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    try {
        return @($container.FindAll($TS::Descendants, $cond) | ForEach-Object { $_.Current.Name })
    } catch {
        return @()
    }
}

function Get-RowGeometry($rect, [int]$rows, [int]$index) {
    # 完全照抄 TimelineControl 的布局：ruler 22、轨间隙 6、行高均分可用高度（最小 46），
    # 波形 30..96、字幕条 22；导出/删除按钮 20x20 在「波形+字幕条」里垂直居中。
    # $rect 必须是时间轴控件（UIA 里的 Custom 元素）的矩形，不是整个 pane。
    $ruler = 22
    $gap = 6
    $cue = 22
    $available = $rect.Height - $ruler - $gap
    $stride = [Math]::Max(46, $available / [Math]::Max(1, $rows))
    $offset = [Math]::Max(0, ($available - $stride * $rows) / 2)
    $top = $rect.Top + $ruler + $offset + $index * $stride
    $waveH = [Math]::Min(96, [Math]::Max(30, $stride - $cue - $gap))
    $stack = $top + [Math]::Max(0, ($waveH + $cue - 44) / 2)
    return @{
        Top     = $top
        Height  = $stride
        ExportY = $stack + 10
        DeleteY = $stack + 34
        X       = $rect.Right - 17
    }
}

function Get-TimelineRect($pane) {
    # 时间轴是自绘控件，UIA 里表现为 pane 下第一个 Custom 元素
    $cond = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Custom)
    $custom = $pane.FindFirst($TS::Descendants, $cond)
    if ($custom) { return $custom.Current.BoundingRectangle }
    return $pane.Current.BoundingRectangle
}

function Get-TrackRows($win) {
    # 状态栏「（共 1/4 条音轨、2 条字幕）」里的第二个数字才是总轨数；
    # 注意状态文字以「音轨 N ·」开头，不能 -like '共*'
    $statusAll = (Get-Texts $win) | Where-Object { $_ -match '共\s*(\d+)/(\d+)\s*条音轨' } | Select-Object -First 1
    if ($statusAll -match '共\s*(\d+)/(\d+)\s*条音轨') { return [int]$Matches[2] }
    return 4
}

function Move-At([double]$x, [double]$y) {
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]$x, [int]$y)
}

function Find-ButtonIcons($win, $timelineRect) {
    # 悬浮后右侧会画出「导出（箭头）」「删除（红垃圾桶）」两个自绘图标。
    # 截图实测：按钮列在窗口右边往内约 30px（时间轴控件比窗口窄），
    # 且播放头红线会横穿整条时间轴，所以只认「每行红色像素 >= 8」的垃圾桶边框行。
    $wr = $win.Current.BoundingRectangle
    $x0 = [int]($wr.Right - 60)
    $y0 = [int]([Math]::Max($wr.Y + 60, $timelineRect.Top))
    $w = 50
    $h = [int]($timelineRect.Bottom - 6 - $y0)
    if ($w -le 0 -or $h -le 0) { return $null }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x0, $y0, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $rows = @()
    $xs = @()
    for ($y = 0; $y -lt $h; $y++) {
        $rc = 0; $rowX = @()
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bmp.GetPixel($x, $y)
            if ($c.R -gt 120 -and $c.G -lt 110 -and $c.B -lt 110 -and ($c.R - $c.G) -gt 40) {
                $rc++; $rowX += $x
            }
        }
        # 垃圾桶边框一行有十几二十个红点；播放头那根 1.5px 线到不了这个量
        if ($rc -ge 8) { $rows += $y; $xs += $rowX }
    }
    $bmp.Dispose()
    if ($rows.Count -lt 2) { return $null }
    $top = $rows[0]; $bottom = $rows[-1]
    $height = $bottom - $top
    Write-Host "SCAN y0=$y0 rows=$($rows.Count) span=$top..$bottom"
    # 垃圾桶连边框高 20px，带抗锯齿最多 30px；超出说明扫到别的东西了
    if ($height -lt 10 -or $height -gt 40) { return $null }
    $deleteY = $y0 + ($top + $bottom) / 2
    $xMin = ($xs | Measure-Object -Minimum).Minimum
    $xMax = ($xs | Measure-Object -Maximum).Maximum
    $x = $x0 + ($xMin + $xMax) / 2
    # 两个按钮中心固定差 24（按钮 20 + 间距 4）
    return @{ ExportY = $deleteY - 24; DeleteY = $deleteY; X = $x }
}

function Save-Shot([string]$name) {
    $bmp = New-Object System.Drawing.Bitmap 1920, 1080
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $path = Join-Path $Shots $name
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    return "SHOT $path"
}

function Test-Foreground {
    Add-Type @"
using System; using System.Runtime.InteropServices;
public class UiFg {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
}
"@ -ErrorAction SilentlyContinue
    $h = [UiFg]::GetForegroundWindow()
    $pidOut = 0
    [UiFg]::GetWindowThreadProcessId($h, [ref]$pidOut) | Out-Null
    return ($pidOut -eq $script:procId)
}

function Force-Foreground {
    # 前台可能被别的程序（Qoder 弹窗等）抢走，单纯 SetForegroundWindow 会被系统拒绝。
    # 先按一下 Alt 解除前台锁，再 AttachThreadInput 到当前前台线程后置前。
    $h = $proc.MainWindowHandle
    if ($h -eq [IntPtr]::Zero) { return }
    [UiFg]::ShowWindow($h, 9) | Out-Null          # SW_RESTORE
    [UiFg]::keybd_event(0x12, 0, 0, [IntPtr]::Zero)   # Alt down
    [UiFg]::keybd_event(0x12, 0, 2, [IntPtr]::Zero)   # Alt up
    $fg = [UiFg]::GetForegroundWindow()
    $fgPid = 0; $fgThread = 0
    if ($fg -ne [IntPtr]::Zero) {
        $fgThread = [UiFg]::GetWindowThreadProcessId($fg, [ref]$fgPid)
    }
    $cur = [UiFg]::GetCurrentThreadId()
    if ($fgThread -ne 0) { [UiFg]::AttachThreadInput($cur, $fgThread, $true) | Out-Null }
    [UiFg]::SetForegroundWindow($h) | Out-Null
    [UiFg]::BringWindowToTop($h) | Out-Null
    if ($fgThread -ne 0) { [UiFg]::AttachThreadInput($cur, $fgThread, $false) | Out-Null }
    Start-Sleep -Milliseconds 350
}

function Focus-App {
    # 截图/悬浮之前先把主窗口抢回前台，否则鼠标停在别的窗口上，
    # 时间轴收不到 PointerMoved，自绘的导出/删除图标根本不会出现
    if (-not (Test-Foreground)) { Force-Foreground }
}

function Click-At([double]$x, [double]$y) {
    if (-not (Test-Foreground)) {
        # UIA 调用弹窗后前台可能跑到终端上，先把主窗口抢回前台再点
        Force-Foreground
    }
    if (-not (Test-Foreground)) {
        $script:skippedClicks++
        Write-Host "SKIP 前台不是本程序，不点击"
        return $false
    }

    [UiWin]::SetCursorPos([int]$x, [int]$y) | Out-Null
    Start-Sleep -Milliseconds 300
    [UiWin]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 70
    [UiWin]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    return $true
}

function Click-Element($element) {
    # 菜单项 / 按钮优先走 UIA Invoke：不依赖前台窗口，鼠标被别的程序抢走也能点
    if (-not $element) { return }
    if (Invoke-Element $element) {
        Start-Sleep -Milliseconds 500
        return
    }
    $r = $element.Current.BoundingRectangle
    Click-At ($r.X + $r.Width / 2) ($r.Y + $r.Height / 2) | Out-Null
}

try {
    $win = Get-Window
    # 导入文件后不再自动识别：先等状态栏出现「点「开始识别」」（说明媒体加载完了，
    # 按钮本身一直叫「开始识别」，不能拿按钮名当加载完成的依据），
    # 再点「开始识别」，然后轮询状态栏直到首轮识别完成（最多 120 秒）。
    # 注意不能匹配「识别完成」——预览占位符「识别完成后这里会显示字幕效果」也含这四个字，
    # 状态栏真正完成时会长这样：「音轨 4 · 2 条字幕 · 语言 en（共 1/4 条音轨、2 条字幕）」
    $loaded = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Seconds 1
        if ((Get-Texts $win) -match '点「开始识别」') { $loaded = $true; break }
    }
    if (-not $loaded) { Write-Host 'WARN 等媒体加载超时，继续跑' }
    $results = @()
    $results += "WINDOW $($win.Current.Name)"
    try { $win.SetFocus() } catch { }
    Start-Sleep -Milliseconds 400

    # 0. 首轮识别：点「开始识别」，完成后按钮应变成「重新识别」
    $results += Invoke-Button $win '开始识别'
    $ready = $false
    for ($i = 0; $i -lt 120; $i++) {
        Start-Sleep -Seconds 1
        if ((Get-Texts $win) -match '共.*条音轨') { $ready = $true; break }
    }
    if (-not $ready) { Write-Host 'WARN 等识别完成超时，继续跑' }
    $rerun = Find-ByName $win '重新识别' ([System.Windows.Automation.ControlType]::Button)
    $results += if ($rerun) { 'OK   识别完成后按钮变为「重新识别」' } else { 'FAIL 识别完成后按钮没变成「重新识别」' }

    # 1. 播放 / 暂停
    $results += Invoke-Button $win '播放'
    Start-Sleep -Seconds 2
    $results += (Get-Texts $win | Where-Object { $_ -like '播放头*' } | ForEach-Object { "PLAYHEAD $_" })
    $results += Save-Shot 'ui-playing.png'
    $results += Invoke-Button $win '暂停'

    # 2. 缩放
    $results += Invoke-Button $win '−'
    $results += Invoke-Button $win '+'
    $results += Invoke-Button $win '+'
    $results += Save-Shot 'ui-zoom.png'

    # 3. 清空 → 二次确认 → 取消
    $results += Invoke-Button $win '清空'
        $dlg = Get-ConfirmWindow '清空'
    if ($dlg) {
        $results += "DIALOG $($dlg.Current.Name)"
        $results += Save-Shot 'ui-clear-dialog.png'
        $results += Invoke-Button $dlg '取消'
        # 确认框有淡出动画，立刻点时间轴会点到还在的遮罩上
        Start-Sleep -Seconds 1
    } else {
        $results += 'FAIL 清空没弹确认框'
    }

    # 4. 时间轴自绘图标：算第 2 条轨的垃圾桶位置（几何见 Get-RowGeometry 注释）
    $pane = Find-ByName $win '音频与字幕轨道'
    if ($pane) {
        $r = Get-TimelineRect $pane
        $rowRows = Get-TrackRows $win
        $geo = Get-RowGeometry $r $rowRows 1
        "推算 垃圾桶 at ($($geo.X),$($geo.DeleteY)) pane=$($r.X),$($r.Y) $($r.Width)x$($r.Height) rows=$rowRows"
        Focus-App
        Move-At ($r.Right - 80) ($geo.Top + $geo.Height / 2)
        Start-Sleep -Milliseconds 700
        # 自绘图标直接按颜色定位，比推算像素更稳
        $icons = Find-ButtonIcons $win $r
        $delX = $geo.X; $delY = $geo.DeleteY
        if ($icons) { $delX = $icons.X; $delY = $icons.DeleteY; "ICONS 删除 at ($delX,$delY)" }
        Click-At $delX $delY
        $dlg2 = Get-ConfirmWindow '删除' 3000
        if (-not $dlg2) {
            # 刚关掉别的弹层时第一下点击可能只激活窗口，再点一次
            Click-At $delX $delY
            $dlg2 = Get-ConfirmWindow '删除' 8000
        }
        if ($dlg2) {
            $results += "DIALOG2 $($dlg2.Current.Name)"
            $results += Save-Shot 'ui-delete-track.png'
            $results += Invoke-Button $dlg2 '删除'
            Start-Sleep -Seconds 1
            $results += (Get-Texts $win | Where-Object { $_ -like '*条音轨*' } | ForEach-Object { "AFTER-DELETE $_" })
            $results += Save-Shot 'ui-after-delete.png'
        } else {
            $results += Save-Shot 'ui-delete-fail.png'
            $results += ('DEBUG-BTNS ' + ((Get-Buttons $win | ForEach-Object { $_.Current.Name }) -join '/'))
            $results += ('DEBUG-TEXTS ' + ((Get-Texts $win | Where-Object { $_ -match '删除|取消' }) -join '/'))
            $results += 'FAIL 删除音轨没弹确认框'
        }
    }

    # 5. 重新识别：等状态里出现「共 N/M 条音轨」（不能匹配「识别完成」，预览占位符也含那四个字）
    $results += Invoke-Button $win '重新识别'
    $done = $false
    for ($i = 0; $i -lt 150; $i++) {
        Start-Sleep -Milliseconds 700
        if ((Get-Texts $win) -match '共.*条音轨') { $done = $true; break }
    }
    $results += if ($done) { 'OK   重新识别跑完' } else { 'FAIL 重新识别超时' }

    # 6. 打开文件：文件选择框弹出来再取消
    $results += Invoke-Button $win '打开文件'
    Start-Sleep -Seconds 2
    $results += Save-Shot 'ui-open-dialog.png'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 800

    # 7. 悬浮到最后一条轨再点导出音频（导出图标在垃圾桶上方约 24px）
    $pane = Find-ByName $win '音频与字幕轨道'
    $r = Get-TimelineRect $pane
    $rows = Get-TrackRows $win
    $results += "ROWS $rows CONTROL $([int]$r.Width)x$([int]$r.Height)"
    # 导出图标是自绘的，UIA 看不到；按控件自己的行布局公式算坐标
    $m4a = $null
    $geoLast = Get-RowGeometry $r $rows ($rows - 1)
    Focus-App
    Move-At ($r.Right - 80) ($geoLast.Top + $geoLast.Height / 2)
    Start-Sleep -Milliseconds 700
    $icons = Find-ButtonIcons $win $r
    $expX = $geoLast.X; $expY = $geoLast.ExportY
    if ($icons) { $expX = $icons.X; $expY = $icons.ExportY; "ICONS 导出 at ($expX,$expY)" }
    Click-At $expX $expY
    Start-Sleep -Seconds 5
    $m4a = Get-ChildItem $exportDir -Filter *.m4a -ErrorAction SilentlyContinue
    $results += if ($m4a) { "OK   导出音频 $($m4a[0].Name) $([math]::Round($m4a[0].Length/1KB))KB" } else { 'FAIL 导出音频没出文件' }

    # 8. 导出下拉里挑「透明视频 · WebM」跑一次
    # SplitButton 在 UIA 里拆成 PART_PrimaryButton / PART_SecondaryButton 两个按钮，
    # 按 AutomationId 找右边那个箭头（按 Viewbox 找会撞上别处的图标）
    $arrow = $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'PART_SecondaryButton')))
    if ($arrow) {
        Click-Element $arrow
        Start-Sleep -Milliseconds 1200
        $items = Get-Elements $win
        $results += ('MENU ' + (($items | ForEach-Object { $_.Current.Name } | Select-Object -Unique) -join ' / '))
        $results += Save-Shot 'ui-export-menu.png'
        $webmItem = $items | Where-Object { $_.Current.Name -like '*WebM*' } | Select-Object -First 1
        if ($webmItem) {
            Click-Element $webmItem
            # 等文件写完：连续两次大小不变且 > 1KB
            $prev = -1; $stable = 0
            for ($i = 0; $i -lt 240; $i++) {
                Start-Sleep -Milliseconds 700
                $f = Get-ChildItem $exportDir -Filter *.webm -ErrorAction SilentlyContinue | Select-Object -First 1
                if (-not $f) { continue }
                if ($f.Length -eq $prev -and $f.Length -gt 1024) { $stable++ } else { $stable = 0 }
                $prev = $f.Length
                if ($stable -ge 2) { break }
            }
            $webm = Get-ChildItem $exportDir -Filter *.webm -ErrorAction SilentlyContinue
            $results += if ($webm) { "OK   导出视频 $($webm[0].Name) $([math]::Round($webm[0].Length/1KB))KB" } else { 'FAIL 导出视频没出文件' }
            $results += Save-Shot 'ui-export-done.png'
        }

        # 8b. 再挑一次「字幕 · SRT」
        $arrow2 = $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::AutomationIdProperty, 'PART_SecondaryButton')))
        Click-Element $arrow2
        Start-Sleep -Milliseconds 1200
        $srt = (Get-Elements $win) | Where-Object { $_.Current.Name -like '*SRT*' } | Select-Object -First 1
        if ($srt) {
            Click-Element $srt
            for ($i = 0; $i -lt 30; $i++) {
                Start-Sleep -Milliseconds 500
                if (Get-ChildItem $exportDir -Filter *.srt -ErrorAction SilentlyContinue) { break }
            }
            $sub = Get-ChildItem $exportDir -Filter *.srt -ErrorAction SilentlyContinue
            $results += if ($sub) { "OK   导出字幕 $($sub[0].Name) $($sub[0].Length)B" } else { 'FAIL 导出字幕没出文件' }
        } else {
            $results += 'FAIL 下拉里没找到 SRT'
        }
    } else {
        $results += 'FAIL 没找到导出按钮'
    }

    # 9. 模型菜单 + 设置
    $results += Invoke-Button $win 'Avalonia.Controls.StackPanel'
    Start-Sleep -Milliseconds 900
    $results += Save-Shot 'ui-model-menu.png'
    $settingsItem = (Get-Elements $win) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem -and $_.Current.Name -eq '设置' } | Select-Object -First 1
    if ($settingsItem) {
        $opened = $false
        for ($i = 0; $i -lt 3 -and -not $opened; $i++) {
            Click-Element $settingsItem
            Start-Sleep -Seconds 2
            # 设置窗口改成提供商列表后，默认显示的是当前提供商的表单（本地模型没有 API Key 字段），
            # 所以改成找底部的「＋ 添加模型提供商」按钮来判断窗口是否打开
            $opened = $null -ne (Find-ByName $win '＋ 添加模型提供商' ([System.Windows.Automation.ControlType]::Button))
            if (-not $opened) {
                # 菜单可能被关掉了，重开一次再点
                $again = $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Avalonia.Controls.StackPanel')))
                if ($again) { Click-Element $again; Start-Sleep -Milliseconds 1200 }
                $settingsItem = (Get-Elements $win) | Where-Object { $_.Current.Name -eq '设置' } | Select-Object -First 1
            }
        }
        $results += if ($opened) { 'OK   打开设置（提供商列表）' } else { 'FAIL 设置窗口内容没出现' }
        $results += Save-Shot 'ui-settings.png'
        # 顺手验证一下「添加模型提供商」：点完后自定义表单应出现（名称/接口地址字段）
        if ($opened) {
            $results += Invoke-Button $win '＋ 添加模型提供商'
            Start-Sleep -Milliseconds 800
            $hasCustomForm = (Get-Texts $win | Where-Object { $_ -like '名称*' -or $_ -like '接口地址*' }).Count -gt 0
            $results += if ($hasCustomForm) { 'OK   添加模型提供商弹出自定义表单' } else { 'FAIL 添加模型提供商没反应' }
            $results += Save-Shot 'ui-settings-custom.png'

            # 设置窗口现在是「写了就不管」（关窗自动保存），所以测试加的提供商要当场删掉，
            # 不然会写进真实的 settings.json
            $results += Invoke-Button $win '删除'
            $delDlg = Get-ConfirmWindow '删除'
            if ($delDlg) {
                $results += Invoke-Button $delDlg '删除'
                $results += 'OK   自定义提供商删除（含确认框）'
            } else {
                $results += 'FAIL 删除提供商没弹确认框'
            }
        }
        # 关设置走「完成」按钮（没有「取消」了，关窗即保存）
        $results += Invoke-Button $win '完成'
        Start-Sleep -Milliseconds 800
        $closed = $null -eq (Find-ByName $win '＋ 添加模型提供商' ([System.Windows.Automation.ControlType]::Button))
        $results += if ($closed) { 'OK   关闭设置（自动保存）' } else { 'FAIL 设置窗口没关上' }
    } else {
        $results += 'SKIP 没找到「设置」菜单项'
    }

    $results | ForEach-Object { $_ }
}
finally {
    if (-not $KeepOpen -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}
