param([switch]$SkipLaunch, [switch]$AutoClose)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = New-Object System.Collections.Generic.List[string]

function Log($m) {
  $log.Add($m)
  Write-Host $m
}

function Test-NonAscii($s) {
  foreach ($ch in $s.ToCharArray()) {
    if ([int]$ch -gt 127) { return $true }
  }
  return $false
}

# 0. 路径检查（必须无中文）
if (Test-NonAscii $scriptDir) {
  Write-Host '错误：安装包所在路径包含中文。'
  Write-Host '请把整个文件夹解压/移动到无中文路径（例如 C:\NVSA）后重新运行。'
  if (-not $AutoClose) { Read-Host '按回车键关闭窗口' }
  exit 1
}

# 1. 自动请求管理员权限
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
  Write-Host '正在请求管理员权限，请在弹窗中点“是”...'
  $argList = @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $PSCommandPath + '"'))
  if ($SkipLaunch) { $argList += '-SkipLaunch' }
  if ($AutoClose) { $argList += '-AutoClose' }
  Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -Wait
  exit
}

try {
  $adapterSrc = Join-Path $scriptDir '02-NaturalVoiceSAPIAdapter'
  $appSrc = Join-Path $scriptDir '01-GuilingGuiling'
  if (-not (Test-Path (Join-Path $adapterSrc 'Installer.exe'))) { throw '找不到 02-NaturalVoiceSAPIAdapter 文件夹，请确认压缩包文件完整。' }
  if (-not (Test-Path (Join-Path $appSrc '归零归零.exe'))) { throw '找不到 01-GuilingGuiling 文件夹，请确认压缩包文件完整。' }

  # 2. 复制语音适配器
  $adapterDest = 'C:\Program Files\NaturalVoiceSAPIAdapter'
  Log '正在复制语音适配器（含晓晓语音模型）...'
  if (-not (Test-Path $adapterDest)) { New-Item -ItemType Directory -Path $adapterDest | Out-Null }
  Copy-Item -Path (Join-Path $adapterSrc '*') -Destination $adapterDest -Recurse -Force
  Log ('完成：' + $adapterDest)

  # 3. 注册 64 位与 32 位语音组件
  Log '正在注册 64 位语音组件...'
  $dll64 = Join-Path $adapterDest 'x64\NaturalVoiceSAPIAdapter.dll'
  $p64 = Start-Process -FilePath "$env:SystemRoot\System32\regsvr32.exe" -ArgumentList ('/s "' + $dll64 + '"') -WorkingDirectory $adapterDest -Wait -PassThru
  if ($p64.ExitCode -ne 0) { throw ('64 位组件注册失败（退出码 ' + $p64.ExitCode + '），请检查杀毒软件是否拦截后重试。') }
  Log '正在注册 32 位语音组件...'
  $dll86 = Join-Path $adapterDest 'x86\NaturalVoiceSAPIAdapter.dll'
  $p86 = Start-Process -FilePath "$env:SystemRoot\SysWOW64\regsvr32.exe" -ArgumentList ('/s "' + $dll86 + '"') -WorkingDirectory $adapterDest -Wait -PassThru
  if ($p86.ExitCode -ne 0) { throw ('32 位组件注册失败（退出码 ' + $p86.ExitCode + '），请检查杀毒软件是否拦截后重试。') }
  Log '组件注册成功'

  # 4. 写入语音配置
  Log '正在写入语音配置...'
  $cfg = 'HKCU:\Software\NaturalVoiceSAPIAdapter'
  if (-not (Test-Path $cfg)) { New-Item -Path $cfg | Out-Null }
  New-ItemProperty -Path $cfg -Name 'LogLevel' -Value 2 -PropertyType DWord -Force | Out-Null
  $enumKey = Join-Path $cfg 'Enumerator'
  if (-not (Test-Path $enumKey)) { New-Item -Path $enumKey | Out-Null }
  New-ItemProperty -Path $enumKey -Name 'NarratorVoicePath' -Value (Join-Path $adapterDest 'voices') -PropertyType String -Force | Out-Null
  New-ItemProperty -Path $enumKey -Name 'NoNarratorVoices' -Value 0 -PropertyType DWord -Force | Out-Null
  New-ItemProperty -Path $enumKey -Name 'NoEdgeVoices' -Value 1 -PropertyType DWord -Force | Out-Null
  New-ItemProperty -Path $enumKey -Name 'NoAzureVoices' -Value 1 -PropertyType DWord -Force | Out-Null
  Log '语音配置写入完成'

  # 5. 复制归零归零软件
  $appDest = 'C:\GuilingGuiling'
  Log '正在复制归零归零软件...'
  if (-not (Test-Path $appDest)) { New-Item -ItemType Directory -Path $appDest | Out-Null }
  Copy-Item -Path (Join-Path $appSrc '*') -Destination $appDest -Recurse -Force
  $appData = Join-Path $env:APPDATA 'GuiLingGuiLing'
  $settingsTarget = Join-Path $appData 'settings.xml'
  if (-not (Test-Path $settingsTarget)) {
    if (-not (Test-Path $appData)) { New-Item -ItemType Directory -Path $appData | Out-Null }
    Copy-Item -LiteralPath (Join-Path $appSrc 'settings.xml') -Destination $settingsTarget -Force
    Log '已写入晓晓语音默认配置'
  } else {
    Log '检测到已有配置文件，保留现有设置'
  }

  # 6. 验证晓晓语音（64 位与 32 位）
  Log '正在验证晓晓语音...'
  Add-Type -AssemblyName System.Speech
  $ss = New-Object System.Speech.Synthesis.SpeechSynthesizer
  $found64 = $false
  foreach ($v in $ss.GetInstalledVoices()) {
    if ($v.VoiceInfo.Name -like '*Xiaoxiao*') { $found64 = $true; break }
  }
  $ss.Dispose()
  if (-not $found64) { throw '64 位语音列表里没有晓晓，请检查杀毒软件是否拦截了组件注册。' }

  $inner32 = 'Add-Type -AssemblyName System.Speech; $ss=New-Object System.Speech.Synthesis.SpeechSynthesizer; $ok=$false; foreach($v in $ss.GetInstalledVoices()){ if($v.VoiceInfo.Name -like "*Xiaoxiao*"){$ok=$true; break} }; $ss.Dispose(); if($ok){exit 0}else{exit 1}'
  $b64 = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($inner32))
  & "$env:SystemRoot\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -EncodedCommand $b64
  if ($LASTEXITCODE -ne 0) { throw '32 位语音列表里没有晓晓，请检查杀毒软件是否拦截了组件注册。' }
  Log '晓晓语音验证通过（32 位 / 64 位）'

  # 7. 启动归零归零
  if (-not $SkipLaunch) {
    Start-Process -FilePath (Join-Path $appDest '归零归零.exe')
    Log '归零归零已启动，使用的是晓晓语音。'
  }
  Log '安装完成！'
} catch {
  Log ('安装失败：' + $_.Exception.Message)
}

try {
  $logDir = 'C:\GuilingGuiling'
  if (Test-Path $logDir) {
    Set-Content -LiteralPath (Join-Path $logDir 'install_log.txt') -Value $log -Encoding UTF8
  }
} catch {}

if (-not $AutoClose) {
  Read-Host '按回车键关闭窗口'
}
