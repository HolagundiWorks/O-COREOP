# Build O-CoreOp to $OutDir (if needed) and run UIA e2e scripts.
param(
  [string]$OutDir = "$env:TEMP\ocoreop",
  [string]$Osp = (Resolve-Path "$PSScriptRoot\..\..\Office.Shared.Tests\Fixtures\small.osp").Path,
  [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\..\.."
$proj = Join-Path $root "BBSApp\BBSApp.csproj"
$exe = Join-Path $OutDir "O-CoreOp.exe"

if (-not $SkipBuild) {
  Push-Location $root
  try {
    dotnet build $proj -c Debug -p:Platform=x64 -p:WindowsPackageType=None -o $OutDir | Out-Host
    if (Test-Path (Join-Path $root "build\bbs_engine.dll")) {
      Copy-Item -Force (Join-Path $root "build\bbs_engine.dll") $OutDir
    }
  } finally { Pop-Location }
}

if (-not (Test-Path -LiteralPath $exe)) { throw "Missing $exe" }

& "$PSScriptRoot\uia-smoke.ps1" -Exe $exe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& "$PSScriptRoot\uia-osp-takeoff.ps1" -Exe $exe -Osp $Osp
exit $LASTEXITCODE
