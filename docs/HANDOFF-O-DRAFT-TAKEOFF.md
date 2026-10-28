# Hand-off to O-Draft: record the take-off in ShilpiDB

Written 2026-10-19, revised the same day after the owner's answers, for whoever works in the O-Draft repository (`HolagundiWorks/O-DRAFT`, Rust). Written from the O-CoreOp side: **everything about O-Draft below was read from its `origin/main` on 2026-10-19 and not run.** Where it says *check*, confirm it before building on it.

## 1. What was decided (owner, 2026-10-19)

- **Take-off from drawings and PDFs is done in O-Draft**, including **PDF underlay and calibration**. O-CoreOp's PDF take-off panel was removed.
- O-Draft **records the take-off in ShilpiDB**; O-CoreOp reads it. **Both apps run side by side on one shared ShilpiDB server**, so there is no file to hand over: record to the server and O-CoreOp sees it within seconds.
- A project can have **several drawings**; each is its own take-off in the same database (slots).
- When a take-off is **complete**, O-Draft **generates an annotated drawing PDF** (quantity lines, measurements, hatches) and records it; O-CoreOp stores it.

The contract you must follow is **[TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md)**; read it first. This file only says what to build and how to check it. The bridge file contract ([bridge/](bridge/README.md)) is unchanged and is the format of each row.

O-CoreOp's reader is built and has been run against the real ShilpiDB programs, including two drawings in one database and a binary PDF in chunks. **Nothing in O-Draft writes this yet.**

## 2. What O-Draft already has (check each)

| Need | Where in O-Draft (read on 2026-10-19) |
|---|---|
| Take-off from drawn geometry | `takeoff.start`, `takeoff.linear\|brick\|beams\|columns\|rects\|paint\|ceiling\|floor\|area\|slab`, `takeoff.list`; results saved in the drawing (`crates/engine/src/cmd/takeoff.rs`) |
| The bridge file as JSON | `bridge.export` builds the rows this contract needs: walls, openings, door/window blocks, columns, lintels, slabs, rooms, levels, project, beam depths, opening schedule, `drawing_id`, `source.revision` (`crates/engine/src/cmd/bridge.rs`) |
| ShilpiDB file and server client | `crates/vdb`: `VdbStore`, `save_path`, `load_path`, `VdbClient::{connect, insert, insert_many, delete, list, list_boxes, get, save}`; `MAX_RECORDS` 1,000,000, `MAX_PAYLOAD` 16 MB |
| Windowless commands, MCP, control channel | `Session::execute`, JSON forms, `crates/mcp`, `aad-host` |
| Drawing revision hash | `drawing.revision`, `<file>.aadrev.json` |
| Company-file project link | Prefer `source.project_ref` (company UUID + project id) so O-CoreOp can group drawings with the open company file |

## 3. What to build

### 3.1 Start or find the shared server

On first need, `GET http://127.0.0.1:7421/health`. If nothing answers, start `shilpid --bind 127.0.0.1:7420 --data %LocalAppData%\OSAS\takeoff\takeoff.vdb --autosave-secs 5` and `shilpi-http --bind 127.0.0.1:7421 --backend 127.0.0.1:7420` (find them in `SHILPIDB_BIN`, `%LocalAppData%\ShilpiDB\bin`, then `PATH`). Both bind fixed ports, so if O-CoreOp starts one at the same moment the second fails harmlessly: use whichever is up. You may talk to `shilpid` with `VdbClient` (binary, port 7420) or to the gateway (HTTP, 7421); `VdbClient` is the better fit in Rust. See [TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md) section 3.

### 3.2 `takeoff.record`

Run `bridge.export`'s collection step (the part that builds the `root` JSON, without writing `name.aqc.json`) and write it to ShilpiDB following the **writer's protocol in section 4.4 of the contract**, in this order:

1. **Slot.** `SlotFor(drawing_id)` (FNV-1a, test vectors in the contract), probing on collision, **stored in the drawing** with its `drawing_id` so it never changes.
2. **Header** with `"state": "recording"` (no `annotated_pdf`).
3. **Rows**: one record per row, `id = (kind << 56) | (slot << 32) | n`, kind 3 wall, 4 opening, 5 column, 6 lintel, 7 room, 8 slab; payload = the row object exactly as `bridge.export` writes it; bounding box = the item's geometry bounds in **millimetres** (zero box at the origin when there is none).
4. **Delete** this slot's records of kinds 3 to 8 that are not in the new set.
5. **Annotated PDF**, if generated (3.4).
6. **Header** with `"state": "complete"` (+ `annotated_pdf`), then `POST /save` (or `VdbClient::save`).

