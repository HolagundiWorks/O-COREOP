# Hand-off: what is left in O-CoreOp

Written 2026-10-19 for whoever picks up O-CoreOp next. O-CoreOp is the quantities, estimates and billing app of OSAS, the Open Source Architecture Suit. It is this repository (`BBSDesktop/BBSApp`, WinUI 3, with the C++ `bbs_engine.dll`).

Each item says how it is known. **Checked** means it was read in the code or run. **Not tested** means nobody has tried it. Where an item is also on the suite roadmap, the number is from `docs/roadmap.md` in the O-STUDIO repository.

## 1. Where it stands on `main`

Merged on 2026-10-19 (merge commit `a0aa37d`):

- The OSAS shell: menu bar, grouped left navigation pane, work area, status strip. The pane is open from 900 px, an icon rail from 560 px, and a menu button below that. See `docs/WINUI-SHELL.md`.
- The shared `Hcw.Ui` design (blue accent), referenced from a sibling checkout of the private `hcw-ui` repository (`..\..\..\hcw-ui` from the app folder). The build fails without it.
- Accessibility fixes from the WCAG 2.2 AA audit: names on sheet cells and icon buttons, targets of 24 px or more, headings, a polite notice host, 1x scale.
- The rename to O-CoreOp. The program is still `AQCCore.exe`, the installer's AppId and the `aqc` AORMS session name are unchanged, on purpose.
- AORMS sign-in (Credential Manager stores, status badge, File > Sign in / Sign out, take-over prompt) and File > Online projects (list the studio's projects, link one, import CAD take-off).
- CI (`.github/workflows/ci.yml`): the import and sign-in tests, and a build.

Also on `main` since 2026-10-19 (fast-forward to `b67710a`; the branch `feat/company-live-sync` was then deleted):

- The README and `docs/WINUI-SHELL.md` rewrite to the current names.
- **Shared company file and OSAS hub** (section 5.1): O-CoreOp has no company picker. It opens the company file OSAS chose (`OSAS_COMPANY`), signed in by OSAS's one-time ticket (`OSAS_TICKET`, `OSAS_USER`), and shows a project saved in O-Studio within about two seconds, and the reverse. Code: `BBSDesktop/Office.Shared` (`OfficeSession`, `SharedCompany`, 5 tests), `BBSApp/MainWindow.Company.cs`, `Services/Company/CompanyService.cs`, the `Shared` link in `Services/ProjectStore.cs`. The engine is `office.dll`, copied beside the app from the O-STUDIO build (`OfficeDllDir`).
- The project sync host, take-off reading from O-Draft, annotated drawings in reports, and the overlap warning (progress log below).

## 2. Do first: make what exists trustworthy

| # | Item | Evidence | Where to look |
|---|---|---|---|
| 2.1 | Add the repository secret `HCW_UI_TOKEN` (a read-only token for `HolagundiWorks/hcw-ui`). Without it the CI build job cannot clone the design library and fails | Checked: the workflow reads `secrets.HCW_UI_TOKEN`; the first run on GitHub has not been looked at | `.github/workflows/ci.yml` |
| 2.2 | Sign in to the real AORMS portal and pull a project. Everything in `Aorms.Connect` was tested against a fake server and a local development server | Not tested live (`docs/ROADMAP.md`, "CAD take-off and AORMS sign-in") | `docs/AORMS-CONNECT-HANDOFF.md`, `BBSDesktop/Aorms.Connect.Smoke` |
| 2.3 | The portal needs migration `0113_aqc_sessions_aos_app.sql` applied before the suite apps can hold a session. It is on the branch `claude/aos-suite-session` of the portal repository | Checked: not applied | portal repository, `web/supabase/migrations/` |
| 2.4 | Click through File > Import from CAD, File > Export for CAD and File > Online projects with a real drawing export. The libraries have 20 and 80 tests; the app screens have not been used | Not tested | `BBSDesktop/AQC.CadImport`, `docs/bridge/` |
| 2.5 | Install on a **clean** machine with the Inno Setup installer and check the Start menu entry reads O-CoreOp. On the development machine (2026-10-19) the installer upgraded an existing copy in place: it still installs to `%LOCALAPPDATA%\Programs\AQC-Core`, not `O-CoreOp`, and the run was silent, so the Start menu text was not looked at | Partly checked | `BBSDesktop/installer/AQCCore.iss`, `build-installer.ps1` |
| 2.6 | ~~Rebuild the installer~~ done (1.1.0, built and installed 2026-10-19 from `main`). **Open:** `build-installer.ps1` does not put `office.dll` in the publish folder unless the O-STUDIO engine is built in its default folder (`architect-office-desktop\office-ffi\target\release`); without it the installed app cannot open a company file. It was copied into `artifacts\publish` by hand. Pass `OfficeDllDir` through the script, or fail the build when the file is missing | Checked | `BBSDesktop/installer/build-installer.ps1` |

## 3. Accessibility (WCAG 2.2 AA)

The full audit is in the O-STUDIO repository, `docs/wcag-audit.md`. For O-CoreOp:

| # | Item | Evidence |
|---|---|---|
| 3.1 | Narrator and NVDA pass over the main tasks: open a project, add a row on an element page, generate a BBS, read the Results tab. This is the only way to confirm the polite notice host works | Not tested |
| 3.2 | Look at every page at 520 px. Only the Dashboard, the element pages (Columns was checked) and Drawing takeoff have been | Checked: three pages |
| 3.3 | 200% text size, 400% display zoom and Windows high contrast | Not tested |
| 3.4 | Walk the keyboard order of the shell and the element sheet (Tab, arrows, Enter between cells) | Not tested |
| 3.5 | In the last automated pass two controls measured under 24 px: a navigation item ("Masonry walls", 3 px high) and a layer toggle ("Show layer A-Wall-SSM", 26 by 6). Both were probably cut off by a scroll area, not really that size. Confirm by scrolling them into view | Checked: measured; cause not confirmed |
| 3.6 | The Pomodoro clock is not shown below 900 px (it lives in the pane footer). Decide if that is acceptable or if the timer needs another home | Checked: by design |
| 3.7 | Text spacing (1.4.12) and the dashboard's use of colour only for status | Not tested |

How the checks were run: a UI Automation script that opens each page, takes a screenshot, and lists every control with its name and size. It lives in the O-STUDIO repository as `tools/a11y/uia-audit.ps1` with `analyse.mjs` to summarise; the version used for the shell fixes is not in either repository yet and should be committed there.

## 4. Finish the AORMS Connect work

From `docs/AORMS-CONNECT-HANDOFF.md`, still open (**Checked**, listed there as not built):

| # | Item |
|---|---|
| 4.1 | ~~A file-backed `IOutboxStore`~~ done (`FileOutboxStore`) |
| 4.2 | The home screen: sign in first, "Continue without signing in" for the Community edition, then Online projects, then Local; open from a seed; Push online |
| 4.3 | ~~`ProjectStore` hooks and the sync timer~~ built: `ProjectSyncHost`, `RowBaseline`, `ProjectMapper.ApplyDelta`, File > Push project online / Pull latest from AORMS (see the progress log). Not yet run against AORMS |
| 4.4 | Map the shared rate books onto `RateBookStore` (the library `RateBooksApi` is done) |
| 4.5 | Remove `Aorms.Bridge`, `Aorms.Bridge.Smoke` and the old Bind/Flush flow once nothing references them. The status strip still has Bind and Flush buttons |
| 4.6 | Two suite apps refreshing the shared token at once, and sign-out clearing the shared entry from the UI, were not checked |

## 5. Join up with the rest of the suite

| # | Item | Roadmap | Notes |
|---|---|---|---|
| 5.1 | ~~Read the shared company file~~ **done 2026-10-19.** Projects are stored in the shared company file (`project_documents`, kind `aqc.project`, revisioned, optimistic `expected_revision`, last 10 revisions kept) and sync live both ways with O-Studio. The company, sign-in and users are chosen and managed in OSAS only. **Open:** clients are still not read from the company file inside O-CoreOp; a protected (encrypted) company file stays single-program; switching company in OSAS does not move an open O-CoreOp | 2.2 | Contract in O-STUDIO `docs/office-abi.md`, `docs/shared-company-file.md`, `docs/suite-hub.md` |
| 5.2 | Produce and read `ProjectRef` and `ArtifactRef`: a BOQ, an estimate and a rate schedule each get an `ArtifactRef` that names the drawing revision it was made from | 2.3 | Contract: `docs/contracts/refs.md` in the O-STUDIO repository (written; implemented in `office-core`; no app uses it yet) |
| 5.3 | Compute and store a SHA-256 for each artifact at each revision | 2.3 | Needed for 5.2 |
| 5.4 | An MCP server around the calculation engine (`aqc.calculate_boq`, `aqc.compare_estimates`), with the same descriptor shape as O-Studio's `office-agent` | 2.5 | Writes are proposals first; today only O-Studio has a server. The in-app assistant (`Services/Ai`) is separate and stays opt-in |
| 5.5 | Estimates and running bills: O-CoreOp calculates, O-Studio shows the result against the fee proposal | 2.4 | Needs 5.1 to 5.3 first |
| 5.6 | O-Draft writes the bridge file (`aqcexport`) and carries a `ProjectRef` in it | (O-Draft repo) | Until then, import is tested only against the sample files in `docs/bridge/` |

## 6. Product work, in rough order

| # | Item |
|---|---|
| 6.1 | Streaming AI replies (first item under "Planned next" in `docs/ROADMAP.md`) |
| 6.2 | Code signing for the installer and an update path (roadmap 3.1) |
| 6.3 | Automated UI tests with UI Automation for the main flows, run in CI (roadmap 3.2) |
| 6.4 | Check quantities and purchase-order output against a real project and add the cases it exposes (roadmap 3.3) |
| 6.5 | ~~Retire the PDF take-off canvas~~ done 2026-10-19: take-off moves to O-Draft and is read from ShilpiDB (`docs/TAKEOFF-SHILPIDB.md`) |
| 6.6 | Pixel-level take-off pre-fill (deferred; needs reliable scale calibration) |
| 6.7 | Narrow-width polish: cramped table columns and any page that was not looked at (3.2) |

## 7. Names and licence

- Remaining old names are in technical specs where they are identifiers or contract names: `docs/AADT.md`, `docs/bridge/AQC-IMPORTER-SPEC.md`, `docs/AORMS-BRIDGE*.md`, `docs/SUITE-PACKAGING.md`, `docs/PROJECT-SYNC.md`, `CLAUDE.md`. Rewrite them when the contracts they describe are next revised.
- `LICENSING.md` and `CONTRIBUTING.md` still name the product **AQC-Core**. Those are the licence terms; changing the licensed product's name is a legal edit. Decide with whoever owns the licence, then change them together (dual AGPL-3.0 and commercial).
- The program is `AQCCore.exe`, the project file is `.bbsproj` and the repository folder is still `AQC` on disk. Keep them unless you also plan a migration; installed copies and saved projects use them.

## 8. Build, test and run

```powershell
# one folder above this repository: git clone <hcw-ui>   (private)
dotnet test BBSDesktop/AQC.CadImport.Tests
dotnet test BBSDesktop/Aorms.Connect.Tests
dotnet build BBSDesktop/BBSApp/BBSApp.csproj -c Debug -p:Platform=x64 -p:WindowsPackageType=None -o $env:TEMP\ocoreop
```

Close the running app before rebuilding to the same folder; Windows keeps `AQCCore.exe` locked. Delete `bin`, `obj`, `artifacts` and `build` any time; they are ignored by git.

## 9. Decisions only the owner can make

1. The licence wording and product name in `LICENSING.md` and `CONTRIBUTING.md` (section 7).
2. Whether the PDF take-off canvas is retired (6.5).
3. Whether the company file or the portal is the system of record when both exist. The proposed rule is in `docs/contracts/refs.md`: the portal id when there is one, otherwise the company file and project id.
4. ~~Whether O-CoreOp may require the shared company file~~ Answered: the company is chosen in OSAS and O-CoreOp follows it. A local `.bbsproj` still works when no company is chosen; decide if that should stay.

## 10. Progress log

**2026-10-19, started from this hand-off:**

- **4.1 done.** `Aorms.Connect/FileOutboxStore.cs`: a file-backed `IOutboxStore`. A save writes a temporary file and swaps it in; an unreadable file is moved aside (`.corrupt-<time>`), never deleted, and `QuarantinedPath` says where. 8 new tests (`FileOutboxStoreTests`); `Aorms.Connect.Tests` is 88 passed. **Not wired yet:** nothing in `BBSApp` creates one (that is 4.3, with the `ProjectStore` hooks); the intended path is `%LocalAppData%\AQCCore\outbox\<aqc project id>.json`.
- **2.6 done.** The release installer builds: `BBSDesktop/artifacts/installer/O-CoreOp-Setup-1.1.0.exe` (51 MB). Not installed or run (2.5 is still open). Two things the script does not tell you:
  - **The engine must be built with MSVC.** On this machine the only compiler on `PATH` was LLVM-MinGW, and `build-installer.ps1` tried it and failed (`std::malloc` / `std::free` without `<cstdlib>` in `src/api/bbs_c_api.cpp`, wide-path `ofstream` in `src/core/Project.cpp`). Built instead with Visual Studio 2022 Build Tools (`vcvars64`, CMake, Ninja, outside the repo) and copied `bbs_engine.dll` to `BBSDesktop/build/`. `bbs_tests.exe`: 0 failures. Fixing the two clang errors would let the script work without Visual Studio.
  - **`hcw-ui` must be built for `win-x64` first.** `dotnet publish -r win-x64` of the app fails with `PRI252: Hcw.Ui.pri not found` until `dotnet build -c Release -p:Platform=x64 -r win-x64` has been run in `hcw-ui/Hcw.Ui`. CI (Release build, no `-r`) does not hit this; the installer script does. Fix in `hcw-ui` (build its own RID output) or have the script pre-build it.

**2026-10-19 (later):**

- **Engine builds with clang/MinGW as well as MSVC.** `src/api/bbs_c_api.cpp` includes `<cstdlib>`; `src/core/Project.cpp` opens files through `std::filesystem::path`; `CMakeLists.txt` sets `PREFIX ""` so MinGW names the library `bbs_engine.dll` (it was `libbbs_engine.dll`). Built with both compilers, `bbs_tests.exe` 0 failures on each. This **removes the need for Visual Studio** in `build-installer.ps1`'s engine step (item 2.6's first note is resolved). The `hcw-ui` win-x64 pre-build note still stands.
- **Annotated drawings in reports.** The stored annotated PDFs (see `docs/TAKEOFF-SHILPIDB.md` section 6) are embedded in the project report and the estimate PDF. Run in a scratch program, not yet in the app with a real project.
- **Roadmap and docs brought up to date** (`docs/ROADMAP.md`: "Backlog" section; README take-off wording).
- **Overlap warning.** Importing a drawing that adds rows on a floor and trade another drawing already supplies now lists "Check before importing" in the preview (once per drawing and floor; a warning, not a refusal). 5 tests; `AQC.CadImport.Tests` is 54 passed.

