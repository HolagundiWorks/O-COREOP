# O-CoreOp packaging and signing

## Installers

| Kind | Script | Output |
|---|---|---|
| Per-user Setup.exe (Inno Setup 6) | `BBSDesktop/installer/build-installer.ps1` | `BBSDesktop/artifacts/installer/O-CoreOp-Setup-<ver>.exe` |
| MSIX (sideload / Store) | `BBSDesktop/installer/build-msix.ps1` | `BBSDesktop/artifacts/msix/*.msix` |

Inno Setup installs to `%LOCALAPPDATA%\Programs\O-CoreOp\` (no admin). The Inno
**AppId** stays `{A8F3C2E1-…}` so upgrades replace older AQC-Core installs.

MSIX signing today uses a **local self-signed** certificate
(`AQCCore_TemporaryKey.pfx` / “AQC-Core Dev (MSIX)”) for sideload only. Store
uploads are re-signed by Partner Center — do not treat the repo PFX as a
production Authenticode identity.

## Authenticode for Setup.exe

After `build-installer.ps1` succeeds it calls `BBSDesktop/installer/sign.ps1` on
the Setup.exe. Without secrets the script **warns and leaves the file unsigned**
(exit 0). Configure one of:

- `WINDOWS_CERTIFICATE` (base64 PFX) + optional `WINDOWS_CERTIFICATE_PASSWORD`
- Azure Trusted Signing (`AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`,
  `AZURE_SIGNING_ACCOUNT`, `AZURE_CERT_PROFILE`, plus `AZURE_SIGNING_ENDPOINT` **or**
  `AZURE_SIGNING_REGION`)
- `SIGN_SELF_SIGNED=1` for local smoke tests only

When Azure secrets are set and the Trusted Signing client is not cached, `sign.ps1`
downloads `Microsoft.Trusted.Signing.Client` from NuGet.

Never commit production PFX files or invent certificates in CI.

### Local unsigned vs signed smoke

```powershell
# Unsigned (default)
powershell -File BBSDesktop\installer\build-installer.ps1
Get-AuthenticodeSignature BBSDesktop\artifacts\installer\O-CoreOp-Setup-*.exe

# Dev self-sign only
$env:SIGN_SELF_SIGNED = '1'
powershell -File BBSDesktop\installer\sign.ps1 BBSDesktop\artifacts\installer\O-CoreOp-Setup-1.1.0.exe
```

## Suite glue

The OSAS launcher can install this Setup.exe via
`installer/install-suite.ps1 -ArtifactsDir …` when the file is named
`O-CoreOp-Setup-*.exe`. The suite installer stages O-Studio, O-CoreOp, and O-Draft; see `docs/SUITE-PACKAGING.md`.

## Update channel (check-only)

Shared schema lives in the OSAS repo (`installer/channel/osas-channel.schema.json`).
Package id for this app: **`o-coreop`**.

Set `OSAS_UPDATE_CHANNEL_URL` to a hosted channel JSON. Help → **Check for updates…**
compares the installed version to the channel and does **not** download or apply
updates yet.

## CI

- App CI: `.github/workflows/ci.yml` (build/test only).
- Packaging: `.github/workflows/packaging-windows.yml` (`workflow_dispatch` or tag `v*`)
  installs Inno, runs `build-installer.ps1`, maps the shared signing secret names,
  uploads `O-CoreOp-Setup-<ver>.exe` or `…-unsigned.exe`.
- Do not fail packaging solely because signing material is missing.
- Once org/repo secrets exist, re-run packaging — signing turns on with no code change.
- Full suite ops checklist (Azure vs EV enrollment for Vishwabhiram): Project doc
  `suite-installer-signing.md`.
