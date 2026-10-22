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

function Get-ConfirmWindow([string]$yesLabel, [int]$milliseconds = 5000) {
    $deadline = (Get-Date).AddMilliseconds($milliseconds)
    do {
        Update-Windows
        foreach ($w in $script:allWindows) {
            if ($w.Current.ProcessId -ne $script:procId -or $w.Current.IsOffscreen) { continue }
            $rect = $w.Current.BoundingRectangle
            if ($rect.Width -gt 1200) { continue }
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
        foreach ($c in @($container, [System.Windows.Automation.AutomationElement]::RootElement) + $script:allWindows) {
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
    $ruler = 22 * 1.25
    $stride = ($rect.Height - $ruler) / [Math]::Max(1, $rows)
    $gap = 6 * 1.25
    $rowH = [Math]::Max(20, $stride - $gap)
    $top = $rect.Top + $ruler + $index * $stride
    return @{
        Top     = $top
        Height  = $rowH
        ExportY = $top + $rowH * 0.27
        DeleteY = $top + $rowH * 0.73
        X       = $rect.Right - 18
    }
}

function Move-At([double]$x, [double]$y) {
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point([int]$x, [int]$y)
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
}
"@ -ErrorAction SilentlyContinue
    $h = [UiFg]::GetForegroundWindow()
    $pidOut = 0
    [UiFg]::GetWindowThreadProcessId($h, [ref]$pidOut) | Out-Null
    return ($pidOut -eq $script:procId)
}

function Click-At([double]$x, [double]$y) {
    if (-not (Test-Foreground)) {
        $script:skippedClicks++
        Write-Host "SKIP 前台不是本程序，不点击"
        return
    }

    [UiWin]::SetCursorPos([int]$x, [int]$y) | Out-Null
    Start-Sleep -Milliseconds 300
    [UiWin]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 70
    [UiWin]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
}

try {
    $win = Get-Window
    Start-Sleep -Seconds 18   # 等自动识别跑完
    $results = @()
    $results += "WINDOW $($win.Current.Name)"
    try { $win.SetFocus() } catch { }
    Start-Sleep -Milliseconds 400

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
    } else {
        $results += 'FAIL 清空没弹确认框'
    }

    # 4. 时间轴自绘图标：算出第 2 条轨的垃圾桶位置（4 轨时实测步距 93px）
    $pane = Find-ByName $win '音频与字幕轨道'
    if ($pane) {
        $r = $pane.Current.BoundingRectangle
        # 行高自适应后不能再写死步距：按 22 DIP 标尺 + 剩余高度均分算第 2 行
        $rowRows = 4
        $statusAll = (Get-Texts $win | Where-Object { $_ -like '共*条音轨*' } | Select-Object -First 1)
        if ($statusAll -match '共\s*\d+/(\d+)') { $rowRows = [int]$Matches[1] }
        $geo = Get-RowGeometry $r $rowRows 1
        "推算 垃圾桶 at ($($geo.X),$($geo.DeleteY)) pane=$($r.X),$($r.Y) $($r.Width)x$($r.Height) rows=$rowRows"
        Move-At ($r.Right - 80) ($geo.Top + $geo.Height / 2)
        Click-At $geo.X $geo.DeleteY
        $dlg2 = Get-ConfirmWindow '删除'
        if ($dlg2) {
            $results += "DIALOG2 $($dlg2.Current.Name)"
            $results += Save-Shot 'ui-delete-track.png'
            $results += Invoke-Button $dlg2 '删除'
            Start-Sleep -Seconds 1
            $results += (Get-Texts $win | Where-Object { $_ -like '*条音轨*' } | ForEach-Object { "AFTER-DELETE $_" })
            $results += Save-Shot 'ui-after-delete.png'
        } else {
            $results += 'FAIL 删除音轨没弹确认框'
        }
    }

    # 5. 重新识别：等状态里出现「识别完成」
    $results += Invoke-Button $win '重新识别'
    $done = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 700
        if ((Get-Texts $win) -match '识别完成') { $done = $true; break }
    }
    $results += if ($done) { 'OK   重新识别跑完' } else { 'FAIL 重新识别超时' }

    # 6. 打开文件：文件选择框弹出来再取消
    $results += Invoke-Button $win '打开文件'
    Start-Sleep -Seconds 2
    $results += Save-Shot 'ui-open-dialog.png'
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
    Start-Sleep -Milliseconds 800

    # 7. 悬浮到第 4 条轨再点导出音频（导出图标在垃圾桶上方约 23px）
    $pane = Find-ByName $win '音频与字幕轨道'
    $r = $pane.Current.BoundingRectangle
    $statusText = (Get-Texts $win | Where-Object { $_ -like '共*条音轨*' } | Select-Object -First 1)
    $rows = 4
    if ($statusText -match '共\s*\d+/(\d+)') { $rows = [int]$Matches[1] }
    $results += "ROWS $rows ($statusText)"
    # 导出图标是自绘的，UIA 看不到；沿纵向扫三个候选高度，命中就停
    $m4a = $null
    $geoLast = Get-RowGeometry $r $rows ($rows - 1)
    Move-At ($r.Right - 80) ($geoLast.Top + $geoLast.Height / 2)
    Start-Sleep -Milliseconds 600
    Click-At $geoLast.X $geoLast.ExportY
    Start-Sleep -Seconds 5
    $m4a = Get-ChildItem $exportDir -Filter *.m4a -ErrorAction SilentlyContinue
    if (-not $m4a) {
        Click-At $geoLast.X ($geoLast.ExportY + 6)
        Start-Sleep -Seconds 5
        $m4a = Get-ChildItem $exportDir -Filter *.m4a -ErrorAction SilentlyContinue
    }
    $results += if ($m4a) { "OK   导出音频 $($m4a[0].Name) $([math]::Round($m4a[0].Length/1KB))KB" } else { 'FAIL 导出音频没出文件' }

    # 8. 导出下拉里挑「透明视频 · WebM」跑一次
    $build = $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Avalonia.Controls.Viewbox')))
    if ($build) {
        $br = $build.Current.BoundingRectangle
        # SplitButton 右边那一半才是下拉箭头
        Click-At ($br.Right + 18) ($br.Y + $br.Height / 2)
        Start-Sleep -Milliseconds 1200
        $items = Get-Elements $win
        $results += ('MENU ' + (($items | ForEach-Object { $_.Current.Name } | Select-Object -Unique) -join ' / '))
        $results += Save-Shot 'ui-export-menu.png'
        $webmItem = $items | Where-Object { $_.Current.Name -like '*WebM*' } | Select-Object -First 1
        if ($webmItem) {
            $wr = $webmItem.Current.BoundingRectangle
            Click-At ($wr.X + $wr.Width / 2) ($wr.Y + $wr.Height / 2)
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
        $build2 = $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Avalonia.Controls.Viewbox')))
        $br2 = $build2.Current.BoundingRectangle
        Click-At ($br2.Right + 18) ($br2.Y + $br2.Height / 2)
        Start-Sleep -Milliseconds 1200
        $srt = (Get-Elements $win) | Where-Object { $_.Current.Name -like '*SRT*' } | Select-Object -First 1
        if ($srt) {
            $sr = $srt.Current.BoundingRectangle
            Click-At ($sr.X + $sr.Width / 2) ($sr.Y + $sr.Height / 2)
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
            $sr = $settingsItem.Current.BoundingRectangle
            Click-At ($sr.X + $sr.Width / 2) ($sr.Y + $sr.Height / 2)
            Start-Sleep -Seconds 2
            $opened = (Get-Texts $win | Where-Object { $_ -eq 'API Key' }).Count -gt 0
            if (-not $opened) {
                # 菜单可能被关掉了，重开一次再点
                $again = $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Avalonia.Controls.StackPanel')))
                if ($again) { $ar = $again.Current.BoundingRectangle; Click-At ($ar.X + $ar.Width / 2) ($ar.Y + $ar.Height / 2); Start-Sleep -Milliseconds 1200 }
                $settingsItem = (Get-Elements $win) | Where-Object { $_.Current.Name -eq '设置' } | Select-Object -First 1
            }
        }
        $results += if ($opened) { 'OK   打开设置（含 API Key 字段）' } else { 'FAIL 设置窗口内容没出现' }
        $results += Save-Shot 'ui-settings.png'
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Milliseconds 600
        $results += 'OK   关闭设置'
    } else {
        $results += 'SKIP 没找到「设置」菜单项'
    }

    $results | ForEach-Object { $_ }
}
finally {
    if (-not $KeepOpen -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}
