# The project file: ShilpiDB, beside the drawing

An O-CoreOp project is a ShilpiDB file (`.vdb`). It sits beside the drawing (`house.dwg` and `house.takeoff.vdb`), it is the file O-Draft records its take-off in, and it holds everything O-CoreOp keeps for the project: the estimate and its revisions, running bills, pending tasks, quantities, purchase orders, contracts, stores, rates and settings. The company file stays as it is (it has the company's own data); a project no longer has a file of its own.

## Opening

- **File > Open ShilpiDB project…** picks the `.vdb`. A file with a project in it opens that project. A file O-Draft has just made, with a take-off but no project yet, opens as a new project named after the drawing; the first Save adds the project to the same file. A project from before (`.bbsproj`, `.json`) still opens, and Save writes it into a ShilpiDB file (Save as).
- **File > Open O-Draft's project** opens the file of the drawing O-Draft has open (below). O-CoreOp also switches to it by itself when the project here has no unsaved changes.
- **File > New project** starts an empty project; Save as picks the `.vdb`.
- The File menu no longer has Open company, Company projects or Close company.

## Active project (O-Draft to O-CoreOp)

O-Draft keeps `%LOCALAPPDATA%\OSAS\active-project.json` current: it writes it when a saved drawing is opened, saved under a name, or its tab is chosen, and deletes it when no saved drawing is active or O-Draft closes.

```json
{ "format": "osas-active-project", "version": 1, "app": "O-Draft",
  "drawing": "C:\\p\\house.dwg", "shilpidb": "C:\\p\\house.takeoff.vdb", "updated_utc": "2026-10-29T09:00:00Z" }
```

`shilpidb` is where O-Draft's `takeoff.record` writes with `file: true`: beside the drawing, extension replaced by `takeoff.vdb`. O-CoreOp reads the pointer every two seconds (`ActiveProject`, `Office.Shared`). A pointer without a rooted `.vdb` path, or in another format, is ignored.

## What is in the file

Records are ShilpiDB's `(u64 id, box, bytes)`. O-Draft's take-off kinds (1, 3 to 11, the annotated PDF) are unchanged and read by the Take-off page straight from this file, no server. O-CoreOp adds one kind:

| kind | Record | Payload |
|---|---|---|
| `0x60` | O-CoreOp project document, chunk `n` in the low 32 bits of the id (a chunk is at most 4 MiB, a project is usually one) | the project as UTF-8 JSON, the same document the old project file held |

Reading takes every kind-`0x60` record, orders them by `n` and joins them. **Saving** reads the file again as it is on disk, replaces only the kind-`0x60` records, and writes to a temporary file that is then moved over the original, so a take-off O-Draft recorded since the project was opened is kept and a reader never sees half a file. The file is written as version 1 (the one `shilpid` and O-Draft both read); O-Draft's spatial-index trailer is dropped and O-Draft rebuilds it when it loads the file. Code: `Office.Shared/ShilpiProjectFile.cs`, `BBSApp/Services/ProjectFiles.cs`; tests `ShilpiProjectFileTests`, `ActiveProjectTests`. O-Draft: `takeoff.record` accepts a file that holds only a project (kind `0x60`) and keeps the record.

## Taking off

Measuring happens in O-Draft, with its take-off toolbars. The Take-off page lists the drawings recorded in the open project's file, reads one into the import preview, and refreshes by itself when the file changes. Imported rows are ordinary rows: they can be edited by hand on their pages, and importing again updates the rows O-Draft measured without touching a row edited here.

## Limits

- **One writer at a time.** The file is read and written directly, with no server, so O-CoreOp saving and O-Draft recording at the same instant can lose one of the two writes (each save re-reads the file first, which makes the window a few milliseconds, but it is not locked). Save in O-CoreOp, record in O-Draft, one after the other.
- The shared take-off server and its page (start server, server address) are gone from the Take-off page; the code (`ShilpiTakeoffService`) still reads one.
- A project's annotated drawings are still kept beside the project file in an `.assets` folder, not inside it.
- Not run end to end against a live O-Draft: the pointer was checked by writing it by hand, and O-Draft's side by its unit test and build.
