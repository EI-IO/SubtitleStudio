# 编译后跑一遍端到端冒烟测试（CLI 路径）。
# 用法： pwsh -File tools\smoke.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$repo = Split-Path -Parent $root
$dotnet = if (Test-Path (Join-Path $repo '.tools\dotnet\dotnet.exe')) { Join-Path $repo '.tools\dotnet\dotnet.exe' } else { 'dotnet' }
$cli = Join-Path $root 'src\SubtitleStudio.Cli\bin\Debug\net10.0\SubtitleStudio.Cli.dll'
$clip = Join-Path $repo '.tools\testclip.mp4'
$modelDir = Join-Path $root 'models'
$out = Join-Path $repo '.tools\out\smoke'

if (!(Test-Path $cli)) { throw "先编译：$cli 不存在" }
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host "`n=== 1) help"
& $dotnet $cli --help

Write-Host "`n=== 2) 端到端（抽取音轨 → 识别 → 断句 → 导出 srt/ass/vtt + 透明 webm）"
& $dotnet $cli --input $clip --out (Join-Path $out 'clip') --model (Join-Path $modelDir 'ggml-tiny.bin') `
    --format srt,ass,vtt --overlay webm --width 1280 --height 720 --fps 30 --waveform

Write-Host "`n=== 3) 产物"
Get-ChildItem $out | Select-Object Name, Length | Format-Table -AutoSize

$srt = Join-Path $out 'clip.srt'
if (Test-Path $srt) {
    Write-Host "`n=== 4) SRT 前几行"
    Get-Content $srt -TotalCount 12
}
