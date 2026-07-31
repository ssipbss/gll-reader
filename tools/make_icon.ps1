Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$iconPath = Join-Path $root 'src\app.ico'

$bmp = New-Object System.Drawing.Bitmap 32, 32
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

$blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 114, 206))
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)

$g.FillEllipse($blue, 1, 1, 30, 30)

$pts = @(
  (New-Object System.Drawing.Point 8, 13),
  (New-Object System.Drawing.Point 8, 19),
  (New-Object System.Drawing.Point 13, 19),
  (New-Object System.Drawing.Point 19, 24),
  (New-Object System.Drawing.Point 19, 8),
  (New-Object System.Drawing.Point 13, 13)
)
$g.FillPolygon($white, [System.Drawing.Point[]]$pts)

$pen = New-Object System.Drawing.Pen $white, 2
$g.DrawArc($pen, 17, 8, 12, 16, -70, 140)
$g.DrawArc($pen, 22, 3, 18, 26, -70, 140)

$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [System.IO.File]::Create($iconPath)
$icon.Save($fs)
$fs.Close()

$pen.Dispose()
$blue.Dispose()
$white.Dispose()
$g.Dispose()
$bmp.Dispose()
Write-Output ("ICON OK: " + $iconPath)