Header content: take `source`, `project`, `levels`, `beam_depths_mm` and `opening_schedule` from the bridge JSON unchanged. Add `source.app` = `"O-Draft"`, `source.project_ref` (company UUID + project id from the shared company file; **this is what lets O-CoreOp group a project's drawings**), and `source.revision_sha256` (the `drawing.revision` hash). Optional legacy `project_office_id` is ignored for grouping in O-CoreOp. Report counts per kind and any skipped item, as `bridge.export` reports warnings.

### 3.3 Two fixes in `bridge.export` that this depends on

1. **Stable `ref`s.** `bridge.export` builds `ref` from the **position in the take-off table** (`C0001`, `L0001`, `S0001`, `W0001`). If the take-off is re-run or reordered the same wall can get a different `ref`; O-CoreOp matches on `ref`, so it would see one row deleted and a new one added. Derive `ref` (and `n`) from something that survives a re-run: the entity handle of the measured polyline or block (door and window blocks already use the block handle: *check*). Where a tool has no stable key, say so in the command's result rather than pretending.
2. **Slab rows are incomplete.** `bridge.export` sends `{ ref, level, area_m2, thickness_mm }`. O-CoreOp imports a slab only with `length_mm`, `breadth_mm` and `"rectangular": true`; otherwise it **skips it** and tells the user to add it by hand. For a rectangular slab (four right angles: *check*) add those three; for other shapes leave them out.

### 3.4 `takeoff.annotate`: the annotated drawing PDF

When the take-off is complete, generate a PDF of the drawing **with what was measured shown on it**:

- the **quantity lines** (each measured wall, beam, lintel, boundary; coloured by element kind or take-off group),
- the **measurements** (length or area text, with the mark or group letter),
- the **hatches** (areas measured: slab, floor, ceiling, room; and wall hatch where the take-off uses it),
- a legend and a title block that names the drawing, the revision and the date.

It must be of the **same revision** the take-off was measured from. One or more pages (one per layout or view is fine). Stay in your own rendering code (`aad-plot` or equivalent: *check* what O-Draft can already plot to PDF) and use the take-off results and their layers; do not read O-CoreOp's code.

Then record it: split into chunks of **at most 512 KiB** (the gateway returns hex), `PUT` kind 9 records `n = 0, 1, 2 …` with raw bytes and a zero box, total **at most 64 MiB**, and put `annotated_pdf` `{ sha256, bytes, chunks, pages, generated }` in the header at step 6. SHA-256 is of the whole PDF, upper or lower case hex. O-CoreOp verifies every chunk, the length, the hash and the `%PDF` start, and refuses anything else.

### 3.5 PDF underlay and calibration (O-Draft owns this)

The removed O-CoreOp panel measured from a **PDF**. O-Draft's take-off reads geometry on `MEASURE-*` layers, so PDF-only projects cannot be measured anywhere until O-Draft can **show a PDF page as an underlay, calibrate its scale, and measure over it** with the same `takeoff.*` tools. *Check* whether an underlay exists today; if not, that is the work and its design is O-Draft's. Behaviour of the old panel, to match (read it for behaviour, do not copy code; section 6):

- Import a PDF, page by page; zoom, pan, fullscreen.
- **Set scale** from two picked points on a known dimension and the real length in millimetres.
- **Measure by element kind**: point (columns, pedestals), line (beams, lintels, walls), area (slabs, flooring, rooms); each carries a mark and a level (floor); an opening has a width and height and sits on a wall (nearest wall by default, changeable).
- Layers on and off, a list of items, delete.

### 3.6 Several drawings

Each drawing records itself: own slot, own header, own `annotated_pdf`. Do **not** merge drawings. Give a project's drawings the same `project_office_id`. Name levels consistently across drawings (`Ground`, `First`): O-CoreOp matches levels **by name**. **Record only the drawing that is the measured source for a floor**: if two drawings both measure the same wall on the same floor, O-CoreOp imports both and double counts (it cannot know). If a drawing is retired, delete its slot's records.

### 3.7 Optional: show what was recorded

When O-Draft opens a drawing whose slot has records, it can select the measured item from the bounding box. Not needed for O-CoreOp.

## 4. Tests to add (O-Draft side)

1. `takeoff.record` on a drawing with walls, openings, columns, a lintel, a slab and a room: counts and kinds match; the header parses; every payload equals the bridge row; ids have the kind in the top byte and the slot in bits 32 to 55.
2. Recording twice: same ids, same count, no duplicates; change one wall and record again: one record changed; remove a wall and record again: its record is deleted.
3. **Slot**: `SlotFor` gives `6389047` for `0b7d9c34-1e6f-4f0b-8a13-9b2c7d5e41aa` and `15751439` for `6f1c1b0e-7a52-4d0a-9c1e-2d8a3f4b5c60`; a second drawing whose id hashes to a taken slot gets the next one; the slot is reused on the next record.
4. **State**: the header is `recording` while rows are written and `complete` at the end; a failure in the middle leaves `recording`.
5. Two drawings recorded into one database stay separate; deleting one leaves the other.
6. Units: everything in millimetres whatever the drawing units, imperial too.
7. A rectangular slab has `length_mm`, `breadth_mm`, `rectangular: true`; an L-shaped one does not.
8. Stable refs: record, rebuild the take-off from the same geometry, record again: ids unchanged.
9. Annotated PDF: chunk sizes, count, the SHA-256 and byte count in the header match the chunks; the PDF contains the measured items.

## 5. Rules

- **Do not copy code between the repositories.** O-CoreOp is AGPL-3.0 or commercial; O-Draft is MIT or Apache-2.0. Share the contract only. The old PDF panel's code is in O-CoreOp's git history (the commit before "take-off moves to O-Draft") if you want to read how it behaved; read for behaviour, write your own.
- Send **measured facts only**: no plaster, painting, skirting, shuttering, rebar, rates or costs.
- A change to a field, a kind number, the id layout or a unit is a new version, decided by the owner, in [TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md) and here together. Adding a kind or an optional field is not.
- Say only what you ran. Mark each item "Ready for verification @ <hash>" the way O-Draft's own process does.

## 6. How to check your work against O-CoreOp

Both apps use the shared server, so:

```bash
curl http://127.0.0.1:7421/health
curl http://127.0.0.1:7421/records                       # ids: look for kind 1 (header) per drawing
curl http://127.0.0.1:7421/records/<header id>           # payloadText is the header JSON; "state" must be "complete"
```

Then in O-CoreOp: **Project > Take-off (O-Draft)**. The drawing should appear within five seconds with its row count; **Read take-off** shows the import preview (adds, updates, unchanged, flagged, skipped, with reasons); **Fetch annotated drawing** stores the PDF and **Open** shows it. Anything O-CoreOp cannot use is named in the status line, not dropped silently.

The reader, the id and slot code and its tests are `BBSDesktop/AQC.CadImport/ShilpiTakeoff.cs` and `BBSDesktop/AQC.CadImport.Tests/ShilpiTakeoffTests.cs` in the O-CoreOp repository. `ShilpiTakeoffTests.RecordsFromFixture` and `WithPdf` are working examples of exactly what a writer must produce, built from `docs/bridge/fixtures/small-house.json`.

## 7. Open questions for the owner

1. Should the OSAS launcher start the shared server at logon, or is "whichever app needs it first" enough?
2. May one drawing describe several floors? (The contract allows it; rows name their level.)
3. Size limit for annotated PDFs (64 MiB assumed).
4. O-CoreOp reports will embed the annotated PDF pages; confirm whether the PDF should be one page per sheet or one combined page set (O-CoreOp does not mind).
5. A check for two drawings that measure the same floor (the double-count risk in 3.6): who owns it, O-Draft (warn when recording) or O-CoreOp (warn when importing)?

## 8. Results of reading your output (2026-10-19, from the O-CoreOp side)

I read the database your `takeoff.record` wrote (your ignored real-server test, run against an installed `shilpid`, 11 normal tests passing as well) with O-CoreOp's reader. Full detail is in [TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md) section 10; the records are saved as `BBSDesktop/AQC.CadImport.Tests/fixtures/odraft-takeoff-records.json`.

**It agrees with the contract.** Header `complete`, id layout, kinds, and your slot for the drawing id equals the slot O-CoreOp computes. All seven rows read; the take-off imports; importing again changes nothing. Thank you for the unit tests on the slot vectors: both sides now agree on them independently.

**Four things to change in O-Draft, in order of how much they matter.** O-CoreOp copes with each for now (so nothing is blocked), but a computed value is a guess and the preview marks it "check":

1. **Write `level` on room rows** (the walls, openings, columns, lintels and slabs have it; rooms do not). Today a room only imports because the file happens to have one floor.
2. **Write `length_mm` and `breadth_mm` on room rows** (the flooring row needs both; `area_m2` alone is not enough). A rectangular room: its two sides. Any other shape: send `area_m2` and the bounding sides and say which (a field `"rectangular": false`). O-CoreOp currently derives them from the record's box, which is exact only for rectangles.
3. **Write `wall_ref` on openings** (you list this as not done). Without it an opening does not deduct from a wall, which is the point of the opening rows. O-CoreOp currently guesses the nearest wall by position (box widened by 150 mm); a wrong guess changes a masonry quantity.
4. **Write `thickness_mm` on wall rows.** The wall row has the layer (`MEASURE-FULLBRICK`) but no thickness, so O-CoreOp assumes 230 mm. If a layer implies a thickness, say it in the row.

Smaller: `opening_schedule` has `"kind": "Ventilator"` for V1; the contract says a ventilator is a window, so either send `Window` or keep `Ventilator` (O-CoreOp now treats both as windows). The header's `levels` rows carry `ceiling_mm` and `lintel_bottom_mm` as well; that is fine, they are ignored. `source.drawing` was empty for an unsaved drawing; recording an unsaved drawing could be refused, or named `(unsaved)`.

**Not yet checked, and worth doing on your side:** run `takeoff.record` with `annotate` against the real `shilpid` (your tests use a stand-in plotter, so no real PDF has crossed the server yet); record **two** drawings into one server and one of them linked via `project_ref` (company file); a drawing with more than one floor. Send me the database (or run the same test and keep the file) and I will read it with the same reader.

**Your "not done" items:** the check for two drawings measuring the same floor is built on the O-CoreOp side as a warning in the import preview (not on yours). PDF underlay and calibration are still the next piece of work and still block PDF-only projects.

## 9. Reply to the parametric walls hand-off (2026-10-19)

Your hand-off for parametric walls ([HANDOFF-O-COREOP-PARAMETRIC-TAKEOFF.md](HANDOFF-O-COREOP-PARAMETRIC-TAKEOFF.md), copied here unchanged) was read and the row shapes in it are now tests (`ParametricWallTests`, 11). I also built your engine from a copy of your working tree, ran `param.wall.new`, `param.commit` and `takeoff.record` on a metric drawing with a 5 m wall, and read the file it wrote with O-CoreOp (kept as `BBSDesktop/AQC.CadImport.Tests/fixtures/odraft-parametric.takeoff.vdb`; your repository was not touched).

**Answers to your questions (section 9 of your document)**

1. **Optional `thickness_mm` on kind 3: accepted, no version bump.** It was already read when present. An additive optional field is not a contract change (see Versioning in [TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md)).
2. **Section 4A now, 4B accepted when you want it.** O-CoreOp derives plaster, paint and shuttering itself and ignores `finish`. If you add `material` it is used: `brick.full` and `brick.half` set the thickness (230 and 115 mm) when none is given, `block.concrete` sets the unit type to Cement Block (200 mm assumed and flagged when no thickness), and `rcc` is **skipped** with "an RCC wall is not masonry; add it under RCC walls". `component_type`, `schema_version` and the provenance fields are ignored. None of them is required.
3. **`PW-*` refs need no reserving.** Any string `ref` that is stable and unique within the drawing works; O-CoreOp stores it as `source_ref` and never parses it. A `PW-` wall and a `W`+handle wall can be in one drawing (tested), and a `PW-` wall that stops being recorded is listed as "no longer in the drawing" and kept.
4. **Round trip: done, one real mismatch found (below).**

**Brick take-off walls now get their thickness from the layer.** `MEASURE-FULLBRICK` is 230 mm and `MEASURE-HALFBRICK` is 115 mm (the 4.5 in wall, which AQC measures as its 110 mm build and says so in the preview). Other layers with no `thickness_mm` are still 230 mm, flagged "assumed, check it". This answers request 4 of section 8 above for the layers you use; sending `thickness_mm` on brick rows is still the cleaner fix.

**Mismatch found: the `.vdb` file your file mode writes cannot be opened by `shilpid`.** `takeoff.record { path }` writes an `aad-vdb` file with format **version 2**; the installed `shilpid` (0.2.0) reads only its own **version 1** and answers `unsupported data file version 2`. The record layout is the same (magic, version, count, records; your grid trailer is ignored), only the number differs. Consequences and what O-CoreOp did:

- O-CoreOp now reads `.vdb` files **itself** (`VdbFile`, versions 1 and 2) with no server, so a file O-Draft saves opens in the Take-off page. Checked in the real app against your file.
- **For you:** anything else that serves your file with `shilpid` fails. Either write version 1 in file mode when the payloads are take-off JSON text, or tell the ShilpiDB owner that `shilpid` should accept version 2 (it ignores the trailer already). Server mode is unaffected: records you send to a running `shilpid` are saved in its own format.
- **File mode writes slot 0** (ids `(kind << 56) | handle`, the original one-drawing-per-file layout) while server mode uses the hashed slot. O-CoreOp accepts slot 0 for a file that holds one drawing. Do not use slot 0 on the shared server: two drawings would collide.

**Also seen in the same run:** the parametric wall's record has the box of its face lines, the row `{ ref "PW-wall-1", level, length_mm 5000.0, height_mm 3000.0, thickness_mm 230.0, layer "A-WALL" }`, and imports as one masonry row (230 mm, 5000 by 3000) with no warning. The header carried `state: complete`. The warnings your command printed (no saved revision, no rooms, no openings) are correct for that drawing.

**Still open for you:** `wall_ref` on openings, room level and size, and the real annotated PDF (sections 2 and 8 above). Room level and size and `wall_ref` are still guessed here from boxes and flagged "check it".
