# Take-off in ShilpiDB: the contract between O-Draft and O-CoreOp

Written 2026-10-19 on the owner's instruction. **Status: version 1, draft.** O-CoreOp's side is built and checked (see section 7). O-Draft's side is not built; the instructions for it are in [HANDOFF-O-DRAFT-TAKEOFF.md](HANDOFF-O-DRAFT-TAKEOFF.md).

## The decision

Drawing and PDF take-off is **not done in O-CoreOp any more**. It is done in **O-Draft** (the drawing app; its repository is `HolagundiWorks/O-DRAFT`, earlier called AADT and AAD). O-Draft **records the take-off in ShilpiDB**, and O-CoreOp **reads it from there**, previews it and imports it. O-CoreOp's own PDF take-off panel and canvas were removed on 2026-10-19; a project saved before that keeps its stored take-off data and its annotated drawing in reports, but there is no longer a way to measure a PDF in O-CoreOp.

The bridge **file** (`hcw-aqc-bridge` v1, [bridge/](bridge/README.md)) stays: it is the offline path (File > Import from CAD) and the format each take-off row is written in. This contract puts the same rows in ShilpiDB.

## What ShilpiDB is, for this purpose

A store of **records**. A record is a `u64` id, a bounding box (two corners, `f64` each) and a payload of bytes. There are no tables or collections, no types, and a record with an id that already exists is **replaced** (checked). `shilpid` serves a database file (`.vdb`) over TCP; `shilpi-http` is a JSON gateway in front of it (`GET /records` lists ids, `GET /records/{id}` returns the box and the payload as text and hex, `PUT /records/{id}?minX=&minY=&maxX=&maxY=` with the payload as the body, `POST /save`). Source: `HolagundiWorks/shilpidb`.

## One database per drawing

A take-off lives in **its own ShilpiDB** (its own `.vdb`), not in the database that holds the drawing's entities. The id space is shared inside a database and has no namespaces, so mixing the two would make every reader guess. Name it `<drawing name>.takeoff.vdb` beside the drawing. A database that has more than one header record (below) is refused.

## Records

Everything is millimetres (`"units": "mm"`; areas in m²) and the payload is **UTF-8 JSON, one object**.

### Id

`id = (kind << 56) | n`

`n` is the CAD entity handle (hex handle read as a number) or, where an item has no entity, a running number starting at 1. It must be **the same for the same measured thing every time you export**, so exporting again replaces the same records. Do not use a counter that restarts.

| kind | Record | Payload |
|---|---|---|
| 1 | **Header** (exactly one, `n = 0`, so id `72057594037927936`) | see below |
| 3 | Wall | the bridge file's `walls[]` row |
| 4 | Opening (door or window) | the `openings[]` row |
| 5 | Column | the `columns[]` row |
| 6 | Lintel | the `lintels[]` row |
| 7 | Room | the `rooms[]` row |
| 8 | Slab | the `slabs[]` row |

Kinds 0, 2 and 9 to 255 are reserved. A reader skips an unknown kind and reports it; it never fails on one, so a later version can add kinds.

### Item payload

**The same fields as the bridge file row, unchanged** (see [bridge/hcw-aqc-bridge.schema.json](bridge/hcw-aqc-bridge.schema.json) and the samples in [bridge/fixtures](bridge/fixtures)): a wall is `{ "ref", "level", "length_mm", "height_mm", "thickness_mm", "layer" }`, an opening `{ "ref", "kind", "mark", "type", "level", "width_mm", "height_mm", "sill_mm", "wall_ref" }`, and so on. `ref` is the row's stable identity (the wall id, the block handle); O-CoreOp stores it as `source_ref`. `level` is the floor **name** from the header's `levels`.

### Bounding box

The bounds of the measured geometry in the drawing's coordinates, in millimetres: the wall's outline, the opening's block, the room's outline. O-CoreOp does not need it to import, but it lets any ShilpiDB client find "what was measured in this window", and O-Draft can use it to select the item in the drawing. For an item with no geometry use a zero-size box at the origin. The header's box is `0,0,0,0`.

### Header payload

