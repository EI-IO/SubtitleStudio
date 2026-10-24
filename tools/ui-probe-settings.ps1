param([string]$Media = 'C:\Users\Q10y\Documents\project\SubtitleStudio\test\test.mp4')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

function ClickAt([double]$x, [double]$y) {
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point ([int]$x), ([int]$y)
    Start-Sleep -Milliseconds 300
    Add-Type -MemberDefinition '[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);' -Name M -Namespace W -PassThru | Out-Null
    [W.M]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 70
    [W.M]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
}

function Shot([string]$name) {
    $bmp = New-Object System.Drawing.Bitmap 1920, 1080
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save($name, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

$dir = 'C:\Users\Q10y\Documents\project\SubtitleStudio\src\SubtitleStudio.App\bin\Debug\net10.0'
$proc = Start-Process -FilePath "$dir\SubtitleStudio.exe" -ArgumentList $Media -WorkingDirectory $dir -PassThru
Start-Sleep -Seconds 20
$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = $root.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)))

$stack = $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Avalonia.Controls.StackPanel')))
$r = $stack.Current.BoundingRectangle
ClickAt ($r.X + $r.Width / 2) ($r.Y + $r.Height / 2)
Start-Sleep -Milliseconds 1200

$all = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$items = @($all | Where-Object { $_.Current.ControlType -eq $CT::MenuItem -and $_.Current.Name -notmatch '^System' })
foreach ($i in $items) { "ITEM $($i.Current.Name) @ $([int]$i.Current.BoundingRectangle.X),$([int]$i.Current.BoundingRectangle.Y)" }

$set = $items | Where-Object { $_.Current.Name -eq '设置' } | Select-Object -First 1
if ($set) {
    $sr = $set.Current.BoundingRectangle
    ClickAt ($sr.X + $sr.Width / 2) ($sr.Y + $sr.Height / 2)
    Start-Sleep -Seconds 3
    Shot 'C:\Users\Q10y\Documents\project\.tools\shots\ui-settings2.png'
    $wins = $root.FindAll($TS::Children, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)))
    "WINDOWS after: $($wins.Count)"
    foreach ($w in $wins) { "  [$($w.Current.ClassName)] '$($w.Current.Name)'" }
    $tabs = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $names = @($tabs | Where-Object { $_.Current.Name -match '硅基流动|天翼星辰|本地模型|API Key' } | ForEach-Object { $_.Current.Name } | Select-Object -Unique)
    "SETTINGS-CONTROLS: $($names -join ' | ')"
}

if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
