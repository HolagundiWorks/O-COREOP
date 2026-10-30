# The project file (.osp)

An O-CoreOp project is a **project file, `.osp`**: one SQLite file that holds the drawing, the take-off O-Draft recorded, and everything O-CoreOp keeps for the project (estimate and revisions, running bills, tasks, quantities, purchase orders, contracts, stores, rates, settings). O-Draft and O-CoreOp open the same file. The company file stays Studio's and is read-only here (name, address, GSTIN, letterhead). The names, roles and links of the whole suite are in [SUITE.md](SUITE.md); the format and sync plan are in the O-Studio repository, `docs/ODP-PROJECT-FILE-AND-SYNC.md`.

## What O-CoreOp does with it

| Action | Behaviour |
|---|---|
| File > Open | `.osp`; older `.bbsproj` / `.json` still open |
| File > Save / Save as | Writes the project record into the `.osp`; Save as to a new name copies the file first so the drawing and take-off come with it. Older formats are saved as `.osp` |
| Take-off page | Reads headers, rows and annotated PDFs from the `.osp` (`OdpTakeoffAdapter` feeds the same importer as before) |
| Live | Every 2 s `ServedProject.PollAsync` reads `meta.revision`; a revision this app did not write raises "O-Draft updated the take-off". Own saves are ignored (`NoteOwnRevision`) |
| Opening O-Draft's project | The active-project pointer `%LOCALAPPDATA%\OSAS\active-project.json` names the `.osp` O-Draft has open |
| Double-click | The installer registers `.osp` with "Open in O-CoreOp" in the right-click menu |

Code: `Office.Shared/OdpFile.cs` (the format), `BBSApp/Services/ProjectFiles.cs`, `Services/Shilpi/{ServedProject,OdpTakeoffAdapter}.cs`, `Views/TakeoffPage.cs`.

## Retired

ShilpiDB files (`.vdb`), the `shilpid` / `shilpi-http` servers and the gateway are no longer used for projects. There is no conversion: no production project was ever created in ShilpiDB. [TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md) is kept for the record layout, which the `.osp` take-off tables keep.