```json
{
  "format": "hcw-takeoff-shilpi",
  "version": 1,
  "units": "mm",
  "source": { "app": "O-Draft", "drawing": "small house.dxf", "drawing_id": "<GUID kept in the drawing>",
              "exported": "2026-10-19T11:00:00Z", "revision_sha256": "<hash of the saved drawing>" },
  "project":  { "name": "", "client_name": "", "company_name": "", "location": "", "address": "" },
  "levels":   [ { "name": "Ground", "height_mm": 3150, "slab_thickness_mm": 150, "beam_depth_mm": 450 } ],
  "beam_depths_mm": [ 300, 450 ],
  "opening_schedule": [ { "mark": "D2", "kind": "Door", "width_mm": 900, "height_mm": 2100, "nos": 2 } ]
}
```

`format`, `version` 1, `units` `mm` and `source.drawing_id` are required; a reader refuses anything else. `project`, `levels`, `beam_depths_mm` and `opening_schedule` are optional and have the bridge file's meaning (levels are the floors **above the plinth**; `Lvl0` is O-CoreOp's and is never imported). `revision_sha256` is the `DrawingRevision` O-Draft already computes at save, so an estimate can say which drawing revision it used.

## Rules that do not change

These are the file importer's rules, because the records are turned into a bridge file and fed to it:

1. Imported rows get `source = "hcwcad:<drawing_id>"` and `source_ref = <ref>`. The prefix `hcwcad:` stays: it is stored in users' projects. A take-off imported from a file and the same drawing's take-off read from ShilpiDB therefore **never duplicate each other** (tested).
2. Preview first, one undo step, nothing deleted: a row that is no longer in the take-off is listed as "no longer in the drawing" and kept.
3. A row the user edited in O-CoreOp after importing is flagged and left alone unless they choose to overwrite.
4. **Never send** plaster, painting, skirting, shuttering, rebar, PCC, earthwork, waterproofing or any rate or cost. O-CoreOp derives or owns them.
5. A column carries no height; O-CoreOp computes it from the level.
6. Wall thickness of 120 mm or less is measured by O-CoreOp as its 110 mm wall.

## How O-CoreOp reads it

Take-off page (File > Take-off from ShilpiDB, or the **Take-off (O-Draft)** item under Project):

1. The user chooses the project's take-off database (`.vdb`); the path is saved in the project file (`takeoff_vdb_path`).
2. O-CoreOp starts `shilpid --read-only` on that file and `shilpi-http` in front of it, on free local ports, reads every record, and stops both. It finds the programs in `SHILPIDB_BIN`, then `%LocalAppData%\ShilpiDB\bin`, then `PATH`. A read-only server refuses writes, so reading cannot change the take-off.
3. Or it reads from a gateway that is already running (default `http://127.0.0.1:7421`), for example one started by O-Draft while the drawing is open.
4. The records are turned into a bridge file, validated, and shown in the import preview (adds, updates, unchanged, flagged, skipped, with reasons). Records it cannot use are counted and named, not silently dropped.

Code: `BBSDesktop/AQC.CadImport/ShilpiTakeoff.cs` (the contract, the reader and the gateway client, platform-neutral) and `BBSDesktop/BBSApp/Services/Shilpi/ShilpiTakeoffService.cs` (starting and stopping the servers).

## Versioning

A change to a field, a kind number or a unit is a new `version`, decided by the owner, in this file and in O-Draft together. Adding a new kind or an optional field is not.

## 7. State

- **Built and run:** the reader, the gateway client, the preview wiring; 12 unit tests (`ShilpiTakeoffTests`) using fakes and the two bridge samples; and a one-off check against the **real** `shilpid` and `shilpi-http` binaries on the development machine: write 15 records through the gateway, replace one by id, save, restart read-only from the `.vdb`, read them back (3 walls, 5 openings, 2 columns, 2 rooms, 1 slab, the header), and confirm a write to the read-only server is refused.
- **Not built:** anything in O-Draft; the Take-off page and the launcher have not been used by a person; nothing has been read from a database O-Draft wrote, because O-Draft cannot write one yet.
- **Open for the owner:** whether the database should instead be a shared one served all day (then O-CoreOp always uses the gateway), and whether more than one drawing per project is wanted (today: one take-off database per drawing, and importing a second drawing adds its rows alongside the first).
