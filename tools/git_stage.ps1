$ErrorActionPreference = 'Stop'
$repo = (Get-Location).Path
$releaseName = -join ([char]0x53D1, [char]0x5E03)
$appName = -join ([char]0x5F52, [char]0x96F6, [char]0x5F52, [char]0x96F6) + '.exe'
$release = Join-Path $repo $releaseName

git add -- build.ps1 src tools
git add -- (Join-Path $release $appName)
git add -- (Join-Path $release 'gll_hook64.txt') (Join-Path $release 'gll_hook32.txt')
git add -- (Join-Path $release 'gll_hook64_20260803_221128.dll')
git add -- (Join-Path $release 'gll_hook32_20260803_221128.dll')
git add -- (Join-Path $release 'gll_hook32_host.exe')
git status --short
