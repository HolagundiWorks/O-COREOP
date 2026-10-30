# Build a new .osp with take-off rows (same scene as engine takeoff_record tests / scripts/e2e-house-takeoff.scr).
# Uses O-Draft aad-cli (same takeoff.record command as WinUI Architecture > Take-off > Record).
param(
  [string]$OutOsp = (Join-Path $env:TEMP "e2e-fresh-house.osp"),
  [string]$ODraftRoot
)
$ErrorActionPreference = "Stop"

function Resolve-ODraftRoot {
  param([string]$Hint)
  if ($Hint -and (Test-Path (Join-Path $Hint "Cargo.toml"))) { return (Resolve-Path $Hint).Path }
  $candidates = @(
    (Join-Path $PSScriptRoot "..\..\..\O-DRAFT-main"),
    (Join-Path $PSScriptRoot "..\..\..\..\O-DRAFT-main"),
    "D:\Work Development\Repos\O-DRAFT-main"
  )
  foreach ($c in $candidates) {
    try {
      $p = (Resolve-Path $c -ErrorAction Stop).Path
      if (Test-Path (Join-Path $p "Cargo.toml")) { return $p }
    } catch { }
  }
  throw "Set -ODraftRoot to your O-DRAFT-main clone (needs apps/aad-cli and scripts/e2e-house-takeoff.scr)."
}

$ODraftRoot = Resolve-ODraftRoot $ODraftRoot
$targetDir = Join-Path $ODraftRoot "target"
$env:CARGO_TARGET_DIR = $targetDir
$cli = Join-Path $targetDir "debug\aad-cli.exe"
$baseScr = Join-Path $ODraftRoot "scripts\e2e-house-takeoff.scr"

if (-not (Test-Path $cli)) {
  Push-Location $ODraftRoot
  try {
    $null = cmd /c "cargo build -p aad-cli 2>&1"
    if ($LASTEXITCODE -ne 0) { throw "cargo build -p aad-cli failed ($LASTEXITCODE)" }
  } finally { Pop-Location }
}
if (-not (Test-Path $cli)) { throw "Missing $cli after build." }
if (-not (Test-Path $baseScr)) { throw "Missing $baseScr" }

$dir = Split-Path -Parent $OutOsp
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$OutOsp = [System.IO.Path]::GetFullPath($OutOsp)
if (Test-Path -LiteralPath $OutOsp) { Remove-Item -LiteralPath $OutOsp -Force }

$esc = ($OutOsp -replace '\\', '\\')
$runScr = Join-Path $env:TEMP ("e2e-house-takeoff-{0}.scr" -f [guid]::NewGuid().ToString("n"))
$utf8 = New-Object System.Text.UTF8Encoding $false
$body = [IO.File]::ReadAllText($baseScr).TrimEnd()
$body += "`ntakeoff.record {`"path`":`"$esc`",`"annotate`":false}`n"
[IO.File]::WriteAllText($runScr, $body, $utf8)

Push-Location $ODraftRoot
try {
  & $cli run --metric --script-file $runScr
  if ($LASTEXITCODE -ne 0) { throw "aad-cli takeoff.record failed ($LASTEXITCODE)" }
} finally {
  Pop-Location
  Remove-Item -LiteralPath $runScr -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $OutOsp)) { throw "Expected $OutOsp" }
$info = Get-Item -LiteralPath $OutOsp
"Wrote $($info.FullName) ($($info.Length) bytes)"
