# Changelog

All notable changes to **AQC-Core** are documented here.
This project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed (2026-10-29)
- **The project file is a ShilpiDB file.** A project is the `.vdb` beside the drawing, the one O-Draft records its take-off in; the estimate, running bills, tasks, quantities, purchase orders and the rest are stored in it as record kind `0x60` and every other record is kept on save. File > Open ShilpiDB project, File > Open O-Draft's project (O-Draft publishes its active drawing in `%LOCALAPPDATA%\OSASctive-project.json`; O-CoreOp follows it when nothing is unsaved). Open company, Company projects and Close company are gone from the File menu. The Take-off page reads the project's own file and no longer needs the shared server. Old `.bbsproj` files still open. See `docs/PROJECT-FILE.md`. Tests: `ShilpiProjectFileTests`, `ActiveProjectTests`.

### Added (2026-10-29)
- **Local assistant model.** The Ask bar now runs on Olmo 3 7B Instruct on this computer: `Office.Ai` starts llama.cpp's `llama-server` hidden on loopback (ends with the app) and `OlmoChat` builds the model's own prompt and reads its function calls. A stored Anthropic key is used only when the model is not installed. O-Draft reaches the same model through the suite's local AI Core contract on `127.0.0.1:8742` while this app is open. See `docs/LOCAL-MODEL.md`. Tests: `OlmoChatTests`.
- **Phases from O-Draft.** The estimation phase and running-bill phase a dimension was given in O-Draft (`phase`, `bill_phase` on the take-off row) are imported onto the masonry, column, lintel and slab rows and shown as the "Est. phase" and "Bill phase" columns. A phase changed in the drawing updates the row on re-import without touching its geometry hash or a row edited here; a file with no phases leaves a phase typed here alone. Openings take their wall's phases in O-Draft. Tests: `PhaseImportTests`.
- **Estimate revision and phase split.** Every calculation of the estimate is a revision (Rev 0 for the first, then one more each time) shown beside the total and stored with it. The estimate lines keep their phases and the screen totals the civil items by estimation phase and by running-bill phase (materials and steel are derived and carry none).
- **Running-bill revision.** A certified RA bill can be reopened as its next revision (Accounts > Revise): it keeps its number and bill number, is unlocked until certified again, and shows `Rev n` in the list, the page and the PDF. The bill also carries a `billing_phase` name (stored; no picker yet).
- **Phase pickers.** The estimate has an "Estimation phase" picker (All phases, or one: only the items given that phase in O-Draft are estimated, and the estimate is named with it; items without a phase, including the derived finishes, are left out of a single phase). An RA bill has a "Running bill phase" picker (the defaults plus any phase chosen in O-Draft; typed names are kept) shown in the bill PDF. Compiled; not yet used in the running app.
- `Office.Shared.ProjectPhases`, `PhaseTotals` and `RevisionLabels`: work stages, billing phases, phase totals and revision labels, with tests (`ProjectPhasesTests`).

- **Data model is an O-Dynamo graph, built into the app.** The Data model tab draws the work items and their link rules as a graph. Each component has input parameters on its left and output parameters on its right, coloured by what they carry: yellow linear, blue area, red volume (grey count for doors and windows). A connection joins an output to an input of the same colour and is stored as a link rule with that measure, so one component can feed different items from different outputs: brick masonry takes volume in and gives length (skirting, DPC), area both faces (plaster, paint) and volume (the bricks). Right-click the canvas > Add component > main component > sub component (text menus; the ribbon has the same menus), and click a link to edit its measure, factor and rate. Which main component may feed which is fixed (RCC cannot be joined to masonry; formwork follows RCC by itself; nothing feeds RCC; finishes follow the surface they are applied to); a refused connection says why, and a link already in a project that breaks the rules is kept but drawn red with a warning. The catalogue and rules are in `Office.Shared.ComponentCatalog` (tests: `ComponentCatalogTests`) and written up in `docs/DATA-MODEL-COMPONENTS.md`. Replaces the two tables (Relationships, Definitions). RCC members can be the source of a link rule (never the target): the derivation engine reads each member's concrete volume, formwork area and run length, so plaster, paint, flooring, screed, VDF and waterproofing can follow a slab or beam and a PCC bed can follow footings (`LinkTradeRegistry.Rcc`, `DerivationEngine.SeedRcc`; the Item links page lists RCC in its source list). Verified by running the engine on the sample project (slab S1 area 15.375 m² to plaster, footing F1 to PCC).
- **Navigation.** Office, Accounts and HR & resources tabs are gone (O-Studio handles them), the pages' own tab strips are hidden where the sub ribbon lists the same pages, and the assistant's `create_correspondence` tool is removed.

