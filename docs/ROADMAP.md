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
| 3b | `list_takeoff` + `commit_opening` — model-chosen opening→wall | ✅ |
| 3c | `read_drawing` — native-PDF vision read of the loaded takeoff | ✅ |
| 4 | `list_link_rules` + `add_link_rule` / `apply_links` — drive the linked-item model | ✅ |
| 5 | `list_levels` + `add_level` — read storeys and add one (confirmed); rows target a level by id | ✅ |
| 6 | `add_civil_row` — confirmed civil-BOQ writes (masonry, plaster, flooring, painting, PCC, …) | ✅ |
| 7 | `edit_row` + `delete_row` — confirmed edit/delete of any RCC or civil row, addressed by mark | ✅ |

Every write is gated by a modal confirmation dialog and stays in-memory until the
user saves. The assistant is inert without an Anthropic API key (entered in-app,
stored encrypted with Windows DPAPI, or read from `ANTHROPIC_API_KEY`).

---

## Planned next 🔭

Ordered roughly by value-to-effort.

1. **Streaming AI replies.** Render the assistant's answer token-by-token
   (`Messages.CreateStreaming`) for responsiveness on long answers. Cross-cutting:
   touches the `AskAsync` tool loop and the command-bar UI to update incrementally.

## Deferred 💤

- **Pixel-level takeoff auto-pre-fill.** Detect geometry from the drawing and
  inject scaled `TakeoffItem`s onto the canvas. Needs reliable pixel→mm mapping
  off the manual scale calibration (`MmPerPx`) and iterative visual tuning against
  real drawings. `read_drawing` (advisory vision) is the interim answer.

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

## AORMS hub bridge (D2) � in progress

Shared `Aorms.Bridge` library under `BBSDesktop/Aorms.Bridge`:
local `firm.db` (org_settings � meta_outbox � artifact_outbox) + activate/Flush
against AORMS HUB-API. See [AORMS-BRIDGE.md](AORMS-BRIDGE.md).
Open source; SaaS commercial licensing deferred.
AStudio / AConsulting will consume this package after the smoke.

---

## CAD take-off and AORMS sign-in 🚧 (2026-10-19)

Take-off from drawings is done in AADT, not here ([AADT.md](AADT.md)); AQC imports the measured facts. Everything below is built and its libraries unit-tested; **none of it has been run against the live AORMS server or a real AADT export**.

| Step | What | State |
|------|------|-------|
| Bridge contract | `hcw-aqc-bridge` v1 schema, samples and spec in [bridge/](bridge/README.md) | ✅ |
| Import from CAD | `AQC.CadImport` + File > Import from CAD…, preview, idempotent, one undo | ✅ (20 tests; app not clicked through) |
| Export for CAD | File > Export for CAD… (levels, project, beam depths) | ✅ |
| AORMS sign-in | Credential Manager stores, status chip, File > Sign in / Sign out, take-over prompt | ✅ built; live sign-in not yet tried |
| Online projects | File > Online projects…: list the studio's projects, link one to the open project, **Import CAD take-off** from the portal into the same preview | ✅ built; not yet run |
| AADT writes the file | `aqcexport` in AADT | 🔭 AADT's last port item |
| Push online / sync | Bind a local project online, edit lease, outbox, `ProjectStore` hooks (see [AORMS-CONNECT-HANDOFF.md](AORMS-CONNECT-HANDOFF.md) items 4–5) | 🔭 next |
| Rate books | Map the shared rate books onto `RateBookStore` (library `RateBooksApi` is done) | 🔭 |
| Retire the PDF take-off canvas | Owner's call | 💤 |
