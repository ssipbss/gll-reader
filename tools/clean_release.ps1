$ErrorActionPreference = 'Continue'
$repo = (Get-Location).Path
$releaseName = -join ([char]0x53D1, [char]0x5E03)
$release = Join-Path $repo $releaseName
$patterns = @(
  'gll_hook32.def', 'gll_hook32.dll',
  'gll_hook64.def', 'gll_hook64.dll',
  'gll_tsf_hook32.def', 'gll_tsf_hook32.dll',
  'gll_tsf_hook64.def', 'gll_tsf_hook64.dll',
  'gll_hook64_*.def', 'gll_hook32_*.def'
)
foreach ($pat in $patterns) {
  Get-ChildItem -LiteralPath $release -Filter $pat -ErrorAction SilentlyContinue | ForEach-Object {
    try {
      Remove-Item -LiteralPath $_.FullName -Force -ErrorAction Stop
      Write-Output ("removed " + $_.Name)
    } catch {
      Write-Output ("locked " + $_.Name)
    }
  }
}
Write-Output '--- remaining hook files ---'
Get-ChildItem -LiteralPath $release -File | Where-Object {
  $_.Name -like 'gll_hook*' -or $_.Name -like 'gll_tsf*'
} | ForEach-Object { Write-Output ($_.Name + " " + $_.Length) }
