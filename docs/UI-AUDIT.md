# UI and feature audit — O-Draft and O-CoreOp (2026-10-31)

Cross-check of stale chrome, placeholder behaviour, and intentional stubs after the take-off toolbar move and `.osp` take-off path.

## Fixed in this pass

| Item | App | Was | Now |
|---|---|---|---|
| Retired left tool column | O-Draft | Empty `ToolColumn` + hidden `LeftToolHost` still in the grid | Column removed; canvas uses full width beside sheet rail |
| Window menu checkmarks | O-Draft | Literal `?` prefix on toggles (broken placeholder) | `ToggleMenuFlyoutItem` with real checked state |
| Help → updates | O-Draft | Missing while O-CoreOp had it | **Check for updates…** via shared `OSAS_UPDATE_CHANNEL_URL` (`o-draft` package id) |
| Graph window hint | O-Draft | "Connect Companion" (removed product) | Describes engine `companion.evaluate` + optional O-Dynamo build |
| Take-off status text | O-CoreOp | Empty branch when project not Studio-linked | Explains Studio link for header matching |
| CAD import comment | O-CoreOp | hcwCAD-KIT-only wording | Bridge v1 from O-Draft take-off or legacy export |
| `docs/SUITE.md` O-Draft layout | Both repos | Left vertical tools + take-off on one strip | Two-row top layout documented |

## Intentional stubs (not bugs)

| Item | App | Notes |
|---|---|---|
| Help → Check for updates | Both | Check-only; no download/install until org publishes channel JSON (`UpdateCheck`, `docs/PACKAGING.md`) |
| Code signing / auto-update | Suite | Roadmap; `sign.ps1` exists |
| Rate book seed rows | O-CoreOp | Placeholder **rates** in data, not UI — edit in Rate book |
| AORMS Connect / Bridge | O-CoreOp | Removed 2026-10-28; docs only |
| Graph in-process evaluate | O-Draft | Optional `ODRAFT_GRAPH` + O-Dynamo; default uses engine pipe |
| AI agent tools | O-Draft | Unregistered tools return "not implemented yet" by design |
| Electrical / xref extras | O-Draft engine | Documented "not implemented" in command modules |

## Stale naming (code, not user-facing)

| Item | Notes |
|---|---|
| `BBSApp.Services.Shilpi` | Historical name; reads `.osp` take-off via `OdpTakeoffAdapter` — rename is a large refactor, behaviour is correct |
| `BridgeFile` / `.aqcbridge` | hcw-aqc-bridge v1 contract; still valid offline path |
| `hcw*` theme keys | Shared `Hcw.Ui` bridge resources, not AORMS |

## Verified wired (sample)

- O-Draft take-off bar: draw tools, `takeoff.record`, `takeoff.maplayers`, **Sync take-off** (`takeoff.record` without annotate), phase pickers ← `takeoffPhases` in chrome JSON
- O-CoreOp take-off page: poll on file mtime, **Sync take-off**, **Reload list**, import preview, annotated PDF store
- O-CoreOp ribbon pages: Schedule, Contracts, Stores, Data model, Report — implemented (not empty shells)
- Parameters Manager, Smart blocks, layer import map, Pomodoro — host commands wired

## Follow-ups (owner / later)

- Rename `Shilpi*` types to `Odp*` when a breaking refactor is acceptable
- Publish `OSAS_UPDATE_CHANNEL_URL` JSON with `o-draft` and `o-coreop` package entries
- Sync `docs/SUITE.md` from O-CoreOp into O-Studio and O-DRAFT when editing suite docs (four-repo rule)
- O-Studio cloud connector row in SUITE.md remains **planned**
