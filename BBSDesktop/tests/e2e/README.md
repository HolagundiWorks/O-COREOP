# End-to-end checks that drive the real app

These are **manual** checks (they need a desktop session, the built app and, for the take-off ones, the ShilpiDB programs); they are not run by CI.
They drive O-CoreOp through Windows UI Automation, so no screenshots are involved, and they found real bugs that the unit tests could not
(see "Debug pass 2026-10-19" in `docs/HANDOFF-O-COREOP.md`).

| Script | What it does |
|---|---|
| `uia-smoke.ps1` | Starts the app, lists the File menu, opens each AORMS dialog while signed out, checks the app is still alive |
| `uia-takeoff.ps1` | Starts a shared take-off server holding the records in `AQC.CadImport.Tests/fixtures/odraft-takeoff-records.json` (what O-Draft really wrote), opens the Take-off page, reads a drawing, fetches an annotated PDF if `annotated-sample.pdf` sits beside the fixture |
| `uia-sync-e2e.ps1` | Starts `FakeAorms` and the take-off server, then: sign in (wrong password first), import a take-off, push the project online, change the take-off and import again (one row must reach AORMS by itself), have "another computer" change a row, Pull latest, sign out. 24 checks |
| `FakeAorms/` | A scripted stand-in for the AORMS server (config, login, session, projects, push, lease, rows, settings, catch-up); logs every request to a file |

Run the sync check (PowerShell, from this folder, with the app built and `shilpid.exe` and `shilpi-http.exe` in `%LocalAppData%\ShilpiDB\bin`):

```powershell
dotnet build .\FakeAorms -c Release
.\uia-sync-e2e.ps1 -Exe ..\BBSApp\bin\x64\Debug\net8.0-windows10.0.19041.0\AQCCore.exe `
  -Fixture ..\AQC.CadImport.Tests\fixtures\odraft-takeoff-records.json `
  -FakeExe (Get-ChildItem .\FakeAorms\bin -Recurse -Filter fakeaorms.exe | Select-Object -First 1 -ExpandProperty FullName)
```

**Warnings.** The sync check starts the app with `AORMS_URL` pointing at the fake, signs in with a throwaway account (the password is `test-password`), and so writes
the shared sign-in to Windows Credential Manager (`AORMS/Connect`, `AORMS/Connect/aqc`). It deletes both entries at the end. **Do not run it if you are signed in
to the real AORMS on this machine**: it would replace your sign-in. It also uses ports 7420, 7421 and 7550. Toasts and data-grid cells are not readable through UI
Automation here, so the checks look at what appears on screen and at what the fake server received.