- **CPWD DSR case study and component specifications.** `docs/CPWD-DSR-CASE-STUDY.md` records how a DSR-style schedule is built (sub-heads, items, specifications, analysis of rates, IS 1200 measurement) and what could and could not be verified: the sources disagree on chapter numbers, so the app keeps sub-head names and no chapter, item or rate numbers. `Office.Shared.DsrCatalog` gives every data-model component its DSR sub-head, item wording, unit and measurement rule (matching what the calculators do: masonry m³ or m² to 120 mm, openings under 0.1 m² not deducted) and its materials with the standards the specifications name (cement IS 269 / IS 1489, aggregates IS 383, steel IS 1786, bricks IS 1077, blocks IS 2185, tiles IS 15622 / IS 13755 and so on). Click a component on the Data model screen to see these with the rate-book item it prices on (code, description, rate). Tests: `DsrCatalogTests`.

- **DSR item number on the rate book.** Each rate-book item has a "DSR item no." (Rate book page: a column and an edit box beside the code). It is kept when a version is cloned, saved as `dsr_item_no`, carried onto the estimate lines and printed as a "DSR item no." column in the estimate tables, the PDF abstract of cost and the CSV export; the Data model component panel shows it with the rate-book item. Empty for existing rate books until typed.

- **Compact density from O-UI.** O-CoreOp now follows the suite scale O-UI took from O-Draft: 26 px controls, 28 px ribbon tabs, 24 px menu bar, 22 px status strip, 26 px table rows, 12 / 13 / 18 type (11 for captions), 4 px corners, a 4 px spacing scale. The app's own `FluentTokens.xaml` and `HcwTheme.xaml` had kept the older 35 to 44 px sizes and were overriding the library; they now hold the library's numbers, and code-built UI (ribbons, take-off bar, tables, data model screen) reads `Hcw.Ui.SuiteScale`. New `docs/UI-SIZING.md` lists the scale, where it is read and what changed; `docs/WINUI-SHELL.md` brought up to date (sections, shortcuts, Data model page).

### Changed (2026-10-29)
- **The Take-off page has a text-only tool bar** like O-Draft's take-off bar: flat text buttons in groups with dividers (Start the shared server, Choose take-off file | Refresh, Read take-off | Fetch annotated drawing) instead of buttons scattered through the page. The two bars share the look; CoreOp's reads a recorded take-off and has no drawing tools. Compiled; not yet looked at in the running app.
- **Navigation is two ribbons of text tabs** instead of the left pane: the main ribbon lists the sections, the sub ribbon the pages of the open section. The open tab of each is the colour of what lies below it (the sub ribbon, the canvas). No icons. Ctrl+1 to 9 still jump to a section. Compiled; not yet looked at in the running app.
### Removed
- **AORMS Connect and Bridge connectors** — sign-in, Online projects, Push/Pull, Bind/Flush, hub sync badges, `Aorms.Connect` / `Aorms.Bridge` libraries, FakeAorms e2e, and Bridge-only stub hosts (`AQC.BBS` / `AQC.Estimation` / `AQC.PM`). Company-file / OSAS ticket flow, local `.bbsproj`, CAD import, and ShilpiDB take-off remain. Orphan Credential Manager entries `AORMS/Connect*` (if any) can be deleted manually.

### Changed (2026-10-19)
- **Renamed O-CoreOp** (OSAS, the Open Source Architecture Suit). The program is still `AQCCore.exe`; the installer AppId, the `.bbsproj` extension and the `aqc` AORMS session name are unchanged on purpose. New shell: menu bar, grouped left navigation pane, work area and status strip (the same structure as the other OSAS apps), the shared `Hcw.Ui` design (building needs a sibling checkout of the public `HolagundiWorks/O-UI` repository as `o-ui`), accessibility fixes from the WCAG 2.2 AA audit.
- **Take-off moved to O-Draft.** Drawings and PDFs are measured in O-Draft, which records the take-off in one shared ShilpiDB server; O-CoreOp reads it from there (see Added). The AutoCAD plugin hcwCAD-KIT is dropped.

