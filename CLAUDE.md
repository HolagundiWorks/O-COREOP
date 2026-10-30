# AQC: notes for any Claude agent in this repo

**Quantity take-off from drawings and PDFs is done in O-Draft** (repository `HolagundiWorks/O-DRAFT`, earlier AADT), **not in O-CoreOp. O-Draft writes the take-off** (`takeoff.record` / `takeoff.annotate`) **into the shared project file (`.osp`)** that both apps open; O-Draft also generates the annotated drawing PDF, which O-CoreOp stores. **O-CoreOp reads it from there** (Project > Take-off (O-Draft); row layout matches `hcw-aqc-bridge` v1 — see [docs/bridge/](docs/bridge/) and [docs/PROJECT-FILE.md](docs/PROJECT-FILE.md)). The O-CoreOp PDF take-off panel was removed on 2026-10-19. The bridge file (`hcw-aqc-bridge` v1, millimetres, [docs/bridge/](docs/bridge/)) stays as the offline path and as the format of each take-off row. Remaining trust work is product E2E (Windows GUI) and PDF underlay polish. The AutoCAD plugin hcwCAD-KIT is dropped. O-CoreOp imports the measured facts and does everything after: derived quantities, rates, estimates, bar bending schedules.

Read **[docs/AADT.md](docs/AADT.md)** before touching imports, levels, take-off or the bridge. In short:

- The importer and the `.osp` take-off reader live in `BBSDesktop/AQC.CadImport` (File > Import from CAD; take-off via `Services/Odp/OdpTakeoffAdapter.cs` and `OspTakeoff.cs`). Change them through their tests (`dotnet test BBSDesktop/AQC.CadImport.Tests`).
- Import idempotently: every imported row gets `source = "hcwcad:<drawing_id>"` and `source_ref = <ref>`. Keep the `hcwcad:` prefix: it is stored in user projects.
- `Lvl0` (plinth) is AQC's; an import never changes it. Match levels by name, then by position.
- Never import plaster, painting, skirting, shuttering or rebar: AQC derives them.
- Preview first, one undo step, never delete a row that has gone from the file.
- Do not guess AADT's commands, files or export code; nothing here has been checked against it. Do not copy code between AQC (AGPL) and AADT or hcwCAD-KIT; share only the file contract (`docs/bridge/` is the authority). Do not edit the AADT or hcwCAD-KIT repos from here.
- Say only what you have run. No "works" or "tested" without the check and the commit.
- The owner decides file formats, names, defaults and scope.
