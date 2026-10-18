# O-Metra

**O-Metra** (formerly AQC-Core) is the quantities, estimates and billing app of **OSAS, the Open Source Architecture Suit**. The repository, the executable `AQCCore.exe` and the project file extension `.bbsproj` keep their old names for now.

**AQC-Core** (*Accelerated Quantity and Costing Core*) — quantity take-off and costing by **Human Centric Works, Hospet**.

RCC bar bending schedules, civil BOQ, drawing takeoff, rate books, DSR-style estimates (with electrical / plumbing / escalation / consulting %), and purchase orders — plus back-office modules for project scheduling, correspondence, contracts, running-account billing, stores and HR.

One project can be operated as either a **Project Manager / PMC** or a **Contractor**: each persona has its own letterhead and numbering series, and every letter, bill and order is branded and numbered accordingly.

**Linked-item derivation** derives one trade's quantity from another (plaster from masonry, painting from plaster, skirting from flooring), and an **opt-in AI assistant** can navigate the app, answer questions, run the deterministic engine, and make confirmed changes — all while the C++ engine stays the single source of truth for every number.

Quantities follow IS 456 / IS 1200–derived conventions — always cross-check against drawings.

**Version:** 1.1.0 · **License:** [AGPL-3.0](LICENSE) (Community) or [Commercial](LICENSING.md) · see also [NOTICE](NOTICE) · [CHANGELOG](CHANGELOG.md) · [ROADMAP](docs/ROADMAP.md)

## Stack

| Layer | Tech |
|-------|------|
| UI | **C# WinUI 3** (Fluent 2, Mica), single-project **MSIX** |
| Engine | **C++** `bbs_engine.dll` (JSON C API) |
| Reports | QuestPDF (estimate / BOQ / PO) |

Executable name: `AQCCore.exe`

## Requirements

