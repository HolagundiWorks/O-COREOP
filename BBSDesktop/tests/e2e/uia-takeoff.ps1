param([string]$Exe, [string]$Fixture)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
$ErrorActionPreference = 'Continue'
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$PC = [System.Windows.Automation.PropertyCondition]
$root = $AE::RootElement
$bin = "$env:LOCALAPPDATA\ShilpiDB\bin"

function Find-Name($parent, [string]$name, [int]$timeoutMs = 4000) {
  $cond = New-Object $PC($AE::NameProperty, $name)
  $sw = [Diagnostics.Stopwatch]::StartNew()
  do { $e = $parent.FindFirst($TS::Descendants, $cond); if ($e) { return $e }; Start-Sleep -Milliseconds 150 } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
  return $null
}
function Names($parent, [int]$max = 100) {
  $out = @()
  foreach ($e in $parent.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
    $n = $e.Current.Name
    if ($n -and $n.Length -lt 400) { $out += "$($e.Current.ControlType.ProgrammaticName.Replace('ControlType.','')): $n" }
  }
  return $out | Select-Object -Unique -First $max
}
function Invoke-El($e) {
  $p = $null
  if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return $true }
  if ($e.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) { $p.Expand(); return $true }
  if ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return $true }
  return $false
}

# ---- a shared take-off server holding what O-Draft really wrote, on the default ports ----
$work = Join-Path $env:TEMP ("uia-takeoff-" + [guid]::NewGuid().ToString("N")); New-Item -ItemType Directory $work | Out-Null
$db = Start-Process "$bin\shilpid.exe" "--bind 127.0.0.1:7420 --data `"$work\t.vdb`" --log-level error" -PassThru -WindowStyle Hidden
$gw = Start-Process "$bin\shilpi-http.exe" "--bind 127.0.0.1:7421 --backend 127.0.0.1:7420" -PassThru -WindowStyle Hidden
for ($i = 0; $i -lt 40; $i++) { try { if ((Invoke-WebRequest "http://127.0.0.1:7421/health" -UseBasicParsing -TimeoutSec 1).StatusCode -eq 200) { break } } catch { Start-Sleep -Milliseconds 250 } }
$recs = Get-Content -Raw $Fixture | ConvertFrom-Json
foreach ($r in $recs) {
  $q = "minX=$($r.minX)&minY=$($r.minY)&maxX=$($r.maxX)&maxY=$($r.maxY)"
  Invoke-WebRequest -Method Put -Uri "http://127.0.0.1:7421/records/$($r.id)?$q" -Body ([Text.Encoding]::UTF8.GetBytes($r.payload)) -UseBasicParsing | Out-Null
}
# ---- the annotated PDF, as O-Draft is to record it: chunk records (kind 9) and the header's annotated_pdf block ----
$pdfPath = Join-Path (Split-Path $Fixture) "annotated-sample.pdf"
if (Test-Path $pdfPath) {
  $bytes = [IO.File]::ReadAllBytes($pdfPath)
  $hdr = $recs | Where-Object { ([uint64]$_.id -shr 56) -eq 1 } | Select-Object -First 1
  $slot = ([uint64]$hdr.id -shr 32) -band 0xFFFFFF
  $chunk = 4096; $count = [int][math]::Ceiling($bytes.Length / $chunk)
  for ($i = 0; $i -lt $count; $i++) {
    [byte[]]$part = $bytes[($i * $chunk)..([math]::Min(($i + 1) * $chunk, $bytes.Length) - 1)]
    $id = ([uint64]9 -shl 56) -bor ($slot -shl 32) -bor [uint64]$i
    Invoke-WebRequest -Method Put -Uri "http://127.0.0.1:7421/records/$id`?minX=0&minY=0&maxX=0&maxY=0" -Body $part -ContentType "application/octet-stream" -UseBasicParsing | Out-Null
  }
  $h = $hdr.payload | ConvertFrom-Json
  $sha = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($bytes)) -replace '-', '')
  $h | Add-Member -NotePropertyName annotated_pdf -NotePropertyValue ([pscustomobject]@{ sha256 = $sha; bytes = $bytes.Length; chunks = $count; pages = 2; generated = "2026-10-19T12:00:00Z" }) -Force
  Invoke-WebRequest -Method Put -Uri "http://127.0.0.1:7421/records/$($hdr.id)?minX=0&minY=0&maxX=0&maxY=0" -Body ([Text.Encoding]::UTF8.GetBytes(($h | ConvertTo-Json -Depth 12 -Compress))) -UseBasicParsing | Out-Null
  "annotated pdf recorded: $($bytes.Length) bytes in $count chunks"
}
"server holds $((Invoke-RestMethod http://127.0.0.1:7421/records).records.Count) records"

$proc = Start-Process -FilePath $Exe -PassThru
$win = $null
for ($i = 0; $i -lt 60 -and -not $win; $i++) {
  Start-Sleep -Milliseconds 500; $proc.Refresh()
  if ($proc.HasExited) { "APP EXITED at start (code $($proc.ExitCode))"; break }
  $win = $root.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $proc.Id)))
}
try {
  if ($win) {
    Start-Sleep -Seconds 2
    [void](Invoke-El (Find-Name $win "File"))
    Start-Sleep -Milliseconds 500
    $item = Find-Name $root ("Take-off from ShilpiDB" + [char]0x2026)
    "menu item found: $($item -ne $null)"
    [void](Invoke-El $item)
    Start-Sleep -Seconds 7   # the page polls every 5 s
    "--- Take-off page ---"
    $all = Names $win 400; $ix = [array]::IndexOf($all, "Text: Take-off"); if ($ix -lt 0) { $ix = 0 }; $all[$ix..($all.Count-1)] | ForEach-Object { "   $_" }
    $read = Find-Name $win ("Read take-off" + [char]0x2026) 2000
    "Read button found: $($read -ne $null)  enabled: $(if ($read) { $read.Current.IsEnabled })"
    if ($read -and $read.Current.IsEnabled) {
      [void](Invoke-El $read); Start-Sleep -Seconds 3
      "--- preview dialog ---"
      $dlg = Find-Name $root "Import take-off: " 1500
      $title = $root.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "Import")))
      Names $root 200 | Where-Object { $_ -match "Import take-off|to add|Add |Update|Flagged|Skipped|wall|chosen|thickness|Check|flooring|masonry|Cancel|slabs|columns|lintels|doors|windows" } | Select-Object -First 40 | ForEach-Object { "   $_" }
      [System.Windows.Forms.SendKeys]::SendWait("{ESC}"); Start-Sleep -Milliseconds 600
    }
    "--- fetch annotated drawing ---"
    $fetch = Find-Name $win "Fetch annotated drawing" 2000
    "fetch enabled: $(if ($fetch) { $fetch.Current.IsEnabled })"
    if ($fetch -and $fetch.Current.IsEnabled) {
      [void](Invoke-El $fetch); Start-Sleep -Seconds 3
      Names $win 400 | Where-Object { $_ -match "Annotated drawing|stored|fetched|Open|checksum|PDF|annotated" } | ForEach-Object { "   $_" }
    }
    "--- status after read ---"
    Names $win 160 | Where-Object { $_ -match "take-off records|could not|server|Reading|worked out" } | ForEach-Object { "   $_" }
    $proc.Refresh(); "app alive: $(-not $proc.HasExited)"
  }
}
finally {
  try { if ($proc -and -not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep 2; if (-not $proc.HasExited) { $proc.Kill() } } } catch {}
  foreach ($p in @($gw, $db)) { try { if ($p -and -not $p.HasExited) { $p.Kill() } } catch {} }
  Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
