# AQC-Core — Roadmap

A single view of what has shipped, what is on `main` but unreleased, and what is
planned next. Detail lives in the linked documents; this page is the map.

- Released history → [CHANGELOG.md](../CHANGELOG.md)
- AI assistant design + phased rollout → [ai-integration-plan.md](ai-integration-plan.md)
- Logical ERD — derivation + material composition, code-generated (ERD/SQL) → [schema/aqc-core.dbml](schema/aqc-core.dbml)

Legend: ✅ shipped · 🚧 on `main`, unreleased · 🔭 planned · 💤 deferred

---

## Shipped (released)

| Version | Highlights |
|---------|------------|
| **1.0.0** | WinUI 3 shell over the C++ `bbs_engine.dll`; RCC BBS (columns, beams, slabs, footings, walls, stairs); civil BOQ; PDF drawing takeoff → Commit to BOQ; DSR-style estimates + markups; rate books; project/estimate report PDFs; Setup.exe + MSIX packaging. |
| **1.0.1** | Annotated takeoff drawing in report PDFs; report sketches show steel arrangement. |
| **1.1.0** | Dual personas (PM/PMC vs Contractor) with per-persona letterheads and FY numbering; Schedule (CPM/PERT + Gantt); Correspondence; Contracts; Accounts (RA bills + Indian statutory deductions); Interim Payment Certificate; Stores; HR & payroll; duplicate-row on datasheets. `.bbsproj` **v16**. |

Licensing changed post-1.1.0 to **dual AGPL-3.0 / Commercial** (prior releases remain Apache-2.0).

---

## On `main`, unreleased 🚧

These are complete on `main` and will land in the next tagged release.

### Linked-item derivation ("Item links")
Data-driven model where one trade's quantity derives from another —
plaster = 2 × masonry face area, painting = plaster area, skirting = flooring
perimeter, and chained combinations. Editable rules with a standard
India-practice library, a chained derivation preview, and **Apply-to-sheets**
that materialises derived quantities into the take-off so they flow into the BOQ
and estimate (idempotent; undoable via *Clear applied*). The preview and Apply
dialog **price** the derived quantities against the active rate book — rate and
amount per line, a total cost, and any unpriced codes — using the same canonical
rate codes as the estimate so applied rows price consistently, and a rule can
**pin a specific rate code** or a **manual unit rate** for its derived lines (a
manual rate prices even with no rate-book entry). `.bbsproj` bumped to **v17**
(`link_rules`).

