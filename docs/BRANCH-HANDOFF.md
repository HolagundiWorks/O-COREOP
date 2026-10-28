# Branch handoff (2026-10-11)

> Update 2026-10-28: AORMS Connect / Bridge connectors and related branches are historical only — the libraries (`Aorms.Connect`, `Aorms.Bridge`), stub hosts, and sync UI were removed from `main`. See `HANDOFF-O-COREOP.md` for what is current (company file, CAD import, ShilpiDB take-off).

All remote branches listed below were reviewed and merged into `main` at the time. Nothing was deleted then; the branches stay as history.

| Branch | State | What it held (historical) |
|---|---|---|
| `claude/aqc-connect-a1` | Merged | Former `Aorms.Connect` library (since removed). |
| `feat/aorms-bridge-smoke` | Merged | Former `Aorms.Bridge.Smoke` / spike docs (since removed). |
| `claude/ai-entry-points-kgcmip` | Already in `main` | AI entry points, DPAPI-encrypted API key. |
| `claude/roadmap-continuation-l8vv8e` | Already in `main` | Roadmap phases up to row edit/delete via the assistant. |

## Current next steps
1. Company-file / OSAS ticket flow and local CAD / ShilpiDB take-off (keep).
2. Click through Import/Export CAD and Take-off with a real O-Draft export.
