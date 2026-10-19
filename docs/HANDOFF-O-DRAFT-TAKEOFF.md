# Hand-off to O-Draft: take-off recorded in ShilpiDB

Written 2026-10-19 for whoever works in the O-Draft repository (`HolagundiWorks/O-DRAFT`, Rust). Written from the O-CoreOp side: **everything about O-Draft below was read from its `origin/main` on 2026-10-19 and not run.** Where it says *check*, confirm it before building on it.

## 1. What changed and why

Drawing and PDF take-off has moved out of O-CoreOp into O-Draft. O-CoreOp's own PDF take-off panel (import a PDF, set a scale, measure, "Commit to BOQ") was removed. The take-off is **recorded in ShilpiDB by O-Draft** and **read from there by O-CoreOp**. The full contract, which you must follow, is **[TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md)** (read it first; this file only says what to build). The bridge file contract ([bridge/](bridge/README.md)) is unchanged and is the format of each row.

O-CoreOp's reader is built and has been run against the real ShilpiDB programs (section 6). **Nothing in O-Draft writes this yet.**

## 2. What O-Draft already has (check each)

| Need | Where in O-Draft (read on 2026-10-19) |
|---|---|
| Take-off from drawn geometry | `takeoff.start`, `takeoff.linear\|brick\|beams\|columns\|rects\|paint\|ceiling\|floor\|area\|slab`, `takeoff.list`; results are saved in the drawing (`crates/engine/src/cmd/takeoff.rs`) |
| The bridge file as JSON | `bridge.export` builds the exact rows this contract needs: walls, openings, door/window blocks, columns, lintels, slabs, rooms, levels, project, beam depths, opening schedule, `drawing_id`, `source.revision` (`crates/engine/src/cmd/bridge.rs`) |
| A ShilpiDB file and a server client | `crates/vdb`: `VdbStore`, `save_path`, `load_path`, `VdbClient::{connect, insert, insert_many, delete, list, save}`; `MAX_RECORDS` 1,000,000, `MAX_PAYLOAD` 16 MB |
| Commands that do not need a window | `Session::execute`, JSON forms, the MCP server |
| A drawing revision hash | `drawing.revision`, `<file>.aadrev.json` |

## 3. What to build

### 3.1 A command that records the take-off: `takeoff.record`

Run `bridge.export`'s collection step (the part that builds the `root` JSON object, without writing `name.aqc.json`) and write the result as ShilpiDB records per [TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md):

1. **Header record**, id `(1 << 56) | 0`: `{ "format": "hcw-takeoff-shilpi", "version": 1, "units": "mm", "source": {…}, "project": {…}, "levels": […], "beam_depths_mm": […], "opening_schedule": […] }`. Take `source`, `project`, `levels`, `beam_depths_mm` and `opening_schedule` from the bridge JSON unchanged. Add `source.app` = `"O-Draft"` and keep `drawing_id` and `revision` (call it `revision_sha256`: the hash `drawing.revision` produces).
2. **One record per row**, id `(kind << 56) | n`, payload = the row object exactly as `bridge.export` writes it: kind 3 wall, 4 opening, 5 column, 6 lintel, 7 room, 8 slab.
3. **Bounding box** = bounds of that item's geometry in drawing coordinates, in millimetres (convert from the drawing units exactly as `bridge.export` does for lengths). No geometry: a zero box at the origin.
4. **Where to write:** a `.vdb` file beside the drawing named `<drawing>.takeoff.vdb`, using `VdbStore` and `save_path`, **or** a running `shilpid` through `VdbClient`. Support both; the file is the default, because O-CoreOp can read a file with no server running.
5. **Re-recording must replace, not append.** Write the new set, then **delete the records of that kind that are no longer present**, so the database always equals the current take-off. (A record with an existing id is replaced, checked on the real server.)
6. Report counts per kind, and anything skipped, as `bridge.export` already reports warnings.

### 3.2 Stable ids: the one thing that is wrong today

`bridge.export` currently builds `ref` from the **position in the take-off table**: `C0001`, `L0001`, `S0001`, `W0001`. **If the take-off is re-run or reordered, the same wall can get a different `ref`.** O-CoreOp matches on `ref` to update instead of duplicating, so a changing `ref` makes it see a deleted row and a new one. Fix it at the source, in `bridge.export` and so in both paths:

- Door and window blocks: the block's entity handle (already the case: check).
- Walls, columns, lintels, slabs, rooms: derive `ref` from something that survives a re-run: the entity handle of the measured polyline or block, or, for the take-off table rows, the measured geometry's handle plus a letter. If a stable key is not available for some tool, say so in the command's result rather than pretending.
- `n` in the record id is then the numeric value of that `ref`'s handle (or a number stored with the take-off in the drawing). It must be the same for the same item on every run.

### 3.3 The slab row is incomplete

