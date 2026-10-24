# 用系统 SAPI 合成一段中文测试音频（给 whisper 做冒烟测试）。
# 用法： powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\make-sample.ps1
param(
  [string]$Text = '大家好，这是一个测试音频。我们正在测试字幕生成软件的语音识别效果。',
  [string]$Out = (Join-Path $PSScriptRoot '..\.tools\sample-zh.wav')
)

Add-Type -AssemblyName System.Speech

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
  $synth.SelectVoice('Microsoft Huihui Desktop')
} catch {
  Write-Host "没找到 Huihui，用默认声音：$($_.Exception.Message)"
}

$synth.Rate = 0
$synth.SetOutputToWaveFile([System.IO.Path]::GetFullPath($Out))
$synth.Speak($Text)
$synth.Dispose()

$file = Get-Item $Out
Write-Host ("OK {0}  {1:N0} bytes" -f $file.FullName, $file.Length)
