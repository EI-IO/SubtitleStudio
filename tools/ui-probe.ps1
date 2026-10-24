param([string]$Media = 'C:\Users\Q10y\Documents\project\SubtitleStudio\test\test.mp4')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

$dir = 'C:\Users\Q10y\Documents\project\SubtitleStudio\src\SubtitleStudio.App\bin\Debug\net10.0'
$proc = Start-Process -FilePath "$dir\SubtitleStudio.exe" -ArgumentList $Media -WorkingDirectory $dir -PassThru
Start-Sleep -Seconds 22

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$win = $root.FindFirst($TS::Children, $cond)
$all = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
"total=$($all.Count)"

foreach ($e in $all) {
    $c = $e.Current
    $type = $c.ControlType.ProgrammaticName
    if ($type -match 'Button|MenuItem|ComboBox|RadioButton|CheckBox' -or $c.Name -match '导出|模型|播放|设置') {
        $r = $c.BoundingRectangle
        '{0,-50} | {1,-26} | {2,-34} | {3},{4} {5}x{6}' -f $c.Name, $type, $c.ClassName, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height
    }
}

# 点开模型菜单，再看弹出层里有什么
$stack = $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Avalonia.Controls.StackPanel')))
if ($stack) {
    $pattern = $null
    if ($stack.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke() }
    Start-Sleep -Milliseconds 1200
    '--- after menu open ---'
    $all2 = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all2) {
        $c = $e.Current
        if ($c.ControlType.ProgrammaticName -match 'MenuItem|Popup|Menu' -or $c.Name -match '本地模型|硅基流动|天翼星辰|设置') {
            $r = $c.BoundingRectangle
            '{0,-40} | {1,-24} | {2},{3} {4}x{5}' -f $c.Name, $c.ControlType.ProgrammaticName, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height
        }
    }
}

if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