### Removed (2026-10-19)
- The **PDF take-off panel and canvas** (import a PDF, set a scale, measure, Commit to BOQ) and the AI tools that used it. A project saved before keeps its stored take-off and its annotated drawing in reports; there is no longer a way to measure a PDF in O-CoreOp.

### Fixed (2026-10-19, found by driving the real app; see `docs/HANDOFF-O-COREOP.md`)
- *(Historical — AORMS Connect/Pull was later removed 2026-10-28.)* **Pull latest** threw (`Int32 cannot be converted to Double`) and was not atomic; it also re-sent rows it had not changed, and shuttering and link-rule rows were deleted and re-inserted on every calculation (they now have stable ids).
- *(Historical — Connect sync.)* Local file paths no longer travel to the server inside the project settings; settings changed on another computer now come in on a pull; the sync head moves only after the incoming rows are applied; the change check waits for 1.5 s of quiet.
- A drawing recorded without a name is called "drawing <slot>" instead of showing an empty title.
- Wall thickness for brick take-off walls now comes from the layer (full brick 230 mm, half brick 115 mm) and parametric walls (`PW-` refs, `thickness_mm`) from O-Draft are accepted; `.vdb` files written by O-Draft (format version 2, which `shilpid` cannot open) are read directly.
- The C++ engine now builds with clang/MinGW as well as MSVC.

### Added
- **Take-off from ShilpiDB** — **Project > Take-off (O-Draft)** lists the drawings recorded on the shared take-off server (or in a `.vdb` file, read directly, versions 1 and 2), reads one into the import preview (idempotent, one undo, nothing deleted), and fetches, verifies (SHA-256) and stores the annotated drawing PDF O-Draft generated; the stored PDFs are embedded in the project report and the estimate PDF. Several drawings per project; a warning when two drawings measure the same floor. Contract: `docs/TAKEOFF-SHILPIDB.md`.
- **CAD import and export** — File > Import from CAD reads an `hcw-aqc-bridge` v1 file (millimetres) with a preview; File > Export for CAD writes levels, project details and beam depths. Contract: `docs/bridge/`.
- **AI assistant (opt-in)** — a built-in copilot on the command bar that drives the same code
  paths a user would. It can navigate the app, summarise the project, run the deterministic C++
  engine for RCC checks (read-only), and — each behind an explicit confirmation dialog — add RCC
  member rows, add civil-BOQ measurement rows (masonry, plaster, flooring, painting, PCC, etc.),
  edit or delete a row by mark (RCC or civil), change covers/markups,
  add storeys/levels, and author/apply linked-item derivation rules. (The take-off tools
  `list_takeoff`, `commit_opening` and `read_drawing` were removed with the PDF take-off panel.) The C++
  engine stays the single source of truth for all numbers. Inert without an Anthropic API key;
  the key is entered in-app and stored encrypted (Windows DPAPI, per-user).
