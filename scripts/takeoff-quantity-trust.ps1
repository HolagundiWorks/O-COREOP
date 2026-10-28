# Quantity-trust CadImport checks (Windows / CI).
# Mirrors the O-Draft Linux harness half for CoreOp: known wall/opening checklist
# through bridge + Shilpi records → CadImport assertions.
#
# Depends on QuantityTrustTests (O-CoreOp PR #26).
# Full product E2E (WinUI Draft → shared shilpid → CoreOp Take-off page + PDF)
# is in the OSAS store runbook docs/takeoff-e2e-runbook.md — not automated here.
#
# Usage (from repo root):
#   pwsh -File scripts/takeoff-quantity-trust.ps1
#   pwsh -File scripts/takeoff-quantity-trust.ps1 -Configuration Release

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$proj = Join-Path $Root "BBSDesktop/AQC.CadImport.Tests/AQC.CadImport.Tests.csproj"
if (-not (Test-Path $proj)) {
    throw "CadImport.Tests project not found at $proj"
}

Write-Host "== QuantityTrust (CadImport) =="
dotnet test $proj -c $Configuration --filter "FullyQualifiedName~QuantityTrust" --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "== CadImport suite (regression neighbours) =="
dotnet test $proj -c $Configuration --filter "FullyQualifiedName~CadImporter|FullyQualifiedName~ShilpiTakeoff|FullyQualifiedName~VdbFile|FullyQualifiedName~ODraftOutput|FullyQualifiedName~ParametricWall|FullyQualifiedName~QuantityTrust" --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "CoreOp quantity-trust harness done."
Write-Host "Still needs a Windows GUI machine (takeoff-e2e-runbook.md):"
Write-Host "  - Draft WinUI measure → Record in ShilpiDB"
Write-Host "  - CoreOp Take-off Read + Fetch annotated PDF + report embed"
Write-Host "  - QS audit of expected_face_m2 (32.67) on a real project"
Write-Host "  - Signing / clean install"
