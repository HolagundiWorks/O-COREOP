param([string]$Exe, [string]$Fixture, [string]$FakeExe)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
$ErrorActionPreference = 'Continue'
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $PC = [System.Windows.Automation.PropertyCondition]
$root = $AE::RootElement
$ELL = [string][char]0x2026
$bin = "$env:LOCALAPPDATA\ShilpiDB\bin"
$work = Join-Path $env:TEMP ("uia-sync-" + [guid]::NewGuid().ToString("N")); New-Item -ItemType Directory $work | Out-Null
$log = Join-Path $work "fake.log"; New-Item -ItemType File $log | Out-Null
$results = [ordered]@{}
function Check($name, $ok, $detail = "") { $results[$name] = [bool]$ok; "{0,-6} {1} {2}" -f $(if ($ok) { "PASS" } else { "FAIL" }), $name, $detail }

function Find-Name($parent, [string]$name, [int]$timeoutMs = 4000) {
  $cond = New-Object $PC($AE::NameProperty, $name); $sw = [Diagnostics.Stopwatch]::StartNew()
  do { $e = $parent.FindFirst($TS::Descendants, $cond); if ($e) { return $e }; Start-Sleep -Milliseconds 150 } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
  return $null
}
function Find-Like($parent, [string]$pattern, [int]$timeoutMs = 4000) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  do {
    foreach ($e in $parent.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) { if ($e.Current.Name -like $pattern) { return $e } }
    Start-Sleep -Milliseconds 250
  } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
  return $null
}
function Invoke-El($e) {
  if (-not $e) { return $false }
  $p = $null
  if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return $true }
  if ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return $true }
  if ($e.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) { $p.Expand(); return $true }
  return $false
}
function Set-Text($e, [string]$text) {
  $p = $null
  if ($e.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$p) -and -not $p.Current.IsReadOnly) { try { $p.SetValue($text); return $true } catch {} }
  $e.SetFocus(); Start-Sleep -Milliseconds 200; [System.Windows.Forms.SendKeys]::SendWait($text); return $true
}
function Menu($text) {
  [void](Invoke-El (Find-Name $win "File")); Start-Sleep -Milliseconds 500
  $item = Find-Name $root $text 3000
  if (-not $item) { return $false }
  [void](Invoke-El $item); Start-Sleep -Milliseconds 900; return $true
}
function LogText() { if (Test-Path $log) { Get-Content $log -Raw } else { "" } }
function Wait-Log([string]$pattern, [int]$sec = 10) { $sw = [Diagnostics.Stopwatch]::StartNew(); while ($sw.Elapsed.TotalSeconds -lt $sec) { if ((LogText) -match $pattern) { return $true }; Start-Sleep -Milliseconds 400 }; return $false }
function Fake($path) { Invoke-RestMethod "http://127.0.0.1:7550$path" }