- **ERD export (DBML / SQL)** — a generator (`SchemaExport`) emits the full logical schema for
  drawing in [dbdiagram.io](https://dbdiagram.io): the estimation tables plus both item
  relationships — **item → material** composition (concrete → cement + fine + coarse aggregate via
  mix recipes; masonry → bricks + mortar) and **item → item** derivation (brick wall → plaster →
  paint; flooring → skirting). Trades, materials, mix ratios and the standard link-rule library are
  pulled from the live registries so the schema tracks the code. Exportable from **Item links →
  Export ERD**; a committed snapshot lives in [`docs/schema/aqc-core.dbml`](docs/schema/aqc-core.dbml)
  (and `.sql`).
- **Item links — data-driven linked-item derivation** — derive one trade's quantity from another
  (e.g. plaster = 2 × masonry face area, painting = plaster area, skirting = flooring perimeter).
  Editable rules with a standard India-practice library, a chained derivation preview, and
  Apply-to-sheets to materialise the derived quantities into the take-off so they flow into the
  BOQ and estimate (idempotent, undoable via Clear applied). The preview and Apply dialog now also
  **price the derived quantities** against the active rate book (rate/amount per line, a total cost,
  and any unpriced codes), using the same canonical rate codes as the estimate so applied link rows
  price consistently. A rule can **pin a specific rate code** or a **manual unit rate** for its
  derived lines (otherwise the target trade's canonical code is used); a manual rate prices the
  derived quantity even with no rate-book entry. The AI `list_link_rules` tool reports the derived
  cost too, and `add_link_rule` accepts the optional rate code and manual rate.

### Changed
- Project file (`.bbsproj`) format bumped to **v17** — adds the linked-item derivation rules
  (`link_rules`). Older files load unchanged and seed the standard rule library.
- **Relicensed** from Apache-2.0 to a **dual license**: GNU AGPL v3 (Community Edition) or a
  commercial license from Human Centric Works. Free for individuals, research and internal
  business use; a commercial license is required to resell, embed in a proprietary product,
  offer as SaaS without AGPL compliance, remove the branding, or ship closed-source
  modifications. See [LICENSING.md](LICENSING.md). Prior releases (1.0.x, 1.1.0) remain Apache-2.0.

## [1.1.0] — 2026-07-27

Project-management & back-office modules, and dual-persona (Project Manager / Contractor) support.

### Added
- **Personas (Project Manager / Contractor)** — one project now serves both sides of a contract.
  Declared under **File → Project settings**, each persona carries its own letterhead identity
  (company, signatory, GSTIN/PAN) and numbering prefix. Every letter, contract, RA bill and cash
  entry is tagged with the issuing persona, numbered in that persona's own financial-year series
  (e.g. `PMC/LTR/2026-27/001` vs `CON/RA/2026-27/001`), and printed on that persona's letterhead.
  A read-only badge in the ribbon shows the active persona.
- **Schedule** — CPM / PERT project scheduling with an activity network and Gantt chart.
- **Office → Correspondence** — letters, memos, notices, circulars, certificates, declarations,
  site instructions, work-order notes and interim payment certificates, with financial-year
  auto-numbering and per-persona PDF letterheads.
- **Contracts** — item-rate / lump-sum work orders and tenders, a schedule of rates, and a reusable
  standard-terms library, with retention and auto-numbering.
- **Accounts** — running-account (RA) bills with retention and **Indian statutory deductions
  (GST, TDS 194C, labour welfare cess, GST-TDS)**, certify-and-number, a cash/bank book, and a
  per-contractor ledger (order value, certified, paid, retention held, balance payable).
- **Interim Payment Certificate** — generate a PM-issued IPC from a certified RA bill; it lands in
  Correspondence referencing the bill number with its gross → deductions → net breakdown.
- **Stores** — procurement & inventory: purchase orders, goods-receipt notes, stock/inventory and
  supplier / warehouse masters.
- **HR & resources** — sites, resources, employees, attendance and payroll (with payroll register PDF).
- **Duplicate row** on the element datasheets (RCC & civil) — copy a row's fields into a new row
  below (Ctrl+D); speeds up near-identical members such as retaining-wall segments.

### Changed
- Project file (`.bbsproj`) format bumped to **v16** — adds the personas block plus the schedule,
  office, contracts, accounts, stores and HR data. Older files load unchanged: the PM persona seeds
  from the existing project company and legacy document counters fold into the PM series.

## [1.0.1] — 2026-07-26

### Added
- Annotated takeoff drawing in project/estimate report PDFs — imported plan
  marks for columns and other elements are drawn onto the exported page.

### Changed
- Report sketches now show steel arrangement (bar layout) instead of the plain
  measurement sketches.

## [1.0.0] — Initial release

First public **AQC-Core** (*Accelerated Quantity and Costing Core*) release,
by Human Centric Works, Hospet.

### Added
- C# **WinUI 3** desktop app (Fluent 2, Mica) over a C++ `bbs_engine.dll`.
- RCC bar bending schedules: columns (incl. pedestal), beams, slabs, footings,
  walls, stairs — with section sketches.
- Civil BOQ: masonry, plaster, PCC, earthwork, SSM, shuttering, flooring,
  painting, doors/windows, and more.
- PDF **drawing takeoff** — measure on an imported plan, then Commit to BOQ.
- Openings on a wall deduct from that masonry wall (IS 1200 ignore-small).
- DSR-style estimates with electrical / plumbing / escalation / consulting %,
  steel & materials purchase orders, and project report PDFs (QuestPDF).
- App-level versioned rate books; `.bbsproj` project files (JSON).
- Packaging: per-user **Setup.exe** (Inno Setup) and single-project **MSIX**.
- Apache-2.0 license.