**2026-10-19 (project sync, 4.3):**

- **Built:** `Aorms.Connect/RowBaseline.cs` (what the server holds, a hash per row, saved as `<aqc id>.base.json` beside the outbox; `Diff` returns the rows to send and delete), `ProjectMapper.ApplyDelta` (merge rows changed elsewhere into a project document; keeps settings, `takeoff`, `last_estimate`), `ProjectStore.MintRowIds` (a stable `_rid` on every take-off row; a copied row gets a new one), `BBSApp/Services/Connect/ProjectSyncHost.cs` (1 s tick, push online, pull latest, status text) and two File menu items. The status chip now reads, for example, `AORMS · Studio · 3 unsent`.
- **Design choice:** the open project is the source of truth for what the user typed, so the host diffs it instead of hooking every edit; the cost is one `ToJson` of the project per second while it is being edited, and only when dirty. A project of 10 000 rows has not been timed (hand-off 6.4 style check is still open).
- **Checked:** 21 new library tests (`Aorms.Connect.Tests` 109 passed) that follow the host's loop against a scripted server: one edit sends one row, a delete is sent as a delete, offline edits survive a restart and go on the next run, a catch-up is applied and does not come back as a local edit, a catch-up with unsent edits asks for review and keeps the head. The app builds and starts. **Not checked:** against the real server, the two dialogs, `MintRowIds` (it lives in the WinUI project, which has no tests), the timing on a large project.
- **Behaviour to know:** a project opened on a computer with no baseline file takes the local file as the baseline and says so in the chip's tooltip; use Pull latest to bring in the online version. If a pull arrives while there are unsent edits to the same rows, the online version wins and the unsent edits to those rows are dropped (the dialog says so). The edit lease is taken automatically; when someone else holds it the project is read-only and the chip says so (editing is not blocked in the UI yet: the rows queue and go when the lease is free).