`bridge.export` sends `{ ref, level, area_m2, thickness_mm }` for a slab. O-CoreOp imports a slab only when the row also has `length_mm`, `breadth_mm` and `"rectangular": true` (it needs the two spans). Otherwise it **skips the slab and tells the user to add it by hand**. For a rectangular slab (check: the outline is four right angles) add `length_mm`, `breadth_mm` and `"rectangular": true`. For other shapes leave them out; `area_m2` stays as information.

### 3.4 PDF take-off in O-Draft

The take-off panel O-CoreOp had also worked from **a PDF**. O-Draft's take-off reads drawn geometry on `MEASURE-*` layers. To replace the PDF workflow O-Draft needs to **measure over a PDF**: show a PDF page as an underlay, calibrate its scale from two points and a known length, and then let the same `takeoff.*` tools measure over it. *Check* whether O-Draft has a PDF underlay today; if it does not, that is the work, and it is O-Draft's decision how to do it (tracing over an underlay with the existing measure tools is the smallest step). What the old panel did, as behaviour to match (not code to copy; see section 5):

- Import a PDF, page by page; zoom, pan, fullscreen.
- **Set scale** by picking two points on a known dimension and typing the real length in millimetres.
- **Measure by element kind**: point (columns, pedestals), line (beams, lintels, walls) and area (slabs, flooring, rooms); each measure carries a mark, a level (floor), and for an opening a width and height.
- **Openings**: a door or window is placed on a wall; the wall it belongs to is the nearest wall, changeable.
- Layers on and off, a list of take-off items, delete.
- An annotated drawing in the reports (O-CoreOp still prints the annotated drawing for projects saved before; for new take-off it will not have one unless you provide it in the header or a file; ask the owner).

### 3.5 Optional: reading back

When O-Draft opens a drawing whose `.takeoff.vdb` exists, it can show what was recorded (select by bounding box). Not needed for O-CoreOp.

## 4. Tests to add (O-Draft side)

1. `takeoff.record` on a drawing with walls, openings, columns, a lintel, a slab and a room: the record count and kinds match; the header parses; every payload parses and equals the bridge row.
2. Recording twice: the same ids, same count, no duplicates; change one wall's length and record again: one record changed.
3. Remove a measured wall and record again: its record is deleted.
4. Ids: `kind` in the top byte, `n` in the low 56 bits; the header is `72057594037927936`.
5. Everything in millimetres, whatever the drawing units (imperial too).
6. A rectangular slab carries `length_mm`, `breadth_mm`, `rectangular: true`; an L-shaped one does not.
7. Refs are stable: record, rebuild the take-off from the same geometry, record again: the ids do not change.

## 5. Rules

- **Do not copy code between the repositories.** O-CoreOp is AGPL-3.0 or commercial; O-Draft is MIT or Apache-2.0. Share the contract only. The old panel's code is in O-CoreOp's git history (the commit before "take-off moved to O-Draft") if you want to read how it behaved; read it for behaviour, write your own.
- Send **measured facts only**: no plaster, painting, skirting, shuttering, rebar, rates or costs (O-CoreOp derives them).
- A change to a field, a kind number or a unit is a new version, decided by the owner, in [TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md) and here together. Adding a kind or an optional field is not.
- Say only what you ran. Mark each item "Ready for verification @ <hash>" the way O-Draft's own process does.

## 6. How to check your work against O-CoreOp

O-CoreOp's reader was run against the real ShilpiDB programs (`shilpid`, `shilpi-http`) like this, and you can repeat it for your output:

```bash
shilpid --bind 127.0.0.1:7420 --data house.takeoff.vdb --read-only
shilpi-http --bind 127.0.0.1:7421 --backend 127.0.0.1:7420
curl http://127.0.0.1:7421/records               # the ids: one header (72057594037927936) and the rows
curl http://127.0.0.1:7421/records/72057594037927936   # payloadText is the header JSON
```

Then in O-CoreOp: **File > Take-off from ShilpiDB…**, choose the `.vdb`, read it; the preview lists what would be added. Rows a reader cannot use are named in the status line, not dropped silently.

The reader and its 12 tests are `BBSDesktop/AQC.CadImport/ShilpiTakeoff.cs` and `BBSDesktop/AQC.CadImport.Tests/ShilpiTakeoffTests.cs` in the O-CoreOp repository; `ShilpiTakeoffTests.RecordsFromFixture` is a working example of the records a writer must produce, built from `docs/bridge/fixtures/small-house.json`.

## 7. Open questions for the owner

1. Is the take-off database a **file beside the drawing** (assumed here) or a shared database served all day?
2. One take-off database per drawing is assumed. For a project with several drawings, should O-CoreOp import each in turn (today: yes, rows are added alongside) or should O-Draft merge them first?
3. Who owns PDF underlay and calibration in O-Draft, and when? Until it exists, a project that only has PDF drawings cannot be measured anywhere.
4. The annotated drawing in O-CoreOp's reports came from the PDF panel. Should O-Draft export an annotated image or PDF of the measured drawing for the reports?
