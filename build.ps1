# 编译 SubtitleStudio（Debug）。
#
# 普通环境：直接用系统 dotnet。
# 受限/离线环境：如果仓库上一层有 .tools\dotnet 和 .tools\nuget-local，
# 会自动改用这套本地 SDK 与离线包，并把临时目录、NuGet 缓存都收进 .tools。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent $root
$tools = Join-Path $repo '.tools'

$dotnet = 'dotnet'
$restoreSource = @()

if (Test-Path $tools) {
    $localSdk = Join-Path $tools 'dotnet'
    $localFeed = Join-Path $tools 'nuget-local'

    # MSBuild 和编译器要往 %TEMP% 写临时目录，受限环境统一收到 .tools\tmp
    $tempRoot = Join-Path $tools 'tmp'
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    $env:TMP = (Resolve-Path $tempRoot).Path
    $env:TEMP = $env:TMP

    # CLI 的 home 与 NuGet 缓存也指到 .tools，避免写用户目录
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

Write-Host "dotnet: $dotnet"
& $dotnet --version

foreach ($proj in @($app, $cli)) {
    Write-Host "`n=== restore $(Split-Path -Leaf $proj)"
    & $dotnet restore $proj @restoreSource
    if ($LASTEXITCODE -ne 0) { throw "restore failed: $proj" }

    Write-Host "=== build $(Split-Path -Leaf $proj)"
    & $dotnet build $proj --no-restore -c Debug
    if ($LASTEXITCODE -ne 0) { throw "build failed: $proj" }
}

Write-Host "`nOK -> src\SubtitleStudio.App\bin\Debug\net10.0\SubtitleStudio.exe"
