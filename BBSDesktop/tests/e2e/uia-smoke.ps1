param([string]$Exe, [string]$Step = "all")
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
$ErrorActionPreference = 'Continue'
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$PC = [System.Windows.Automation.PropertyCondition]
$root = $AE::RootElement

function Find-Name($parent, [string]$name, $scope = $TS::Descendants, [int]$timeoutMs = 4000) {
  $cond = New-Object $PC($AE::NameProperty, $name)
  $sw = [Diagnostics.Stopwatch]::StartNew()
  do {
    $e = $parent.FindFirst($scope, $cond)
    if ($e) { return $e }
    Start-Sleep -Milliseconds 150
  } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
  return $null
}
function Names($parent, $scope = $TS::Descendants, [int]$max = 80) {
  $all = $parent.FindAll($scope, [System.Windows.Automation.Condition]::TrueCondition)
  $out = @()
  foreach ($e in $all) { $n = $e.Current.Name; if ($n -and $n.Length -lt 160) { $out += "$($e.Current.ControlType.ProgrammaticName.Replace('ControlType.','')): $n" } }
  return $out | Select-Object -Unique -First $max
}
function Invoke-El($e) {
  $p = $null
  if ($e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$p)) { $p.Invoke(); return $true }
  if ($e.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$p)) { $p.Expand(); return $true }
  if ($e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { $p.Select(); return $true }
  return $false
}

$proc = Start-Process -FilePath $Exe -PassThru
$win = $null
for ($i = 0; $i -lt 60 -and -not $win; $i++) {
  Start-Sleep -Milliseconds 500
  $proc.Refresh()
  if ($proc.HasExited) { "APP EXITED at start, code $($proc.ExitCode)"; exit 1 }
  $c = New-Object $PC($AE::ProcessIdProperty, $proc.Id)
  $win = $root.FindFirst($TS::Children, $c)
}
if (-not $win) { "no window found"; exit 1 }
"WINDOW: '$($win.Current.Name)'"
Start-Sleep -Seconds 2

function Open-FileMenu() {
  $file = Find-Name $win "File"
  if (-not $file) { "File menu NOT FOUND"; return $false }
  [void](Invoke-El $file)
  Start-Sleep -Milliseconds 500
  return $true
}
function Click-MenuItem([string]$text) {
  if (-not (Open-FileMenu)) { return $false }
  # flyout items live in a popup window owned by the app
  $item = Find-Name $root $text $TS::Descendants 3000
  if (-not $item) { "MENU ITEM NOT FOUND: $text"; return $false }
  [void](Invoke-El $item)
  Start-Sleep -Milliseconds 900
  return $true
}
function Dialog-Report([string]$label) {
  Start-Sleep -Milliseconds 700
  $dlgs = $win.FindAll($TS::Descendants, (New-Object $PC($AE::ControlTypeProperty, $CT::Window)))
  "--- after '$label': window children=$(($win.FindAll($TS::Children,[System.Windows.Automation.Condition]::TrueCondition)).Count)"
  $d = Find-Name $root "Cancel" $TS::Descendants 1500
  if ($d) { "dialog present; its text:"; Names $d.GetUpdatedCache([System.Windows.Automation.CacheRequest]::Current).Parent 20 | ForEach-Object { "   $_" } }
}
function Alive() { $proc.Refresh(); if ($proc.HasExited) { "!!! APP CRASHED (exit $($proc.ExitCode))"; return $false }; return $true }

"== File menu items =="
[void](Open-FileMenu)
$items = Names $root $TS::Descendants 200 | Where-Object { $_ -like "MenuItem*" }
$items | ForEach-Object { "  $_" }
try { [System.Windows.Forms.SendKeys]::SendWait("{ESC}") } catch { }
Start-Sleep -Milliseconds 400

foreach ($step in @("Take-off (O-Draft)", "Import from CAD", "Export for CAD")) {
  "== $step =="
  $ok = Click-MenuItem ($items | Where-Object { $_ -like "*$step*" } | ForEach-Object { ($_ -split ': ', 2)[1] } | Select-Object -First 1)
  if ($ok) {
    Start-Sleep -Milliseconds 1200
    $txt = Names $win $TS::Descendants 60
    ($txt | Where-Object { $_ -match "Take-off|Import|Export|CAD|Drawings|Cancel|Close|Refresh|Read|Start|Choose|shared|first|bridge" } | Select-Object -First 25) | ForEach-Object { "   $_" }
  }
  try { [System.Windows.Forms.SendKeys]::SendWait("{ESC}") } catch { Invoke-El (Find-Name $win "Close" 1500) | Out-Null }
  Start-Sleep -Milliseconds 500
  if (-not (Alive)) { break }
}
"== final =="
if (Alive) { "app still running" }
try { if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep 2; if (-not $proc.HasExited) { $proc.Kill() } } } catch {}
