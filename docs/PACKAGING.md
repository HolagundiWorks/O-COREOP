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
  `AZURE_SIGNING_ACCOUNT`, `AZURE_CERT_PROFILE`, endpoint/region)
- `SIGN_SELF_SIGNED=1` for local smoke tests only

Never commit production PFX files or invent certificates in CI.

## Suite glue

The OSAS launcher can install this Setup.exe via
`installer/install-suite.ps1 -ArtifactsDir …` when the file is named
`O-CoreOp-Setup-*.exe`. ShilpiDB binaries may be staged from
`…/OSAS…/installer/suite-tools` or `%LOCALAPPDATA%\Programs\OSAS\shilpidb`.

## Update channel (check-only)

Shared schema lives in the OSAS repo (`installer/channel/osas-channel.schema.json`).
Package id for this app: **`o-coreop`**.

Set `OSAS_UPDATE_CHANNEL_URL` to a hosted channel JSON. Help → **Check for updates…**
compares the installed version to the channel and does **not** download or apply
updates yet.

## CI notes

- Prefer a Windows runner for Inno + MSIX jobs.
- Upload unsigned Setup.exe as `O-CoreOp-Setup-<ver>-unsigned.exe` when secrets are absent.
- Do not fail packaging solely because signing material is missing.
- Full suite ops checklist: Project doc `suite-installer-signing.md`.
