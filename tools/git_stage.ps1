$ErrorActionPreference = 'Stop'
$repo = (Get-Location).Path
$releaseName = -join ([char]0x53D1, [char]0x5E03)
$appName = -join ([char]0x5F52, [char]0x96F6, [char]0x5F52, [char]0x96F6) + '.exe'
$release = Join-Path $repo $releaseName

git add -- build.ps1 src tools
git add -- (Join-Path $release $appName)
git add -- (Join-Path $release 'gll_hook64.txt') (Join-Path $release 'gll_hook32.txt')
$ptr64 = [System.IO.File]::ReadAllText((Join-Path $release 'gll_hook64.txt')).Trim()
$ptr32 = [System.IO.File]::ReadAllText((Join-Path $release 'gll_hook32.txt')).Trim()
if ($ptr64) { git add -- (Join-Path $release $ptr64) }
if ($ptr32) { git add -- (Join-Path $release $ptr32) }
git add -- (Join-Path $release 'gll_hook32_host.exe')
git add -u
git status --short
