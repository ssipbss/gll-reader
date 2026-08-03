$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$out = Join-Path $root 'bin'
$testOut = Join-Path $root 'test_bin'
$finalOut = Join-Path $root '发布'

New-Item -ItemType Directory -Force -Path $out | Out-Null
New-Item -ItemType Directory -Force -Path $testOut | Out-Null
New-Item -ItemType Directory -Force -Path $finalOut | Out-Null

$gac = [string](Get-ChildItem 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Speech' -Recurse -Filter 'System.Speech.dll' | Select-Object -First 1 -ExpandProperty FullName)
$uia1 = [string](Get-ChildItem 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient' -Recurse -Filter 'UIAutomationClient.dll' | Select-Object -First 1 -ExpandProperty FullName)
$uia2 = [string](Get-ChildItem 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes' -Recurse -Filter 'UIAutomationTypes.dll' | Select-Object -First 1 -ExpandProperty FullName)
$winbase = [string](Get-ChildItem 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase' -Recurse -Filter 'WindowsBase.dll' | Select-Object -First 1 -ExpandProperty FullName)
if (-not $gac) { throw '找不到 System.Speech.dll' }

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw '找不到 csc.exe' }

$mainLines = New-Object System.Collections.Generic.List[string]
$mainLines.Add('/nologo')
$mainLines.Add('/target:winexe')
$mainLines.Add('/optimize+')
$mainLines.Add('/codepage:65001')
$mainLines.Add('/win32icon:src\app.ico')
$mainLines.Add('/r:System.dll')
$mainLines.Add('/r:System.Core.dll')
$mainLines.Add('/r:Microsoft.CSharp.dll')
$mainLines.Add('/r:System.Windows.Forms.dll')
$mainLines.Add('/r:System.Drawing.dll')
$mainLines.Add('/r:System.Xml.dll')
$mainLines.Add('/r:' + $uia1)
$mainLines.Add('/r:' + $uia2)
$mainLines.Add('/r:' + $winbase)
$mainLines.Add('/r:' + $gac)
$winrtDir = Join-Path $root 'tools\winrt'
$mainLines.Add('/r:System.Runtime.dll')
$mainLines.Add('/r:System.Runtime.WindowsRuntime.dll')
$mainLines.Add('/r:tools\winrt\Windows.WinMD')
$mainLines.Add('/r:tools\winrt\Windows.Foundation.FoundationContract.winmd')
$mainLines.Add('/r:tools\winrt\Windows.Foundation.UniversalApiContract.winmd')
$mainLines.Add('/out:bin\app.exe')
Get-ChildItem $src -Filter '*.cs' | Sort-Object Name | ForEach-Object { $mainLines.Add('src\' + $_.Name) }

$mainRsp = Join-Path $root 'build.rsp'
[System.IO.File]::WriteAllLines($mainRsp, $mainLines, [System.Text.Encoding]::ASCII)

$testLines = New-Object System.Collections.Generic.List[string]
$testLines.Add('/nologo')
$testLines.Add('/target:exe')
$testLines.Add('/optimize+')
$testLines.Add('/codepage:65001')
$testLines.Add('/out:test_bin\sendkeys.exe')
$testLines.Add('tools\SendKeysTool.cs')
$testRsp = Join-Path $root 'test_build.rsp'
[System.IO.File]::WriteAllLines($testRsp, $testLines, [System.Text.Encoding]::ASCII)

& $csc ('@' + $mainRsp)
if ($LASTEXITCODE -ne 0) { throw '主程序编译失败' }

& $csc ('@' + $testRsp)
if ($LASTEXITCODE -ne 0) { throw '测试工具编译失败' }


Copy-Item (Join-Path $out 'app.exe') (Join-Path $finalOut '归零归零.exe') -Force
Copy-Item (Join-Path $root '使用说明.txt') (Join-Path $finalOut '使用说明.txt') -Force
Copy-Item (Join-Path $root '测试清单.txt') (Join-Path $finalOut '测试清单.txt') -Force
Write-Output ("BUILD OK: " + (Join-Path $finalOut '归零归零.exe'))
