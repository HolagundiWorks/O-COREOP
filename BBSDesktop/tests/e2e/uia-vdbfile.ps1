param([string]$Exe, [string]$Vdb)
# Opens the Take-off page, switches to "A take-off file (.vdb)", picks $Vdb in the file dialog, reads the drawing and shows the import preview.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
$AE = [System.Windows.Automation.AutomationElement]; $TS = [System.Windows.Automation.TreeScope]; $PC = [System.Windows.Automation.PropertyCondition]
$root = $AE::RootElement
$ELL = [string][char]0x2026
$results = [ordered]@{}
function Check($name, $ok, $detail = "") { $results[$name] = [bool]$ok; "{0,-6} {1} {2}" -f $(if ($ok) { "PASS" } else { "FAIL" }), $name, $detail }
function Find-Name($parent, [string]$name, [int]$timeoutMs = 4000) {
  $cond = New-Object $PC($AE::NameProperty, $name); $sw = [Diagnostics.Stopwatch]::StartNew()
  do { $e = $parent.FindFirst($TS::Descendants, $cond); if ($e) { return $e }; Start-Sleep -Milliseconds 150 } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
  return $null
}
function Find-Like($parent, [string]$pattern, [int]$timeoutMs = 4000) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  do { foreach ($e in $parent.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) { if ($e.Current.Name -like $pattern) { return $e } }; Start-Sleep -Milliseconds 250 } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
  return $null
}
function Invoke-El($e) {
  if (-not $e) { return $false }
  $p = $null
  if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return $true }
  if ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return $true }
  return $false
}
$app = Start-Process -FilePath $Exe -PassThru
try {
  $win = $null
  for ($i = 0; $i -lt 60 -and -not $win; $i++) { Start-Sleep -Milliseconds 500; $win = $root.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $app.Id))) }
  Check "app starts" ($win -ne $null); Start-Sleep -Seconds 2
  [void](Invoke-El (Find-Name $win "File")); Start-Sleep -Milliseconds 500
  [void](Invoke-El (Find-Name $root ("Take-off from ShilpiDB" + $ELL))); Start-Sleep -Seconds 2
  Check "switch to the file option" (Invoke-El (Find-Name $win "A take-off file (.vdb)")); Start-Sleep -Milliseconds 800
  Check "choose button" (Invoke-El (Find-Name $win ("Choose take-off file" + $ELL))); Start-Sleep -Seconds 2
  # the common file dialog: type the path into "File name" and press Open
  $dlg = $null
  for ($i = 0; $i -lt 20 -and -not $dlg; $i++) { $dlg = $root.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, "Open"))); Start-Sleep -Milliseconds 300 }
  $fn = Find-Name $root "File name:" 4000
  Check "file dialog is open" ($null -ne $fn)
  if ($fn) {
    # the common file dialog opens with the File name box focused: type the path (SendKeys needs + ^ % ~ ( ) { } escaped)
    $keys = [regex]::Replace($Vdb, '([+^%~(){}\[\]])', '{$1}')
    Start-Sleep -Milliseconds 600
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
  }
  Start-Sleep -Seconds 4
  $row = Find-Like $win "*complete*" 6000
  Check "the drawing from O-Draft's own file is listed" ($null -ne $row) $(if ($row) { $row.Current.Name })
  Check "no version error" ($null -eq (Find-Like $win "*unsupported*" 800))
  [void](Invoke-El (Find-Name $win ("Read take-off" + $ELL))); Start-Sleep -Seconds 3
  $sum = Find-Like $win "*take-off records read from*" 4000
  Check "reading it gives the import preview summary" ($null -ne $sum) $(if ($sum) { $sum.Current.Name.Substring(0, [Math]::Min(170, $sum.Current.Name.Length)) })
  Check "one row to add (the parametric wall)" ($sum -and $sum.Current.Name -match "\b[1-9]\d* to add")
  [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
  $app.Refresh(); Check "app still running" (-not $app.HasExited)
}
finally {
  try { if ($app -and -not $app.HasExited) { $app.CloseMainWindow() | Out-Null; Start-Sleep 1; if (-not $app.HasExited) { $app.Kill() } } } catch {}
  $fail = @($results.GetEnumerator() | Where-Object { -not $_.Value }).Count
  "RESULT: $($results.Count - $fail) passed, $fail failed"
}
