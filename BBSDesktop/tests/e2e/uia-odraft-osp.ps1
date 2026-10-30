# Launch O-Draft WinUI with a .osp on the command line (shared project file smoke).
param(
  [Parameter(Mandatory = $true)][string]$Exe,
  [Parameter(Mandatory = $true)][string]$Osp
)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$PC = [System.Windows.Automation.PropertyCondition]
$root = $AE::RootElement
$results = [ordered]@{}
function Check($name, $ok, $detail = "") {
  $results[$name] = [bool]$ok
  "{0,-6} {1} {2}" -f $(if ($ok) { "PASS" } else { "FAIL" }), $name, $detail
}

$Osp = (Resolve-Path -LiteralPath $Osp).Path
if (-not (Test-Path -LiteralPath $Osp)) { throw "Missing $Osp" }

$app = Start-Process -FilePath $Exe -ArgumentList @($Osp) -PassThru
try {
  $win = $null
  for ($i = 0; $i -lt 80 -and -not $win; $i++) {
    Start-Sleep -Milliseconds 500
    $app.Refresh()
    if ($app.HasExited) { Check "O-Draft starts" $false "exit $($app.ExitCode)"; break }
    $win = $root.FindFirst($TS::Children, (New-Object $PC($AE::ProcessIdProperty, $app.Id)))
  }
  Check "O-Draft starts" ($win -ne $null)
  Start-Sleep -Seconds 4
  $name = if ($win) { $win.Current.Name } else { "" }
  Check "main window" ($name.Length -gt 0) $name
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
