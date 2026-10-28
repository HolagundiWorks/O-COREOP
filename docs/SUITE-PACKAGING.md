# O-CoreOp packaging (suite)

**Status:** Updated after AORMS connector removal · **Updated:** 2026-10-28

**This repo** is the engine and WinUI app SoT: `bbs_engine`, `AQC.CadImport`, `Office.Shared`, reference `BBSApp` (assembly `O-CoreOp`).

Earlier Bridge-only stub hosts (`AQC.BBS`, `AQC.Estimation`, `AQC.PM`) and the shared `Aorms.Bridge` library were removed. Product-specific installers, if revived, should pin this repo and own their own UI without Bridge.

```bat
dotnet build BBSDesktop\BBSApp\BBSApp.csproj -c Release -p:Platform=x64 -p:WindowsPackageType=None
```

MSIX identities and WinUI domain screens remain follow-on work.
