param(
  [int]$Chars = 1000,
  [int]$SpeedMs = 250
)
# 高压打字压测：生成 Chars 字混合文本，以 SpeedMs 毫秒/字（约 240 字/分 @250ms）模拟输入，
# 检测漏读/重复读/乱序/队列积压/稳定性。
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root '发布\归零归零.exe'
$log = Join-Path $root '测试\stress_log.txt'
$textFile = Join-Path $root '测试\stress_text.txt'
$report = Join-Path $root '测试\stress_report.txt'

if (-not (Test-Path $exe)) { throw '请先运行 build.ps1' }

# 1. 检查是否有正在运行的实例（双实例双钩子会互相干扰）
$running = Get-Process -Name '归零归零' -ErrorAction SilentlyContinue
if ($running) {
  Write-Host "错误：归零归零正在运行（PID $($running.Id -join ',')）。请先从托盘退出，再运行压测。" -ForegroundColor Red
  exit 2
}

# 2. 生成混合文本（约85%汉字 + 标点/数字/字母）
$hanzi = '的一是在不了有和人这中大为上个国我以要他时来用们生到作地于出就分对成会可主发年动同工也能下过子说产种面而方后多定行学法所民得经十三之进着等部度家电力里如水化高自二理起小物现实加量都两体制机当使点从业本去把性好应开它合还因由其些然前外天政四日那社义事平形相全表间样与关各重新线内数正心反你明看原又么利比或但质气第向道命此变条只没结解问意建月公无系军很情者最立代想已通并提直题党程展五果料象员革位入常文总次品式活设及管特件长求老头基资边流路级少图山统接知较将组见计别她手角期根论运农指几九区强放决西被干做必战先回则任取据处队南给色光门即保治北造百规热领七海口东导器压志世金增争济阶油思术极交受联什认六共权收证改清己美再采转更单风切打白教速花带安场身车例真务具万每目至达走积示议声报斗完类八离华名确才科张信马节话米整空元况今集温传土许步群广石记需段研界拉林律叫且究观越织装影算低持音众书布复容儿须际商非验连断深难近矿千周委素技备半办青省列习响约支般史感劳便团往酸历市克何除消构府称太准精值号率族维划选标写存候毛亲快效斯院查江型眼王按格养易置派层片始却专状育厂京识适属圆包火住调满县局照参红细引听该铁价严龙飞'
$punct = '，。、；：？！""''（）《》…—·'
$digits = '0123456789'
$letters = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ'
$sb = New-Object System.Text.StringBuilder
$rnd = New-Object System.Random
for ($i = 0; $i -lt $Chars; $i++) {
  $r = $rnd.Next(100)
  if ($r -lt 85) { [void]$sb.Append($hanzi[$rnd.Next($hanzi.Length)]) }
  elseif ($r -lt 93) { [void]$sb.Append($punct[$rnd.Next($punct.Length)]) }
  elseif ($r -lt 97) { [void]$sb.Append($digits[$rnd.Next($digits.Length)]) }
  else { [void]$sb.Append($letters[$rnd.Next($letters.Length)]) }
}
$text = $sb.ToString()
[System.IO.File]::WriteAllText($textFile, $text, (New-Object System.Text.UTF8Encoding($false)))

# 3. 运行压测（--exit-ms 覆盖打字时长 + 播放尾巴 + 裕量）
$exitMs = 5000 + $Chars * $SpeedMs + 60000
Remove-Item $log -ErrorAction SilentlyContinue
Write-Host "开始压测：$Chars 字 @ $SpeedMs ms/字（约 $([math]::Round(60000/$SpeedMs)) 字/分）"
$p = Start-Process $exe -ArgumentList '--test', $log, '--auto-text', $textFile, '--auto-speed', "$SpeedMs", '--exit-ms', "$exitMs" -PassThru -WindowStyle Hidden
$ok = $p.WaitForExit(($exitMs + 120000))
if (-not $ok) { $p.Kill(); throw "压测超时（$exitMs ms 未退出）" }
$exitCode = $p.ExitCode
Write-Host "进程退出码：$exitCode"

