param(
  [Parameter(Mandatory = $true)][string]$Exe,
  [Parameter(Mandatory = $true)][string]$Osp,
  [int]$MinToAdd = 0
)
# UIA: launch with a .osp on the command line, open Take-off, refresh, read preview.
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$PC = [System.Windows.Automation.PropertyCondition]
$root = $AE::RootElement
$ELL = [string][char]0x2026
$results = [ordered]@{}
function Check($name, $ok, $detail = "") {
  $results[$name] = [bool]$ok
  "{0,-6} {1} {2}" -f $(if ($ok) { "PASS" } else { "FAIL" }), $name, $detail
}
function Find-Name($parent, [string]$name, [int]$timeoutMs = 5000) {
  $cond = New-Object $PC($AE::NameProperty, $name)
  $sw = [Diagnostics.Stopwatch]::StartNew()
  do {
    $e = $parent.FindFirst($TS::Descendants, $cond)
    if ($e) { return $e }
    Start-Sleep -Milliseconds 150
  } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
  return $null
}
function Find-Like($parent, [string]$pattern, [int]$timeoutMs = 5000) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  do {
    foreach ($e in $parent.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
      if ($e.Current.Name -like $pattern) { return $e }
    }
    Start-Sleep -Milliseconds 250
  } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
  return $null
}
function Invoke-El($e) {
  if (-not $e) { return $false }
  $p = $null
  if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return $true }
  if ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return $true }
  return $false
}

function Safe-Invoke($e) {
  if (-not $e) { return $false }
  try { return (Invoke-El $e) } catch { return $false }
}

$Osp = (Resolve-Path -LiteralPath $Osp).Path
if (-not (Test-Path -LiteralPath $Osp)) { throw "Missing project file: $Osp" }

$app = Start-Process -FilePath $Exe -ArgumentList @($Osp) -PassThru
try {
  $win = $null
  for ($i = 0; $i -lt 80 -and -not $win; $i++) {
    Start-Sleep -Milliseconds 500
    $app.Refresh()
    if ($app.HasExited) { Check "app starts" $false "exit $($app.ExitCode)"; break }
    $win = $root.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $app.Id)))
  }
  Check "app starts" ($win -ne $null)
  for ($w = 0; $w -lt 30; $w++) {
    Start-Sleep -Milliseconds 500
    $win = $root.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $app.Id)))
    if ($win -and $win.Current.Name -notmatch 'Untitled') { break }
  }

  # Project opens from the command line (.osp); open Take-off from File menu.
  $opened = $false
  for ($i = 0; $i -lt 25; $i++) {
    [void](Safe-Invoke (Find-Name $win "File"))
    Start-Sleep -Milliseconds 400
    $to = Find-Name $root ("Take-off (O-Draft)" + $ELL) 1500
    if (-not $to) { $to = Find-Like $root "*Take-off (O-Draft)*" 1000 }
    if ($to -and (Safe-Invoke $to)) { $opened = $true; break }
    Start-Sleep -Milliseconds 500
  }
  Check "open Take-off page" $opened

  # TakeoffPage refreshes on load; FillList selects the first drawing when none was selected.
  $row = $null
  $refOk = $false
  for ($i = 0; $i -lt 50; $i++) {
    $row = Find-Like $win "*complete*row*" 400
    if (-not $row) { $row = Find-Like $win "*small house*" 400 }
    if (-not $row) { $row = Find-Like $win "*complete*" 400 }
    if ($row) { break }
    if ($i -eq 8 -or ($i -gt 8 -and ($i % 6) -eq 0)) {
      if (Safe-Invoke (Find-Name $win "Reload list" 800)) { $refOk = $true }
    }
    Start-Sleep -Milliseconds 500
  }
  if (-not $refOk) { $refOk = Safe-Invoke (Find-Name $win "Sync take-off" 800) }
  if (-not $refOk) { $refOk = Safe-Invoke (Find-Name $win "Reload list" 800) }
  Check "Refresh" ($refOk -or ($null -ne $row)) $(if ($row) { "list loaded" } elseif ($refOk) { "clicked" } else { "" })
  Check "drawing listed" ($null -ne $row) $(if ($row) { $row.Current.Name })
  if ($row) { [void](Safe-Invoke $row) }
  Start-Sleep -Milliseconds 400
  $readBtn = Find-Name $win ("Read take-off" + $ELL) 3000
  if (-not $readBtn) { $readBtn = Find-Name $win "Read take-off…" 2000 }
  Check "Read take-off" (Safe-Invoke $readBtn)
  Start-Sleep -Seconds 4
  $sum = Find-Like $win "*take-off records read from*" 6000
  Check "import preview summary" ($null -ne $sum) $(if ($sum) { $sum.Current.Name.Substring(0, [Math]::Min(120, $sum.Current.Name.Length)) })
  if ($MinToAdd -gt 0 -and $sum) {
    $m = [regex]::Match($sum.Current.Name, '(\d+)\s+to add')
    $n = if ($m.Success) { [int]$m.Groups[1].Value } else { 0 }
    Check "import plan to add" ($n -ge $MinToAdd) "$n to add (min $MinToAdd)"
  }

  try { [System.Windows.Forms.SendKeys]::SendWait("{ESC}") } catch { }
  $app.Refresh()
  Check "app still running" (-not $app.HasExited)
}
finally {
  try {
    if ($app -and -not $app.HasExited) {
      $app.CloseMainWindow() | Out-Null
      Start-Sleep 1
      if (-not $app.HasExited) { $app.Kill() }
    }
  } catch {}
  $fail = @($results.GetEnumerator() | Where-Object { -not $_.Value }).Count
  "RESULT: $($results.Count - $fail) passed, $fail failed"
  if ($fail -gt 0) { exit 1 }
}
