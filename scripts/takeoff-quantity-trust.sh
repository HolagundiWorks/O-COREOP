#!/usr/bin/env bash
# Quantity-trust CadImport checks when `dotnet` is available (CI / Windows / wine).
# See also takeoff-quantity-trust.ps1 and the OSAS store runbook takeoff-e2e-runbook.md.

set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
PROJ="BBSDesktop/AQC.CadImport.Tests/AQC.CadImport.Tests.csproj"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet not found; run scripts/takeoff-quantity-trust.ps1 on Windows CI" >&2
  exit 1
fi

echo "== QuantityTrust (CadImport) =="
dotnet test "$PROJ" --filter "FullyQualifiedName~QuantityTrust" --nologo

echo "== CadImport neighbours =="
dotnet test "$PROJ" --filter "FullyQualifiedName~CadImporter|FullyQualifiedName~ShilpiTakeoff|FullyQualifiedName~VdbFile|FullyQualifiedName~QuantityTrust" --nologo

echo
echo "CoreOp quantity-trust harness done."
echo "WinUI product E2E still needs a Windows machine (takeoff-e2e-runbook.md)."
