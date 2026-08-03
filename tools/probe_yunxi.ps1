Add-Type -AssemblyName System.Speech
$ss = New-Object System.Speech.Synthesis.SpeechSynthesizer
$all = @()
foreach ($v in $ss.GetInstalledVoices()) { $all += $v.VoiceInfo.Name }
Write-Output ("VOICES: " + ($all -join " | "))
try {
  $ss.SelectVoice('Microsoft Yunxi')
  $out = 'C:\tmp\yunxi_test.wav'
  $ss.SetOutputToWaveFile($out)
  $ss.Speak('hello yunxi, this is a speech synthesis test.')
  $ss.SetOutputToNull()
  $ss.Dispose()
  $f = Get-Item $out
  Write-Output ("WAV bytes: " + $f.Length)
} catch {
  Write-Output ("SPEAK ERR: " + $_.Exception.Message)
}
