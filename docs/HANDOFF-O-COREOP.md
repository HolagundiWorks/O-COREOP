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

Not on `main` yet: this branch also carries one commit that rewrites `README.md` and `docs/WINUI-SHELL.md` to use only the current names.

## 2. Do first: make what exists trustworthy

| # | Item | Evidence | Where to look |
|---|---|---|---|
| 2.1 | Add the repository secret `HCW_UI_TOKEN` (a read-only token for `HolagundiWorks/hcw-ui`). Without it the CI build job cannot clone the design library and fails | Checked: the workflow reads `secrets.HCW_UI_TOKEN`; the first run on GitHub has not been looked at | `.github/workflows/ci.yml` |
| 2.2 | Sign in to the real AORMS portal and pull a project. Everything in `Aorms.Connect` was tested against a fake server and a local development server | Not tested live (`docs/ROADMAP.md`, "CAD take-off and AORMS sign-in") | `docs/AORMS-CONNECT-HANDOFF.md`, `BBSDesktop/Aorms.Connect.Smoke` |
| 2.3 | The portal needs migration `0113_aqc_sessions_aos_app.sql` applied before the suite apps can hold a session. It is on the branch `claude/aos-suite-session` of the portal repository | Checked: not applied | portal repository, `web/supabase/migrations/` |
| 2.4 | Click through File > Import from CAD, File > Export for CAD and File > Online projects with a real drawing export. The libraries have 20 and 80 tests; the app screens have not been used | Not tested | `BBSDesktop/AQC.CadImport`, `docs/bridge/` |
| 2.5 | Install on a clean machine with the Inno Setup installer and check an installed copy of the older name upgrades in place (same AppId) and the Start menu entry reads O-CoreOp | Not tested | `BBSDesktop/installer/AQCCore.iss`, `build-installer.ps1` |
| 2.6 | Rebuild the installer and the release build after the rename; the script was edited for the new name but not run | Checked: not rebuilt | `BBSDesktop/installer/` |

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
| 4.1 | A file-backed `IOutboxStore`, so unsent edits survive a restart (only the in-memory one exists) |
| 4.2 | The home screen: sign in first, "Continue without signing in" for the Community edition, then Online projects, then Local; open from a seed; Push online |
| 4.3 | `ProjectStore` hooks: call `Outbox.Upsert/Delete` and `NoteChange()` from `ProjectStore.Notify()`, drive `ProjectSync.TickAsync()` from a timer of about 1 s, and mint `_rid` on take-off rows through `ProjectMapper.ToSync` |
| 4.4 | Map the shared rate books onto `RateBookStore` (the library `RateBooksApi` is done) |
| 4.5 | Remove `Aorms.Bridge`, `Aorms.Bridge.Smoke` and the old Bind/Flush flow once nothing references them. The status strip still has Bind and Flush buttons |
| 4.6 | Two suite apps refreshing the shared token at once, and sign-out clearing the shared entry from the UI, were not checked |

## 5. Join up with the rest of the suite

| # | Item | Roadmap | Notes |
|---|---|---|---|
| 5.1 | Read the shared company file, so a client and a project are created once. O-CoreOp does not reference `Office.Interop` today; it gets projects only through the portal | 2.2 | The interface is `office-ffi` in the O-STUDIO repository, contract in its `docs/office-abi.md`. O-CoreOp keeps its own tables under the schema-track rules (`<track>_*` names only) |
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
4. Whether O-CoreOp may require the shared company file, or must still run on its own project file. This decides how much of section 5 is needed.

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