# 4. 分析日志
if (-not (Test-Path $log)) { throw '没有生成日志' }
$lines = [System.IO.File]::ReadAllLines($log, [System.Text.Encoding]::UTF8)
$zhIds = New-Object System.Collections.Generic.List[int]
$batchIds = New-Object System.Collections.Generic.List[int]
$playFiles = New-Object System.Collections.Generic.List[int]
$errors = New-Object System.Collections.Generic.List[string]
$zhCount = 0; $spokenChars = 0; $lastZhAt = ''; $lastPlayDoneAt = ''
foreach ($l in $lines) {
  if ($l -match '^(\d\d:\d\d:\d\d\.\d\d\d) ZH:(\d+):') { $zhIds.Add([int]$Matches[2]); $zhCount++; $lastZhAt = $Matches[1] }
  elseif ($l -match 'AUTO_TEXT_DONE total=(\d+) hanzi=\d+ spoken=(\d+)') { $spokenChars = [int]$Matches[2] }
  elseif ($l -match 'W_BATCH ids=\[([\d,]+)\]') {
    foreach ($id in $Matches[1] -split ',') { if ($id -ne '') { $batchIds.Add([int]$id) } }
  }
  elseif ($l -match 'PLAY_START gll_(\d+)\.wav') { $playFiles.Add([int]$Matches[1]) }
  elseif ($l -match '_ERR:|WORKER_ERR|SPEAKER_ERR|PLAY_OPEN_ERR') { $errors.Add($l) }
  elseif ($l -match '^(\d\d:\d\d:\d\d\.\d\d\d) PLAY_DONE') { $lastPlayDoneAt = $Matches[1] }
}
$out = New-Object System.Collections.Generic.List[string]
$cjkCount = 0
foreach ($ch in $text.ToCharArray()) { if ($hanzi.Contains($ch)) { $cjkCount++ } }
$out.Add("=== 压测报告：$Chars 字 @ $SpeedMs ms/字（约 $([math]::Round(60000/$SpeedMs)) 字/分） ===")
$out.Add("输入字符数：$($text.Length)（其中汉字 $cjkCount）")
$out.Add("ZH 入队条数：$zhCount")
$out.Add("AUTO_TEXT_DONE spoken（应用自计汉字朗读数）：$spokenChars")
$out.Add("退出码：$exitCode")

# 漏读：汉字输入数 vs 应用自计朗读汉字数（spoken 只统计 VK_PACKET 汉字通道）
$miss = $cjkCount - $spokenChars
$out.Add("漏读估计（输入汉字数 - 自计朗读数）：$miss")
if ($miss -gt 0) { $out.Add("  !! 疑似漏读 $miss 字") } else { $out.Add("  无漏读") }

# ID 重复与单调性（ZH id 与 EN/数字共用序号，允许断号，只查重复与回退）
$dup = 0; $zhOrderBad = 0
for ($i = 1; $i -lt $zhIds.Count; $i++) {
  if ($zhIds[$i] -eq $zhIds[$i-1]) { $dup++ }
  elseif ($zhIds[$i] -lt $zhIds[$i-1]) { $zhOrderBad++ }
}
$out.Add("入队 ID 重复数：$dup")
$out.Add("ZH 入队序号回退次数：$zhOrderBad")
if ($dup -gt 0) { $out.Add("  !! 重复入队（可能重复读）") }
if ($zhOrderBad -gt 0) { $out.Add("  !! 入队乱序") }

# 乱序：W_BATCH 的 id 序列应单调递增（排除退出时的 id=0 停机批次）
$orderBad = 0
for ($i = 1; $i -lt $batchIds.Count; $i++) {
  if ($batchIds[$i] -eq 0) { continue }
  if ($batchIds[$i] -lt $batchIds[$i-1]) { $orderBad++ }
}
$out.Add("W_BATCH 逆序次数（不含停机批次）：$orderBad")

# 播放文件序号应严格递增（重复播放 = 重复读）
$playBad = 0
for ($i = 1; $i -lt $playFiles.Count; $i++) {
  if ($playFiles[$i] -le $playFiles[$i-1]) { $playBad++ }
}
$out.Add("播放文件序号重复/回退次数：$playBad")
if ($playBad -gt 0) { $out.Add("  !! 存在重复播放（可能重复读）") }

# 积压：最大批量大小 + 最后一个入队到最后一个播放的延迟
$maxBatch = 0
foreach ($l in $lines) {
  if ($l -match 'W_BATCH ids=\[([\d,]+)\]') {
    $n = @($Matches[1] -split ',' | Where-Object { $_ -ne '' }).Count
    if ($n -gt $maxBatch) { $maxBatch = $n }
  }
}
$out.Add("最大批量大小（队列积压指标）：$maxBatch")
if ($maxBatch -gt 10) { $out.Add("  !! 批量偏大，可能积压") }

# 错误
$out.Add("错误日志条数：$($errors.Count)")
foreach ($e in ($errors | Select-Object -First 5)) { $out.Add("  $e") }

# 尾部延迟
$out.Add("最后入队时间：$lastZhAt  最后播放结束：$lastPlayDoneAt")

$reportText = $out -join "`r`n"
[System.IO.File]::WriteAllText($report, $reportText, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ''
Write-Host $reportText
Write-Host ''
Write-Host "报告已写入：$report"
