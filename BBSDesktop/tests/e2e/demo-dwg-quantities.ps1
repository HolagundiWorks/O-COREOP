# DEMO.dwg → O-Draft take-off → shared .osp → CoreOp import plan (headless).
param(
  [string]$Dwg = "$env:USERPROFILE\OneDrive\Documents\OSAS TEST\DEMO.dwg",
  [string]$Osp = "$env:TEMP\demo-dwg.osp",
  [string]$ODraftRoot = "D:\Work Development\Repos\O-DRAFT-main",
  [switch]$LaunchApps
)
$ErrorActionPreference = "Stop"
$coreRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

if (-not (Test-Path -LiteralPath $Dwg)) {
  throw "DEMO.dwg not found at `$Dwg. Copy the OSAS demo drawing there or pass -Dwg."
}

$cli = Join-Path $ODraftRoot "target\debug\aad-cli.exe"
$env:CARGO_TARGET_DIR = Join-Path $ODraftRoot "target"
if (-not (Test-Path $cli)) {
  Push-Location $ODraftRoot
  try { cmd /c "cargo build -p aad-cli 2>nul" } finally { Pop-Location }
}
$scr = Join-Path $ODraftRoot "scripts\demo-dwg-takeoff.scr"
if (-not (Test-Path $scr)) { throw "Missing $scr" }
$ospFull = [System.IO.Path]::GetFullPath($Osp)
Remove-Item -LiteralPath $ospFull -ErrorAction SilentlyContinue
$esc = ($ospFull -replace '\\', '\\')
$runScr = Join-Path $env:TEMP "demo-dwg-run.scr"
$utf8 = New-Object System.Text.UTF8Encoding $false
$body = [IO.File]::ReadAllText($scr).TrimEnd() + "`ntakeoff.record {`"path`":`"$esc`",`"annotate`":false}`n"
[IO.File]::WriteAllText($runScr, $body, $utf8)

Write-Host "=== O-Draft (aad-cli): $Dwg ==="
$draftOut = cmd /c "`"$cli`" run `"$Dwg`" --script-file `"$runScr`" 2>&1"
$draftOut | Select-String -Pattern 'Take-off recorded|BRICKWORK|SLABS|totals|written' | ForEach-Object { $_.Line }

Write-Host "`n=== O-CoreOp (CadImport plan): $ospFull ==="
$env:DEMO_OSP_PATH = $ospFull
Push-Location (Join-Path $coreRoot "AQC.CadImport.Tests")
try {
  dotnet test -c Debug --filter "FullyQualifiedName~DemoDwgQuantityTests" --logger "console;verbosity=detailed" 2>&1 |
    Select-String -Pattern 'Drawing:|kind |Bridge |to add|Passed|Failed|Skip:'
} finally {
  Pop-Location
  Remove-Item Env:DEMO_OSP_PATH -ErrorAction SilentlyContinue
}

if ($LaunchApps) {
  $draftExe = "$env:TEMP\odraft\ODraft.WinUi.exe"
  $coreExe = "$env:TEMP\ocoreop\O-CoreOp.exe"
  if (Test-Path $draftExe) { Start-Process $draftExe -ArgumentList $ospFull }
  if (Test-Path $coreExe) { Start-Process $coreExe -ArgumentList $ospFull }
  Write-Host "Launched WinUI with $ospFull — use File > Take-off (O-Draft) in CoreOp."
}
