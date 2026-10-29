# The project file: ShilpiDB, beside the drawing

An O-CoreOp project is a ShilpiDB file (`.vdb`). It sits beside the drawing (`house.dwg` and `house.takeoff.vdb`), it is the file O-Draft records its take-off in, and it holds everything O-CoreOp keeps for the project: the estimate and its revisions, running bills, pending tasks, quantities, purchase orders, contracts, stores, rates and settings. The company file stays as it is (it has the company's own data); a project no longer has a file of its own.

## The company file is Studio's

O-CoreOp does not open, save to or share into the company file. Studio signs the person in and opens O-CoreOp from its card (`OSAS_COMPANY`, one-time `OSAS_TICKET`); the company file is then **read only** here: the company's details and the Studio project's details (client, site and so on) go into reports and title blocks. The Studio project is the one the take-off header names (`project_ref`: company uuid and project id) when it is the same company. The company badge says where the details come from; Save to company file, the project chooser, create client or project, the polling and conflict dialog and the Share draft to Studio buttons are gone.

## Opening

- **File > Open ShilpiDB project…** picks the `.vdb`. A file with a project in it opens that project. A file O-Draft has just made, with a take-off but no project yet, opens as a new project named after the drawing; the first Save adds the project to the same file. A project from before (`.bbsproj`, `.json`) still opens, and Save writes it into a ShilpiDB file (Save as).
- **File > Open O-Draft's project** opens the file of the drawing O-Draft has open (below). O-CoreOp also switches to it by itself when the project here has no unsaved changes.
- **File > New project** starts an empty project; Save as picks the `.vdb`.
- The File menu no longer has Open company, Company projects or Close company.

## Live: O-Draft runs the server

While a saved drawing is active, O-Draft runs the ShilpiDB server on that file: `shilpid` on `127.0.0.1:7420` (autosave every 2 s) and the `shilpi-http` gateway on `127.0.0.1:7421`. 7420 is where `takeoff.record` records by default, so the take-off toolbars write into the project's own file with no setting. When the active drawing changes O-Draft saves the file and serves the new one; closing O-Draft saves and stops the server. It does not touch a server it did not start: if something else already answers on those ports, nothing is served, and the pointer has no `server`.

While `server` is in the pointer and answers, **O-CoreOp does not touch the file**. It reads the project and the take-off through the gateway, saves the project through it (replace the kind-`0x60` records, then `POST /save`), and checks the record count every two seconds: when O-Draft records, updates or removes a take-off row, the Take-off page refreshes at once and a message says so. With no server (O-Draft closed, ShilpiDB not installed, ports taken) O-CoreOp reads and writes the file directly, as below.

## Active project (O-Draft to O-CoreOp)

O-Draft keeps `%LOCALAPPDATA%\OSAS\active-project.json` current: it writes it when a saved drawing is opened, saved under a name, or its tab is chosen, and deletes it when no saved drawing is active or O-Draft closes.

```json
{ "format": "osas-active-project", "version": 1, "app": "O-Draft",
  "drawing": "C:\\p\\house.dwg", "shilpidb": "C:\\p\\house.takeoff.vdb", "server": "http://127.0.0.1:7421", "updated_utc": "2026-10-29T09:00:00Z" }
```

`server` is the gateway while O-Draft serves the file, else null; it must be an `http` address on this computer. `shilpidb` is where O-Draft's `takeoff.record` writes with `file: true`: beside the drawing, extension replaced by `takeoff.vdb`. O-CoreOp reads the pointer every two seconds (`ActiveProject`, `Office.Shared`). A pointer without a rooted `.vdb` path, or in another format, is ignored.

## What is in the file

Records are ShilpiDB's `(u64 id, box, bytes)`. O-Draft's take-off kinds (1, 3 to 11, the annotated PDF) are unchanged and read by the Take-off page straight from this file, no server. O-CoreOp adds one kind:

| kind | Record | Payload |
|---|---|---|
| `0x60` | O-CoreOp project document, chunk `n` in the low 32 bits of the id (a chunk is at most 4 MiB, a project is usually one) | the project as UTF-8 JSON, the same document the old project file held |

Reading takes every kind-`0x60` record, orders them by `n` and joins them. **Saving** reads the file again as it is on disk, replaces only the kind-`0x60` records, and writes to a temporary file that is then moved over the original, so a take-off O-Draft recorded since the project was opened is kept and a reader never sees half a file. The file is written as version 1 (the one `shilpid` and O-Draft both read); O-Draft's spatial-index trailer is dropped and O-Draft rebuilds it when it loads the file. Code: `Office.Shared/ShilpiProjectFile.cs`, `BBSApp/Services/ProjectFiles.cs`; tests `ShilpiProjectFileTests`, `ActiveProjectTests`. O-Draft: `takeoff.record` accepts a file that holds only a project (kind `0x60`) and keeps the record.

## Taking off

Measuring happens in O-Draft, with its take-off toolbars. The Take-off page lists the drawings recorded in the open project's file, reads one into the import preview, and refreshes by itself when the file changes. Imported rows are ordinary rows: they can be edited by hand on their pages, and importing again updates the rows O-Draft measured without touching a row edited here.

## Limits

- **Served, one writer: the server.** Both apps go through it, so they can work at the same time. Direct file access (no server) remains for O-CoreOp alone; if O-Draft then starts serving the same file, a save made in the instant before can be overwritten by the server's first autosave, so open O-Draft first.
- An old shared take-off server left running on 7420/7421 (for the global `takeoff.vdb`) blocks O-Draft from serving the project; stop it (`shilpid`, `shilpi-http` in Task Manager) and reopen the drawing.
- The shared take-off server and its page (start server, server address) are gone from the Take-off page; the code (`ShilpiTakeoffService`) still reads one.
- A project's annotated drawings are still kept beside the project file in an `.assets` folder, not inside it.
- Run end to end on 2026-10-29 with the real O-Draft (control port): opening a drawing started the server and wrote the pointer; O-CoreOp followed it, showed "Live: O-Draft is serving…", listed the take-off `takeoff.record` had just recorded, and saved its project through the gateway (the file kept both). The servers end with O-Draft, also when it is killed (job object).
