$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Split-Path -Parent (Split-Path -Parent $root)
$releaseName = -join ([char]0x53D1, [char]0x5E03)
$out = Join-Path $repo $releaseName
New-Item -ItemType Directory -Force -Path $out | Out-Null
$tcc64 = 'C:\tmp\tcc64_1\tcc\tcc.exe'
$tcc32 = 'C:\tmp\tcc32_1\tcc\tcc.exe'
if (-not (Test-Path $tcc64)) { throw 'tcc64 not found' }
if (-not (Test-Path $tcc32)) { throw 'tcc32 not found' }

# 钩子 DLL 可能被常驻后台进程（如 Oppo Connect 的 UI）占用，先释放
Get-Process -Name 'oplus_remote_ui' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$buildId = Get-Date -Format 'yyyyMMdd_HHmmss'
$dll64 = 'gll_hook64_' + $buildId + '.dll'
$dll32 = 'gll_hook32_' + $buildId + '.dll'

& $tcc64 -shared -limm32 -lole32 -o (Join-Path $out $dll64) `
  (Join-Path $root 'gll_tsf_hook.c') (Join-Path $root 'gll_hook64.def')
if ($LASTEXITCODE -ne 0) { throw 'hook64 build failed' }

& $tcc32 -shared -limm32 -lole32 -o (Join-Path $out $dll32) `
  (Join-Path $root 'gll_tsf_hook.c') (Join-Path $root 'gll_hook32.def')
if ($LASTEXITCODE -ne 0) { throw 'hook32 build failed' }

[System.IO.File]::WriteAllText((Join-Path $out 'gll_hook64.txt'), $dll64)
[System.IO.File]::WriteAllText((Join-Path $out 'gll_hook32.txt'), $dll32)

# 清理旧的版本化 DLL（被进程占用的会删除失败，下次开机自动清掉）
Get-ChildItem -Path $out -Filter 'gll_hook64_*.dll' -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -ne $dll64 } | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $out -Filter 'gll_hook32_*.dll' -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -ne $dll32 } | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $out -Filter 'gll_hook64_*.def' -ErrorAction SilentlyContinue |
  Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $out -Filter 'gll_hook32_*.def' -ErrorAction SilentlyContinue |
  Remove-Item -Force -ErrorAction SilentlyContinue

Write-Output 'HOOK BUILD OK'
