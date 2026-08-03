$ErrorActionPreference = 'SilentlyContinue'
$repo = (Get-Location).Path
$release = -join ([char]0x53D1, [char]0x5E03)
$releaseDir = Join-Path $repo $release
Get-Process | Where-Object {
  try {
    $p = $_.Path
    $p -and $p.StartsWith($releaseDir, [System.StringComparison]::OrdinalIgnoreCase)
  } catch { $false }
} | Stop-Process -Force
Get-Process -Name 'oplus_remote_ui' -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Output 'stopped'