$procs = @()
try {
  # ---- servers: fake AORMS on 7550, shared take-off server on 7420/7421 holding what O-Draft wrote ----
  $fake = Start-Process $FakeExe "7550 `"$log`"" -PassThru -WindowStyle Hidden; $procs += $fake
  $db = Start-Process "$bin\shilpid.exe" "--bind 127.0.0.1:7420 --data `"$work\t.vdb`" --log-level error" -PassThru -WindowStyle Hidden; $procs += $db
  $gw = Start-Process "$bin\shilpi-http.exe" "--bind 127.0.0.1:7421 --backend 127.0.0.1:7420" -PassThru -WindowStyle Hidden; $procs += $gw
  for ($i = 0; $i -lt 40; $i++) { try { if ((Invoke-WebRequest "http://127.0.0.1:7421/health" -UseBasicParsing -TimeoutSec 1).StatusCode -eq 200 -and (Invoke-WebRequest "http://127.0.0.1:7550/_test/state" -UseBasicParsing -TimeoutSec 1).StatusCode -eq 200) { break } } catch { Start-Sleep -Milliseconds 250 } }
  $recs = Get-Content -Raw $Fixture | ConvertFrom-Json
  foreach ($r in $recs) { $q = "minX=$($r.minX)&minY=$($r.minY)&maxX=$($r.maxX)&maxY=$($r.maxY)"; Invoke-WebRequest -Method Put -Uri "http://127.0.0.1:7421/records/$($r.id)?$q" -Body ([Text.Encoding]::UTF8.GetBytes($r.payload)) -UseBasicParsing | Out-Null }

  # ---- the app, pointed at the fake ----
  $env:AORMS_URL = "http://127.0.0.1:7550"
  $app = Start-Process -FilePath $Exe -PassThru; $procs += $app
  $win = $null
  for ($i = 0; $i -lt 60 -and -not $win; $i++) { Start-Sleep -Milliseconds 500; $win = $root.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $app.Id))) }
  Check "app starts" ($win -ne $null)
  Start-Sleep -Seconds 2

  # 1. sign in
  Check "File > Sign in opens the dialog" (Menu ("Sign in to AORMS" + $ELL))
  $email = Find-Name $win "Email"; $pw = Find-Name $win "Password"
  Check "dialog has Email and Password" ($email -and $pw)
  [void](Set-Text $email "tester@example.test"); [void](Set-Text $pw "wrong-password")
  [void](Invoke-El (Find-Name $win "Sign in")); Start-Sleep -Seconds 2
  Check "wrong password is refused with a message" ($null -ne (Find-Like $win "*Wrong email or password*" 3000))
  $pw = Find-Name $win "Password"; [void](Set-Text $pw "test-password")
  [void](Invoke-El (Find-Name $win "Sign in")); Start-Sleep -Seconds 3
  $chip = Find-Like $win "AORMS * Fake Studio*" 5000
  Check "signed in: the chip names the studio" ($null -ne $chip) $(if ($chip) { $chip.Current.Name })

  # 2. a take-off imported through the real UI gives the project some rows
  Check "File > Take-off opens" (Menu ("Take-off from ShilpiDB" + $ELL))
  Start-Sleep -Seconds 7
  [void](Invoke-El (Find-Name $win ("Read take-off" + $ELL))); Start-Sleep -Seconds 3
  $imp = Find-Name $root "Import" 3000
  Check "preview dialog offers Import" ($null -ne $imp)
  [void](Invoke-El $imp); Start-Sleep -Seconds 2

  # 3. push the project online
  Check "File > Push project online opens" (Menu ("Push project online" + $ELL))
  Start-Sleep -Seconds 2
  $push = Find-Name $root "Push online" 3000
  Check "push dialog lists the free project" ($null -ne (Find-Like $root "P-001*" 3000))
  [void](Invoke-El $push); Start-Sleep -Seconds 3
  Check "server received the project" (Wait-Log "PUSH ONLINE rows=\d+" 8) ((LogText) -split "`n" | Where-Object { $_ -like "PUSH ONLINE*" } | Select-Object -First 1)
  Check "no local paths were sent" ((LogText) -match "hasLocalPath=False")
  $state = Fake "/_test/state"
  Check "server holds the masonry row with length 9000" (($state.lengths | Where-Object { $_ -eq "9000" }) -ne $null) ("lengths=" + ($state.lengths -join ","))
  Start-Sleep -Seconds 4
  $chip = Find-Like $win "AORMS * Fake Studio*" 3000
  Check "chip says synced" ($chip -and $chip.Current.Name -like "*synced*") $(if ($chip) { $chip.Current.Name })

  # 4. change the take-off at the shared server, import again: one row should reach AORMS by itself
  $wall = $recs | Where-Object { $_.payload -like '*"length_mm":9000.0*' } | Select-Object -First 1
  $changed = $wall.payload.Replace('"length_mm":9000.0', '"length_mm":9500.0')
  $q = "minX=$($wall.minX)&minY=$($wall.minY)&maxX=$($wall.maxX)&maxY=$($wall.maxY)"
  Invoke-WebRequest -Method Put -Uri "http://127.0.0.1:7421/records/$($wall.id)?$q" -Body ([Text.Encoding]::UTF8.GetBytes($changed)) -UseBasicParsing | Out-Null
  [void](Menu ("Take-off from ShilpiDB" + $ELL)); Start-Sleep -Seconds 2
  [void](Invoke-El (Find-Name $win ("Read take-off" + $ELL))); Start-Sleep -Seconds 3
  [void](Invoke-El (Find-Name $root "Import" 3000)); Start-Sleep -Seconds 1
  Check "the edit reaches AORMS by itself (one row, length 9500)" (Wait-Log "ROWS 1 :: masonry 9500" 12) ((LogText) -split "`n" | Where-Object { $_ -like "ROWS*" } | Select-Object -Last 1)
  Check "settings sent (if any) carried no local path" (-not ((LogText) -match "SETTINGS localPath=True"))

  # 5. another computer changes a row; Pull latest brings it in
  $keys = Fake "/_test/keys"
  $mk = $keys | Where-Object { $_.key -like "masonry/*" -and $_.fields.source_ref -eq "W120" } | Select-Object -First 1
  $rid = ($mk.key -split "/", 2)[1]
  $f = $mk.fields; $f.length = "7777"
  $body = @{ rows = @(@{ section = "masonry"; row_id = $rid; fields = $f }) } | ConvertTo-Json -Depth 10 -Compress
  Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:7550/_test/delta" -Body $body -ContentType "application/json" | Out-Null
  function Snap() { $s = @{}; foreach ($e in $root.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) { $n = $e.Current.Name; if ($n) { $s[$n] = 1 } }; return $s }
  $before = Snap
  Check "File > Pull latest runs" (Menu "Pull latest from AORMS")
  $seen = @{}
  for ($i = 0; $i -lt 16; $i++) { Start-Sleep -Milliseconds 400; foreach ($k in (Snap).Keys) { if (-not $before.ContainsKey($k) -and -not $seen.ContainsKey($k)) { $seen[$k] = 1 } } }
  $newOnScreen = @($seen.Keys)
  $review = Find-Like $root "Changes arrived from elsewhere" 2500
  if ($review) { "   REVIEW DIALOG shown: " + ((Find-Like $root "*row(s) changed online*" 1500).Current.Name); [void](Invoke-El (Find-Name $root "Take online changes" 2000)); Start-Sleep -Milliseconds 800 }
  Check "the app asked for the catch-up" (Wait-Log "CATCHUP since=\d+ -> 1 rows" 8)
  Check "the app said the row was brought in (no error)" (($newOnScreen -like "*brought in from AORMS*").Count -gt 0 -and ($newOnScreen -like "*cannot be converted*").Count -eq 0) ($newOnScreen -join " | ")
  Start-Sleep -Seconds 4
  Check "nothing was sent back after the pull" (-not ((LogText) -cmatch "CATCHUP[^\n]*\n[\s\S]*ROWS \d+ ::"))
  $chip = Find-Like $win "AORMS * Fake Studio*" 3000
  Check "chip still says synced after the pull" ($chip -and $chip.Current.Name -like "*synced*") $(if ($chip) { $chip.Current.Name })
  # the pulled change is really in the project: the take-off read now sees that wall as changed in AQC since the import
  [void](Menu ("Take-off from ShilpiDB" + $ELL)); Start-Sleep -Seconds 2
  [void](Invoke-El (Find-Name $win ("Read take-off" + $ELL))); Start-Sleep -Seconds 3
  $sum = Find-Like $win "*take-off records read from*" 3000
  Check "after the pull the wall differs locally (flagged as edited since import)" ($sum -and $sum.Current.Name -match "[1-9]\d* flagged") $(if ($sum) { $sum.Current.Name.Substring(0, [Math]::Min(150, $sum.Current.Name.Length)) })
  [System.Windows.Forms.SendKeys]::SendWait("{ESC}"); Start-Sleep -Milliseconds 600

  # 6. sign out
  Check "File > Sign out" (Menu "Sign out of AORMS")
  Start-Sleep -Seconds 1
  $chip = Find-Like $win "AORMS * signed out*" 3000
  Check "chip says signed out" ($null -ne $chip)
  $app.Refresh(); Check "app still running at the end" (-not $app.HasExited)
}
finally {
  "--- fake server log ---"; (LogText) -split "`n" | Where-Object { $_ } | Select-Object -First 60 | ForEach-Object { "   $_" }
  foreach ($p in $procs) { try { if ($p -and -not $p.HasExited) { if ($p.MainWindowHandle -ne 0) { $p.CloseMainWindow() | Out-Null; Start-Sleep 1 }; if (-not $p.HasExited) { $p.Kill() } } } catch {} }
  cmdkey /delete:AORMS/Connect 2>&1 | Out-Null; cmdkey /delete:AORMS/Connect/aqc 2>&1 | Out-Null
  Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
  $fail = @($results.GetEnumerator() | Where-Object { -not $_.Value }).Count
  "RESULT: $($results.Count - $fail) passed, $fail failed"
}
