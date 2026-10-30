# Manual UI checks (desktop session, built app)

These are **not** run in CI. They exercise File menu flows after a local build.

```powershell
powershell -File BBSDesktop\tests\e2e\run-e2e.ps1
# Fixture .osp (take-off already in file):
powershell -File BBSDesktop\tests\e2e\uia-osp-takeoff.ps1 -Exe $exe -Osp (Resolve-Path BBSDesktop\Office.Shared.Tests\Fixtures\small.osp)

# Fresh .osp: O-Draft engine takeoff.record → O-CoreOp import (needs O-DRAFT-main beside repo):
powershell -File BBSDesktop\tests\e2e\e2e-draft-coreop-takeoff.ps1
# Engine only (skip O-Draft WinUI launch): -SkipDraftWinUi
```

**Take-off:** pass the `.osp` on the command line (as the script does) or **File › Open project…**, then **File › Take-off (O-Draft)…**.

**Draft → CoreOp:** `seed-fresh-osp.ps1` runs `scripts/e2e-house-takeoff.scr` plus `takeoff.record` via `aad-cli` (same command as WinUI **Architecture › Take-off › Record**). `e2e-draft-coreop-takeoff.ps1` builds `aad_host.dll` if needed, opens the same `.osp` in **O-Draft WinUI**, then runs the CoreOp UIA script with `-MinToAdd 1`.

**`bbs_engine.dll`:** Visual Studio 2022 Build Tools with C++ workload; from `BBSDesktop`: `vcvars64.bat` then `cmake -B build -G Ninja` and `cmake --build build`. Copy `build\bbs_engine.dll` next to `O-CoreOp.exe` ( `run-e2e.ps1` does this when the DLL exists).

**CAD:** **Import from CAD** / **Export for CAD** with a real `hcw-aqc-bridge` export (see `docs/bridge/`).
