# O-Draft takeoff.record (aad-cli, same engine as WinUI) → shared .osp → O-CoreOp Take-off import UIA.
param(
  [string]$OutDir = "$env:TEMP\ocoreop",
  [string]$ODraftRoot,
  [string]$DraftOutDir = "$env:TEMP\odraft",
  [string]$Osp = (Join-Path $env:TEMP "e2e-fresh-house.osp"),
  [int]$MinToAdd = 1,
  [switch]$SkipBuild,
  [switch]$SkipDraftWinUi
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$coreRoot = Resolve-Path (Join-Path $here "..\..")

function Get-ODraftRoot([string]$Hint) {
  if ($Hint -and (Test-Path (Join-Path $Hint "Cargo.toml"))) { return (Resolve-Path $Hint).Path }
  foreach ($c in @(
      (Join-Path $here "..\..\..\O-DRAFT-main"),
      (Join-Path $here "..\..\..\..\O-DRAFT-main"),
      "D:\Work Development\Repos\O-DRAFT-main"
    )) {
    try {
      $p = (Resolve-Path $c -ErrorAction Stop).Path
      if (Test-Path (Join-Path $p "Cargo.toml")) { return $p }
    } catch { }
  }
  throw "Set -ODraftRoot to O-DRAFT-main."
}

& (Join-Path $here "seed-fresh-osp.ps1") -OutOsp $Osp -ODraftRoot $ODraftRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipDraftWinUi) {
  $odRoot = Get-ODraftRoot $ODraftRoot
  $env:CARGO_TARGET_DIR = Join-Path $odRoot "target"
  $hostDll = Join-Path $env:CARGO_TARGET_DIR "debug\aad_host.dll"
  if (-not (Test-Path $hostDll)) {
    Push-Location $odRoot
    try {
      $null = cmd /c "cargo build -p aad-host 2>&1"
      if ($LASTEXITCODE -ne 0) { throw "cargo build -p aad-host failed ($LASTEXITCODE)" }
    } finally { Pop-Location }
  }
  $draftProj = Join-Path $odRoot "apps\aad-winui\Aad.WinUi.csproj"
  $draftExe = Join-Path $DraftOutDir "ODraft.WinUi.exe"
  if (-not $SkipBuild) {
    if (-not (Test-Path $DraftOutDir)) { New-Item -ItemType Directory -Path $DraftOutDir -Force | Out-Null }
    dotnet build $draftProj -c Debug -p:Platform=x64 -p:WindowsPackageType=None -o $DraftOutDir | Out-Host
  }
  if (-not (Test-Path $draftExe)) { throw "Missing $draftExe (build O-Draft WinUI or pass -SkipDraftWinUi)" }
  if (-not (Test-Path (Join-Path $DraftOutDir "aad_host.dll"))) {
    throw "Missing aad_host.dll beside ODraft.WinUi.exe — run: cargo build -p aad-host in O-DRAFT-main"
  }
  & (Join-Path $here "uia-odraft-osp.ps1") -Exe $draftExe -Osp $Osp
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$coreProj = Join-Path $coreRoot "BBSApp\BBSApp.csproj"
$coreExe = Join-Path $OutDir "O-CoreOp.exe"
if (-not $SkipBuild) {
  if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
  dotnet build $coreProj -c Debug -p:Platform=x64 -p:WindowsPackageType=None -o $OutDir | Out-Host
  $engine = Join-Path $coreRoot "build\bbs_engine.dll"
  if (Test-Path $engine) { Copy-Item -Force $engine $OutDir }
}
if (-not (Test-Path $coreExe)) { throw "Missing $coreExe" }

& (Join-Path $here "uia-osp-takeoff.ps1") -Exe $coreExe -Osp $Osp -MinToAdd $MinToAdd
exit $LASTEXITCODE
