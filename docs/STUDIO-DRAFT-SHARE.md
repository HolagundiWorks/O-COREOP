# Share estimate / RA bill drafts to Studio

**Status (2026-10-31):** Working quantities and money stay in the **`.osp`** (`project_doc`). When a project is linked to the company file, O-CoreOp mirrors **abstracts** into `project_documents` kind **`aqc.project`** (`project_ref`, `last_estimate`, `accounts.bills`) so O-Studio dashboards and fee screens can read them. **Approval drafts** use separate kinds (`aqc.estimate.draft`, `aqc.ra.draft.*`) via **Share draft to O-Studio**. Studio Practice → Approvals lists and locks those drafts.  
**Product rules:** Studio owns company identity and approval; CoreOp generates and shares drafts.

## 1. What CoreOp does

When a project is linked to the company file (`OSAS_COMPANY` + ticket session):

1. User calculates an estimate or edits an RA bill in CoreOp.
2. **Share draft to Studio** writes a document via `office.dll` `doc_put` on that project.
3. CoreOp does **not** treat local “Certify” as the suite approval gate. Local certify remains an interim CoreOp-only lock/number.

Company identity (clients, projects, firm fields) is read-only when suite-linked. Create client/project is disabled with “use Studio”.

## 2. Document kinds (`doc_list` / `doc_get` / `doc_put`)

| Kind | Meaning |
|---|---|
| `aqc.estimate.draft` | Latest estimate draft for the project (one document; re-share overwrites with optimistic revision) |
| `aqc.ra.draft.<bill_id>` | One draft per running-account bill (`bill_id` is CoreOp’s bill `Id`, hex without `.` or `/`) |

Existing project quantities document remains `aqc.project` (unchanged).

## 3. Envelope body (JSON)

```json
{
  "v": 1,
  "state": "draft",
  "artifact_kind": "estimate",
  "shared_at": "2026-10-28T12:00:00.0000000Z",
  "shared_by": "<suite username>",
  "artifact_ref": { "v": 1, "project": { "…" }, "kind": "estimate", "id": "…", "revision": 1, "checksum_sha256": "…", "made_from": [] },
  "summary": { "title": "…", "currency": "INR", "grand_total": 0 },
  "body": { }
}
```

| Field | Notes |
|---|---|
| `v` | Envelope schema version (`1`) |
| `state` | CoreOp always writes `draft`. Studio may later set `pending_approval` / `approved_locked` / `superseded` (Studio-owned; CoreOp does not write those yet) |
| `artifact_kind` | `estimate` or `ra_bill` |
| `shared_at` / `shared_by` | UTC ISO-8601 and ticket username |
| `artifact_ref` | Same shape as O-Studio `docs/contracts/refs.md` (`Office.Shared.ArtifactRefs`) |
| `summary` | Inbox-friendly totals without parsing `body` |
| `body` | Full estimate JSON (`EstimateCalculator.ToJson`) or RA bill JSON (`RunningBill.ToJson`). The estimate carries `revision` (0 for the first calculation) and each line its `phase` and `bill_phase`; the bill carries `revision` (0 for the first issue; a certified bill reopened with Revise keeps its number and takes the next) and `billing_phase`. These are CoreOp's own revision counters; `artifact_ref.revision` is the company-file document revision and is separate |

### Estimate `summary`

`title`, `currency` (`INR`), `grand_total`, `rate_book_version_name`

### RA bill `summary`

`title` (number or preview), `bill_id`, `currency`, `gross`, `net`, `party`

## 4. What a Studio PR must consume

| Studio work | Detail |
|---|---|
| List drafts | `doc_list` for the open project; filter `aqc.estimate.draft` and kinds starting with `aqc.ra.draft.` |
| Show inbox | Read `summary` + `shared_by` / `shared_at` / `state` |
| Approve | Set shared state to approved/locked in Studio (exact write API is Studio’s; do not unlock in CoreOp) |
| Revise | Require a new CoreOp share (new revision) rather than editing a locked body in place |
| Company mutation | Keep firm / client / project editors in Studio only |

## 5. Code map (O-CoreOp)

| Piece | Path |
|---|---|
| Kinds + envelope | `BBSDesktop/Office.Shared/StudioDraftShare.cs` |
| Suite mutation gate | `BBSDesktop/Office.Shared/CompanyOfficePolicy.cs`, `SharedCompany.SuiteLinked` |
| Share API | `CompanyService.ShareEstimateDraftAsync` / `ShareRaBillDraftAsync` |
| UI | Estimate → Share draft to Studio; Accounts → Share draft to Studio; Certify locally (interim) |
| Tests | `Office.Shared.Tests/StudioDraftShareTests.cs`, `CompanyOfficePolicyTests.cs` |

## 6. Implemented vs proposed

| Item | Status |
|---|---|
| CoreOp write draft docs | **Implemented** |
| Gate CreateClient / CreateProject when suite-linked | **Implemented** |
| Settings company fields read-only when linked | **Implemented** (already; save path now skips identity overwrite) |
| Studio inbox UI | **Proposed** (Studio) |
| Studio approve / lock / revise-version | **Proposed** (Studio) |
| CoreOp read-only while `pending_approval` / `approved` | **Proposed** (follow-up) |
| Remove local Certify entirely | **Not done** — retained as interim local numbering; labelled “Certify locally” |

## 7. CI note

CoreOp CI applies a temporary post-checkout patch for O-UI widget accessibility (`WidgetWindow` public; Pomodoro `Path` qualified) until O-UI lands the same fix.