**2026-10-19 (reading O-Draft's output):**

- O-Draft implemented `takeoff.record` and `takeoff.annotate` (slots, `recording` to `complete`, shared server, chunked PDF) in its repository (committed in part; the slot and annotate work was uncommitted in its `.wt/slots` worktree when read). Its 11 tests pass, and its real-server test passed against an installed `shilpid` (both run here from a copy, so its repository was not touched).
- **Read with this reader, and it agrees:** header, ids, slot (two independent `SlotFor` agree), 7 rows, nothing skipped. **Four gaps** in what O-Draft records (rooms have no level, length or breadth; openings have no `wall_ref`; walls have no thickness; the schedule uses kind `Ventilator`), each handled in O-CoreOp and flagged in the preview, and each sent back to O-Draft as a request ([HANDOFF-O-DRAFT-TAKEOFF.md](HANDOFF-O-DRAFT-TAKEOFF.md) section 8; details in [TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md) section 10).
- Regression fixture `fixtures/odraft-takeoff-records.json` and `ODraftOutputTests` (10 tests); `AQC.CadImport.Tests` is 64 passed.
- **Not checked:** O-Draft's real annotated PDF (its tests use a stand-in plotter), two drawings from O-Draft in one database, a linked project, several floors.

**Debug pass 2026-10-19 (tests, review and driving the real app):**

The app was driven through Windows UI Automation for the first time (`BBSDesktop/tests/e2e/`, see its README), against a real shared take-off server (with what O-Draft really wrote) and a scripted fake AORMS. Sign-in, Online projects, Push online, Pull latest and the Take-off page had never been clicked before. **Bugs found and fixed** (each with a test where the code is testable, and each re-checked in the real app):

| # | Bug | How it showed | Fix |
|---|---|---|---|
| 1 | **Pull latest failed**: the project was rebuilt in memory and handed to `LoadFrom`, which cannot read typed values (`Int32 cannot be converted to Double`); the change was never applied | an error toast after Pull in the real app | the document goes through JSON text first (`ProjectSyncHost.ApplyPull`) |
| 2 | **`ApplyPull` was not atomic**: it dropped outbox entries and adopted the server's hashes before loading, so a failed load would make the next sync push a stale value over the other computer's change | code review, then confirmed by #1 | load first, restore the project if it fails, adopt only after success |
| 3 | After a pull the app **re-sent rows it had not changed** (it tidies rows on reload: adds default fields, rebuilds shuttering), which two computers could bounce back and forth | `ROWS 3` right after the catch-up in the fake server's log | the baseline is taken from the project as it is after a pull |
| 4 | **Derived rows churned**: shuttering (and link-rule rows) are rebuilt from scratch whenever quantities are calculated, so every rebuild gave every row a new random id and the server saw all of them deleted and re-inserted (17 rows sent for one pull) | the fake server's log | derived rows (`source` starting `auto`) get a stable id from what they derive from (`ProjectMapper.DerivedRowId`); 6 tests |
| 5 | **Local paths went to the server** (take-off file path, stored PDF paths, logo path) inside the settings, and the head moved on every sync so settings looked changed every time (an endless settings push) | code review | `ProjectMapper.SyncSettings` strips the local-only keys on push online, on settings push and in the change hash; 6 tests |
| 6 | **Settings changed on another computer never came in** on a pull | code review | the catch-up returns the server's settings and `ApplyDelta` merges them (local paths kept) |
| 7 | The catch-up **advanced the head before the rows were applied**, so a failure while applying lost them | code review | the head moves only when there is nothing to apply, or when the caller adopts it after applying |
| 8 | The change check **ran every second while typing** and rebuilt the whole project each time (0.3 to 0.5 s of UI time on 10 000 rows) | measured | it waits for 1.5 s of quiet; `ToSync` no longer deep-clones every row only to empty it (181 to 59 ms) |
| 9 | A drawing O-Draft recorded **without a name** showed an empty title and messages like ": no annotated drawing" | the Take-off page in the real app | `ShilpiDrawing.DisplayName` ("drawing <slot>") |
| 10 | A compiler null warning in `ConnectClient` (the guard was a level up; made explicit) | build | none needed beyond `!` |

**Checked in the real app (24 checks, all passing):** sign in (wrong password first, with its message), the chip naming the studio, a take-off imported through the UI, push online (54 rows, no local paths), the chip going to `synced`, a changed take-off reaching AORMS as exactly one row by itself, a catch-up applied (the wall then shows as changed locally) with nothing sent back, sign-out. Also on the Take-off page: the drawing list from the shared server, Read take-off and its preview, Fetch annotated drawing (PDF verified, stored, listed with Open).

**Test totals:** `AQC.CadImport.Tests` 65, `Aorms.Connect.Tests` 127, `Office.Shared.Tests` 5, engine `bbs_tests` 0 failures (MSVC and clang), all passing. **Timing, 10 000 rows (release build):** a change check costs about 0.14 s, settings 0.06 s, building the JSON 0.06 s; the baseline file is written in under 0.1 s.

**Still not checked:** the real AORMS server (the fake follows the contract as the library understands it); two computers editing at once (lease contention); the Online projects list with a bound project; the home screen (not built); O-Draft's own annotated PDF.

**2026-10-19 (hub merge and install):**

- `origin/main` (project sync, O-Draft reading, annotated drawings, debug pass) was merged into the company-live-sync branch with no conflicts, and the result went to `main` as a fast-forward (`b67710a`). After the merge `Office.Shared.Tests` (6) and `AQC.CadImport.Tests` (65) pass and the app builds in Release. `Aorms.Connect.Tests` was not re-run after the merge.
- O-CoreOp 1.1.0 was built and installed on the development machine, with `office.dll` copied in by hand (see 2.6).
- **Not done:** a run of the installed app against the company file after the merge (the merged sync host and the shared-company code were not driven together); `uia-sync-e2e.ps1` was not re-run.
