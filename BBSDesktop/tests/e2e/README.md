# End-to-end checks that drive the real app

These are **manual** checks (they need a desktop session, the built app and, for the take-off ones, the ShilpiDB programs); they are not run by CI.
They drive O-CoreOp through Windows UI Automation, so no screenshots are involved, and they found real bugs that the unit tests could not
(see "Debug pass 2026-10-19" in `docs/HANDOFF-O-COREOP.md`).

| Script | What it does |
|---|---|
| `uia-smoke.ps1` | Starts the app, lists the File menu, opens Take-off / Import CAD / Export CAD dialogs, checks the app is still alive |
| `uia-takeoff.ps1` | Starts a shared take-off server holding the records in `AQC.CadImport.Tests/fixtures/odraft-takeoff-records.json` (what O-Draft really wrote), opens the Take-off page, reads a drawing, fetches an annotated PDF if `annotated-sample.pdf` sits beside the fixture |
| `uia-vdbfile.ps1` | Opens the Take-off page, switches to "A take-off file (.vdb)", types a file path into the Windows file dialog, reads the drawing and checks the import preview. Used with the file O-Draft's own engine wrote (`AQC.CadImport.Tests/fixtures/odraft-parametric.takeoff.vdb`, format version 2); 9 checks |

AORMS Connect sync e2e (`uia-sync-e2e.ps1` / FakeAorms) was removed with the Connect/Bridge connectors.