### ERD export (DBML / SQL)
A generator (`SchemaExport`) emits the **full logical ERD** as DBML (for
[dbdiagram.io](https://dbdiagram.io)) and SQL — all tables plus seed rows for the
reference/library tables (trades, materials, mix recipes, the standard link-rule
library), pulled from the live registries so the schema tracks the code. Run it
from **Item links → Export ERD**; the committed snapshot is
`docs/schema/aqc-core.dbml` (+ `.sql`).

### AI assistant (opt-in)
A copilot on the command bar that drives the same code paths a user would. The
C++ engine stays the single source of truth for every number; the assistant only
orchestrates and explains. Delivered in phases (see the plan for the "as built"
notes):

| Phase | What | Status |
|-------|------|--------|
| 0 | `IAppCommandBus` — navigation as a callable command (pure refactor) | ✅ |
| 1 | Read-only assistant: `navigate`, `get_project_summary`, `run_calc` (RCC) | ✅ |
| 2 | Confirmed mutations: `add_element_row` (RCC), `update_setting` (covers, markups, RMC flag) | ✅ |
| 3a | `create_correspondence` — drafts an editable, unnumbered office draft | ✅ |
| 3b | `list_takeoff` + `commit_opening` — model-chosen opening→wall | ⛔ removed 2026-10-19 with the PDF take-off panel |
| 3c | `read_drawing` — native-PDF vision read of the loaded takeoff | ⛔ removed 2026-10-19 (take-off is done in O-Draft) |
| 4 | `list_link_rules` + `add_link_rule` / `apply_links` — drive the linked-item model | ✅ |
| 5 | `list_levels` + `add_level` — read storeys and add one (confirmed); rows target a level by id | ✅ |
| 6 | `add_civil_row` — confirmed civil-BOQ writes (masonry, plaster, flooring, painting, PCC, …) | ✅ |
| 7 | `edit_row` + `delete_row` — confirmed edit/delete of any RCC or civil row, addressed by mark | ✅ |

Every write is gated by a modal confirmation dialog and stays in-memory until the
user saves. The assistant is inert without an Anthropic API key (entered in-app,
stored encrypted with Windows DPAPI, or read from `ANTHROPIC_API_KEY`).

---

## Backlog 🔭

Ordered roughly by value and by what unblocks what. Items marked **owner** need a decision before work starts. Detail is in [HANDOFF-O-COREOP.md](HANDOFF-O-COREOP.md).

**Run it for real first (nothing below is trustworthy until these are done)**

1. ~~Add the repository secret `HCW_UI_TOKEN` so CI builds (2.1).~~ Done: CI clones public `HolagundiWorks/O-UI` (no token).
2. Click through Import from CAD, Export for CAD and the Take-off page with a real take-off; CadImport and Office.Shared tests pass, the screens need a desktop click-through (2.4). Needs O-Draft to record a take-off first.
3. Install the new installer on a clean machine and check the in-place upgrade (2.5).

**Connect and sync (removed 2026-10-28)**

AORMS Connect / Bridge (sign-in, Online projects, Push/Pull, Bind/Flush, outbox) were removed from this repository. Do not reintroduce without an owner decision. Company-file / OSAS ticket flow stays.

**Take-off from O-Draft**

7. ~~A check for two drawings that measure the same floor~~ O-CoreOp now warns in the import preview (`CadImporter.WarnAboutOverlap`, 5 tests); whether O-Draft should also warn when recording is **owner**'s call ([TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md) section 5).
8. A notice when O-Draft has re-recorded a drawing since it was imported.
9. O-Draft side (not in this repository): `takeoff.record`, stable `refs`, slab spans and `takeoff.annotate` are built; still to do there: room level, length and breadth, `wall_ref` on openings, wall thickness, a real annotated-PDF run, PDF underlay and calibration, `aqcexport` ([HANDOFF-O-DRAFT-TAKEOFF.md](HANDOFF-O-DRAFT-TAKEOFF.md)).

**Suite joins** (O-STUDIO roadmap numbers): shared company file + live sync (2.2); `ProjectRef` / estimate `ArtifactRef` stamped on company-file saves and Estimate page (2.3); **share estimate / RA drafts to Studio** via `aqc.estimate.draft` / `aqc.ra.draft.*` ([STUDIO-DRAFT-SHARE.md](STUDIO-DRAFT-SHARE.md)); Studio inbox approve/lock still open (2.4); MCP server still open (2.5). Suite-linked CoreOp no longer creates clients/projects (use Studio).

**Product**

10. Streaming AI replies (`Messages.CreateStreaming`; touches the `AskAsync` tool loop and the command-bar UI).
11. Code signing and an update path (3.1); automated UI tests in CI (3.2); check quantities and purchase orders against a real project (3.3).
12. Accessibility passes still open: Narrator and NVDA, 520 px on every page, 200% text and high contrast, keyboard order (3.1 to 3.7 in the hand-off).
13. **owner:** licence wording and product name in `LICENSING.md` and `CONTRIBUTING.md`.

## Done recently ✅

- Take-off moved to O-Draft; the PDF take-off panel and canvas removed; the Take-off page reads one shared ShilpiDB server (several drawings per project) and stores the annotated drawing PDF; **the stored PDFs are embedded in the project report and the estimate PDF**.
- Engine builds with clang/MinGW as well as MSVC (the installer script no longer needs Visual Studio for it).
- CAD import and export; ShilpiDB take-off; the O-CoreOp shell and the shared design; company-file / OSAS ticket flow.

## Deferred 💤

- **Pixel-level take-off auto-detection.** Detecting geometry from a drawing is now O-Draft's to do; it is not planned here.

---

## Working constraints

- **Determinism is non-negotiable.** All quantities and checks come from
  `bbs_engine.dll`. New features may orchestrate or present engine output, never
  recompute it.
- **Additive-at-the-tool-layer preferred.** Recent AI phases add capability by
  wrapping existing services rather than rewiring synchronous UI paths — lower
  risk, and it keeps the hand-entry flows intact.
- **Build environment.** The app is C# WinUI 3 + a C++ engine and builds on
  Windows only (see the README for the build steps).

---

## AORMS hub bridge — removed (2026-10-28)

`Aorms.Bridge`, stub hosts, and hub Flush/Bind UI are gone. Offline CAD bridge contract remains in [bridge/](bridge/README.md).


## CAD take-off and ShilpiDB (2026-10-19; Connect removed 2026-10-28)

Take-off from drawings and PDFs is done in **O-Draft**, recorded in one shared ShilpiDB server, and read here ([TAKEOFF-SHILPIDB.md](TAKEOFF-SHILPIDB.md), [AADT.md](AADT.md)). AORMS portal sign-in / Online projects / project sync were removed.

| Step | What | State |
|------|------|-------|
| Bridge contract | `hcw-aqc-bridge` v1 schema, samples and spec in [bridge/](bridge/README.md) | ✅ |
| Import from CAD | `AQC.CadImport` + File > Import from CAD…, preview, idempotent, one undo | ✅ |
| Export for CAD | File > Export for CAD… (levels, project, beam depths) | ✅ |
| Take-off from ShilpiDB | Take-off page: shared server or `.vdb`; several drawings; company-file grouping | ✅ |
| Annotated drawing | Fetch, verify (SHA-256) and store O-Draft's PDF | ✅ |
| O-Draft writes the take-off | `takeoff.record` / annotate in O-Draft repo | 🚧 |
