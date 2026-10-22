# 编译并启动桌面应用
# 用法：.\run.ps1 [视频或音频路径]

param([string]$File)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$exe = Join-Path $root 'src\SubtitleStudio.App\bin\Debug\net10.0\SubtitleStudio.exe'
Write-Host "`n启动 $exe"

if ([string]::IsNullOrWhiteSpace($File)) {
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
} else {
    $full = (Resolve-Path $File).Path
    Start-Process -FilePath $exe -ArgumentList "`"$full`"" -WorkingDirectory (Split-Path -Parent $exe)
}