- Windows 10/11
- CMake 3.20+ and MSVC (for `bbs_engine`)
- .NET 8 SDK
- Windows App SDK / WinUI workload
- [Inno Setup 6](https://jrsoftware.org/isdl.php) — optional, for Setup.exe
- Developer Mode — optional, for MSIX sideload

## Build & run

```bat
cd BBSDesktop
cmake -S . -B build -G Ninja
cmake --build build --config Release --target bbs_engine bbs_tests
cd BBSApp
dotnet build -c Release -p:Platform=x64
dotnet run -c Release -p:Platform=x64
```

Or launch: `BBSApp\bin\x64\Release\net8.0-windows10.0.19041.0\AQCCore.exe`

## Installer (Setup.exe)

Per-user installer (no admin):

```powershell
cd BBSDesktop
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Output: `BBSDesktop\artifacts\installer\AQCCore-Setup-1.0.1.exe`  
Installs to `%LocalAppData%\Programs\AQC-Core\`

The version comes from `<Version>` in `BBSApp.csproj`; the script passes it to Inno Setup, so the output filename tracks it automatically. Pass `-SkipEngineBuild` when `BBSDesktop\build\bbs_engine.dll` already exists.

## MSIX (Store / sideload)

```powershell
cd BBSDesktop
powershell -ExecutionPolicy Bypass -File installer\build-msix.ps1
```

Output: `BBSDesktop\artifacts\msix\*.msix`

- **Microsoft Store:** upload the `.msix` in Partner Center (Microsoft re-signs). Match `Package.appxmanifest` `Identity` Name/Publisher to Partner Center.
- **Sideload:** trust the local cert from `build-msix.ps1`, then `Add-AppxPackage -Path …`.

## Features

| Area | What you get |
|------|----------------|
| Project | Name, location, client, prepared-by (Engineer/Architect/PMC), company, logo — **File → Project settings** |
| Engineering | Diameters, hooks, covers, civil yields — **File → Engineering settings** |
| Cost % | Electrical, plumbing, escalation, consulting fees on estimate base — **File → Cost percentages** |
| Levels | Lvl0 = plinth … LvlN; column clear height from storey geometry |
| RCC | Columns (incl. pedestal), beams, slabs, footings, walls, stairs — BBS + section sketches |
| Civil BOQ | Masonry, plaster, PCC, earthwork, SSM, shuttering, flooring, paint, doors/windows, etc. |
| Openings | Doors/windows **On wall** mark → deduct from that masonry wall (IS 1200 ignore-small) |
| Takeoff | PDF drawing measure → Commit to BOQ |
| Item links | Data-driven linked-item derivation (plaster from masonry, painting from plaster, skirting from flooring) — editable rules, chained preview, Apply-to-sheets into the BOQ/estimate |
| Rate book | App-level versioned rates (`%LocalAppData%\AQCCore\`) |
| Estimate | DSR abstract (L/B/H, area, volume) + % markups → grand total; PDF sketches appendix |
| PO / report | Steel & materials PO; project PDF with BOQ tables + sketches |
| Personas | One project operated as **PM / PMC** or **Contractor** — declared in **File → Project settings**; each has its own letterhead + numbering series (`PMC/…` vs `CON/…`) |
| Schedule | CPM / PERT scheduling with activity network + Gantt |
| Correspondence | Letters, memos, notices, circulars, certificates, declarations, site instructions, work-order notes, interim payment certificates — FY auto-numbering, per-persona letterhead |
| Contracts | Item-rate / lump-sum work orders & tenders, schedule of rates, standard-terms library, retention |
| Accounts | RA bills with retention + statutory deductions (GST, TDS 194C, labour cess, GST-TDS), certify & number, cash/bank book, per-contractor ledger |
| Stores | Purchase orders, goods-receipt notes, inventory, supplier / warehouse masters |
| HR & resources | Sites, resources, employees, attendance & payroll (payroll register PDF) |
| AI assistant | Opt-in copilot on the command bar — navigate, summarise the project, run the engine (read-only), and (each confirmed) add RCC rows, change covers/markups, draft correspondence, commit takeoff openings, read the takeoff PDF, author/apply link rules. Needs an Anthropic API key (entered in-app, stored encrypted); inert without one |
| Project file | `.bbsproj` (JSON, v17 — project info, personas, estimate markups, schedule, office, contracts, accounts, stores, HR, link rules) |

## Settings & data locations

| Data | Path |
|------|------|
| Rate books / logos | `%LocalAppData%\AQCCore\` (reads legacy `%LocalAppData%\BOQCore\` if present) |
| AI assistant API key | `%LocalAppData%\AQCCore\` — encrypted with Windows DPAPI (per-user); or the `ANTHROPIC_API_KEY` env var |
| Project | User-chosen `.bbsproj` |

## Branding

- Product: **AQC-Core** — Accelerated Quantity and Costing Core  
- Developer: **Human Centric Works, Hospet**  
- Logo: `BBSDesktop/BBSApp/Assets/logo.png`

## Licensing

AQC-Core is **dual-licensed**:

- **Community Edition** — [GNU AGPL v3](LICENSE). Free for individuals, hobbyists,
  research, and internal business use. Note the AGPL's network clause: hosting a modified
  version as a service (SaaS) requires releasing your source under the AGPL.
- **Commercial License** — required to **resell**, **embed in a proprietary product**,
  **offer as SaaS** without AGPL compliance, **remove the branding**, or **ship
  closed-source modifications**.

See [LICENSING.md](LICENSING.md). Commercial enquiries: **office@hcworks.in** · <https://hcworks.in>

## Disclaimer

Estimation tool only — not a substitute for structural design or site mix design (M30+).

The AI assistant is **opt-in** and disabled unless you supply an Anthropic API key. When used, it sends the parts of your project it needs to Anthropic's API to answer — keep it off for confidential work if that is a concern. It never computes quantities itself: all numbers come from the deterministic C++ engine.
