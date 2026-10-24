# 打包成可直接双击运行的目录：dist\SubtitleStudio\
#   full（默认）：自包含，目标机不需要装 .NET
#   -Mode slim：框架依赖，需要目标机装 .NET 10 Desktop Runtime，体积小 80MB 左右
#   加 -Zip 额外压一个 zip；加 -NoModel 不打包模型（首次运行在「设置 → 本地模型」里下载）
#
# 运行前需要把 ffmpeg 放到 tools\ffmpeg\bin\（ffmpeg.exe + ffprobe.exe + 依赖 dll）。

param(
    [ValidateSet('full', 'slim')] [string]$Mode = 'full',
    [switch]$Zip,
    [switch]$NoModel
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $root
$tools = Join-Path $repo '.tools'

$dotnet = 'dotnet'
$restoreSource = @()

if (Test-Path $tools) {
    $localSdk = Join-Path $tools 'dotnet'
    $localFeed = Join-Path $tools 'nuget-local'

    $tempRoot = Join-Path $tools 'tmp'
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    $env:TMP = (Resolve-Path $tempRoot).Path
    $env:TEMP = $env:TMP

    $env:NUGET_PACKAGES = Join-Path $tools 'nuget-packages'
    foreach ($pair in @(@('DOTNET_CLI_HOME', 'clihome'), @('APPDATA', 'appdata\Roaming'), @('LOCALAPPDATA', 'appdata\Local'))) {
        $dir = Join-Path $tools $pair[1]
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Set-Item -Path ('env:' + $pair[0]) -Value $dir
    }

    if (Test-Path (Join-Path $localSdk 'dotnet.exe')) {
        $env:DOTNET_ROOT = $localSdk
        $env:PATH = "$localSdk;$env:PATH"
        $dotnet = Join-Path $localSdk 'dotnet.exe'
    }

    if (Test-Path $localFeed) {
        $restoreSource = @('--source', $localFeed)
    }
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$app = Join-Path $root 'src\SubtitleStudio.App\SubtitleStudio.App.csproj'
$cli = Join-Path $root 'src\SubtitleStudio.Cli\SubtitleStudio.Cli.csproj'
$dist = Join-Path $root ('dist\SubtitleStudio' + $(if ($Mode -eq 'slim') { '-slim' } else { '' }))

# Compress-Archive 在 Windows PowerShell 5.1 下会写成反斜杠条目名，
# Linux/macOS 解压会得到带 \ 的文件名；这里统一手写正斜杠。
function New-Zip([string]$sourceDir, [string]$zipPath) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    $base = (Resolve-Path $sourceDir).Path.TrimEnd('\')
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -LiteralPath $base -Recurse -File -Force | ForEach-Object {
            $rel = $_.FullName.Substring($base.Length + 1).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $_.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally {
        $zip.Dispose()
    }
}

if (Test-Path $dist) {
    Remove-Item -Recurse -Force $dist
}

$selfContained = ($Mode -eq 'full')
$ridArgs = @('-r', 'win-x64')

foreach ($proj in @($app, $cli)) {
    Write-Host "`n=== publish $(Split-Path -Leaf $proj)  ($Mode)"
    & $dotnet restore $proj @restoreSource @ridArgs
    if ($LASTEXITCODE -ne 0) { throw "restore failed: $proj" }

    & $dotnet publish $proj --no-restore -c Release @ridArgs --self-contained $selfContained `
        -p:DebugType=none -p:IncludeNativeLibrariesForSelfExtract=false -o $dist
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $proj" }
}

# 运行时资源：ffmpeg 与模型，让装好的目录开箱即用
$ffmpegBin = Join-Path $root 'tools\ffmpeg\bin'
if (Test-Path $ffmpegBin) {
    $toolsDst = Join-Path $dist 'tools\ffmpeg\bin'
    New-Item -ItemType Directory -Force -Path $toolsDst | Out-Null
    Copy-Item (Join-Path $ffmpegBin '*') $toolsDst -Force
} else {
    Write-Warning "没找到 tools\ffmpeg\bin，发布目录里不带 ffmpeg，运行前需要自己放进去。"
}

$modelsSrc = Join-Path $root 'models'
if (-not $NoModel -and (Test-Path $modelsSrc)) {
    $modelsDst = Join-Path $dist 'models'
    New-Item -ItemType Directory -Force -Path $modelsDst | Out-Null
    Get-ChildItem $modelsSrc -Filter 'ggml-*.bin' -File -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item $_.FullName $modelsDst -Force }
}

# README 只随源码包分发，运行包与安装程序不带

# 运行期产生的用户数据不能跟着发布：安全软件拦用户目录时程序会退到这里存设置
$runtimeConfig = Join-Path $dist 'config'
if (Test-Path $runtimeConfig) {
    Write-Host "清理发布目录里的用户配置：$runtimeConfig"
    Remove-Item $runtimeConfig -Recurse -Force
}
Get-ChildItem $dist -File -Filter '*.log' -ErrorAction SilentlyContinue | Remove-Item -Force

# 只留 Windows 原生库：NuGet 包里带了 linux/macos/wasm 各平台产物，删掉能省 100MB 左右
$runtimes = Join-Path $dist 'runtimes'
if (Test-Path $runtimes) {
    Get-ChildItem $runtimes -Directory |
        Where-Object { $_.Name -ne 'win-x64' } |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force }
}

# 调试期才用得上的东西，发布版删掉；
# 另外 win-x64 用不到其他平台的 Avalonia 后端（Linux/macOS）和设计器远程协议。
# Avalonia.Metal.dll 必须保留：Avalonia.Skia.dll 在 Windows 上也引用它，
# 删了窗口只剩标题栏、客户区不再重绘，看起来像“窗口变透明”。
foreach ($junk in @('createdump.exe', 'Microsoft.DiaSymReader.Native.amd64.dll', '*.pdb', '*.lib', '*.exp',
                    'Avalonia.X11.dll', 'Avalonia.FreeDesktop.dll', 'Avalonia.Native.dll',
                    'Avalonia.DesignerSupport.dll', 'Avalonia.Remote.Protocol.dll', 'ggml-metal.metal')) {
    Get-ChildItem $dist -Recurse -File -Filter $junk -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
}

$size = (Get-ChildItem $dist -Recurse -File | Measure-Object -Property Length -Sum).Sum
Write-Host ("`nOK -> {0}  ({1:0.0} MB, {2} 个文件)" -f $dist, ($size / 1MB), (Get-ChildItem $dist -Recurse -File).Count)

if ($Zip) {
    $zipPath = Join-Path $root ("dist\SubtitleStudio-{0}.zip" -f $Mode)
    Write-Host "压缩中 ..."
    New-Zip $dist $zipPath
    Write-Host ("ZIP -> {0}  ({1:0.0} MB)" -f $zipPath, ((Get-Item $zipPath).Length / 1MB))
}

if ($selfContained) {
    Write-Host "双击 dist\SubtitleStudio\SubtitleStudio.exe 即可运行（自包含，无需安装 .NET）"
} else {
    Write-Host "slim 版需要目标机装 .NET 10 Desktop Runtime：https://dotnet.microsoft.com/download/dotnet/10.0"
}
