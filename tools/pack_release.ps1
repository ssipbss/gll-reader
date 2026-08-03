param(
  [Parameter(Mandatory = $true)][string]$StagingRoot,
  [Parameter(Mandatory = $true)][string]$ZipPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path -LiteralPath $ZipPath) {
  Remove-Item -LiteralPath $ZipPath -Force
}

[System.IO.Compression.ZipFile]::CreateFromDirectory(
  $StagingRoot,
  $ZipPath,
  [System.IO.Compression.CompressionLevel]::Optimal,
  $true)

$f = Get-Item -LiteralPath $ZipPath
Write-Output ("ZIP bytes: " + $f.Length)
$sha = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash
$shaFile = $ZipPath + '.sha256'
Set-Content -LiteralPath $shaFile -Value ($sha + '  ' + $f.Name) -Encoding UTF8
Get-Content -LiteralPath $shaFile
