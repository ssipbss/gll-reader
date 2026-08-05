$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc not found: $csc" }
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
  New-Item -ItemType Directory -Path 'test_bin' -Force | Out-Null
  & $csc '@tests.rsp'
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  & '.\test_bin\tests.exe'
  exit $LASTEXITCODE
} finally {
  Pop-Location
}
