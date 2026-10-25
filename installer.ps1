# 用 Inno Setup 把发布目录打成安装包
# 先运行 .\publish.ps1 生成发布目录；再来这里：
#   .\installer.ps1                 # 完整版：dist\SubtitleStudio
#   .\installer.ps1 -Mode slim      # slim 版：dist\SubtitleStudio-slim
#   .\installer.ps1 -IsccPath "D:\Inno Setup 6\ISCC.exe"
#
# 依赖 Inno Setup 6.5+：https://jrsoftware.org/isdl.php

param(
    [ValidateSet('full', 'slim')] [string]$Mode = 'full',
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# 版本号只维护 src\Directory.Build.props 一处，安装包文件名跟着走
$props = Get-Content (Join-Path $root 'src\Directory.Build.props') -Raw
if ($props -notmatch '<Version>([^<]+)</Version>') {
    throw '没在 src\Directory.Build.props 里找到 <Version>'
}
$version = $Matches[1].Trim()

$isSlim = ($Mode -eq 'slim')
$sourceRel = if ($isSlim) { 'dist\SubtitleStudio-slim' } else { 'dist\SubtitleStudio' }
$source = Join-Path $root $sourceRel
if (-not (Test-Path (Join-Path $source 'SubtitleStudio.exe'))) {
    $publishHint = if ($isSlim) { '.\publish.ps1 -Mode slim' } else { '.\publish.ps1' }
    throw "没找到 $source\SubtitleStudio.exe，先运行 $publishHint"
}

# 安装包文件名跟着版本和模式走，两种包并存、互不覆盖
$outputBase = if ($isSlim) {
    "SubtitleStudio-Slim-Setup-$version"
} else {
    "SubtitleStudio-Setup-$version"
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) {
        $IsccPath = $cmd.Source
    } else {
        $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
}
if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path $IsccPath)) {
    throw '没找到 ISCC.exe，请安装 Inno Setup 6：https://jrsoftware.org/isdl.php'
}

Write-Host "ISCC: $IsccPath"
Write-Host "版本: $version"
Write-Host "模式: $Mode ($sourceRel)"
$defines = @("/DAppVersion=$version", "/DSourceDir=$sourceRel", "/DOutputBase=$outputBase")
if ($isSlim) { $defines += '/DIsSlim' }

Push-Location $root
try {
    & $IsccPath @defines 'installer.iss'
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败（exit $LASTEXITCODE）" }
} finally {
    Pop-Location
}

$setup = Join-Path $root ("dist\{0}.exe" -f $outputBase)
if (-not (Test-Path $setup)) { throw "没生成安装包：$setup" }
Write-Host ("`nOK -> {0}  ({1:0.0} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))
