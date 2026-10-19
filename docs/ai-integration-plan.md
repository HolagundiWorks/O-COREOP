# AI Integration Plan — AQC‑Core

> **Note (2026-10-19):** AI phases 3b and 3c (`list_takeoff`, `commit_opening`, `read_drawing`) were removed when take-off moved to O-Draft; see [ROADMAP.md](ROADMAP.md).

**Status:** Phases 0–7 implemented. Phases 3–7's takeoff, linked‑item, level, civil‑BOQ and row‑edit tools are delivered **additively at the tool layer** (not by rewiring their UIs) — see the notes.
**Scope:** Where and how to add an AI assistant/copilot to AQC‑Core, and the seams it plugs into.
**Branch:** `claude/ai-entry-points-kgcmip`

> AQC‑Core has **no AI today** — it is a deterministic C# WinUI 3 shell over a C++
> calculation engine (`bbs_engine.dll`). This document sketches how an assistant
> would attach without disturbing that determinism. The C++ engine stays the
> single source of truth for all IS‑code math; AI **calls** it, never replaces it.

---

## 1. Architecture recap — the three chokepoints

The whole app hangs off three seams. An assistant that can reach all three can
operate the app end‑to‑end.

| Layer | Chokepoint | File |
|-------|------------|------|
| **UI flow** | `MainWindow.NavigateTo(string tag)` — one `switch` builds every page | `BBSApp/MainWindow.xaml.cs:417` |
| **State**   | `ProjectStore.Current` — one singleton holds the entire project; `ToJson()` / `LoadFrom()` round‑trip it as JSON | `BBSApp/Services/ProjectStore.cs:11`, `:211`, `:288` |
| **Math**    | `EngineClient.Generate(kind, settings, rows)` — the JSON C ABI to `bbs_engine.dll` | `BBSApp/Services/EngineClient.cs:72`, `src/api/bbs_c_api.h:24` |

Supporting seams the assistant reuses:

- **Command vocabulary** — the ribbon tags (`"columns"`, `"masonry"`, `"estimate"`, …) enumerated in `BuildRibbonModel()` (`MainWindow.xaml.cs:88`) and the Ctrl+1..9 map (`:65`).
- **Row model** — every module is an `ObservableCollection<Dictionary<string,string>>` (`ProjectStore.cs:43`). Mutations call `ProjectStore.Current.Notify()` (`:127`) so the UI refreshes.
- **Notifications** — `AppNotify` toast bus (`Services/AppNotify.cs`).
- **Freeform input** — the floating calculator FAB (`MainWindow.xaml.cs:584`, `Services/QuickCalc.cs`) — the natural home for a command/NL bar.
- **Heuristic hooks already present** — `OpeningScheduleLinker.SuggestWallMark` (`Services/OpeningScheduleLinker.cs:22`) is a rule that "guesses"; a clean spot to later swap in a model.

---

## 2. Target design

```
        ┌────────────────────────────────────────────────────────┐
        │  Command bar (upgraded calc FAB)                        │
        │  arithmetic → QuickCalc (fast path, unchanged)          │
        │  natural language → AssistantService                    │
        └───────────────┬────────────────────────────────────────┘
                        │ prompt + tool loop
                        ▼
        ┌────────────────────────────────────────────────────────┐
        │  AssistantService  (Anthropic .NET SDK, tool runner)    │
        │  system prompt = app capabilities + IS‑code guardrails  │
        └───┬───────────────┬────────────────────┬───────────────┘
            │ navigate      │ read/write state   │ validate (read‑only)
            ▼               ▼                    ▼
     IAppCommandBus   ProjectStore.Current   EngineClient.Generate
     (wraps NavigateTo)  (+ Notify)          (deterministic ground truth)
```

**Principle:** the assistant is a *thin orchestration layer over existing services*.
It gets **tools**, not new business logic. Everything it can do, a user can already
do by hand; the assistant just drives the same code paths.

---

## 3. New code (all under `BBSApp/Services/Ai/`)

### 3.1 `IAppCommandBus` — expose navigation as a callable command

Refactor `MainWindow.NavigateTo` to route through a small bus so it is reachable
from a service (today it is `private`). No behavior change for the ribbon.

```csharp
// Services/Ai/IAppCommandBus.cs
namespace BBSApp.Services.Ai;

public interface IAppCommandBus
{
    /// <summary>Navigate to a ribbon tag ("columns", "masonry", "estimate", …).</summary>
    bool Navigate(string tag);

    /// <summary>Valid navigation tags, for tool-schema enumeration and validation.</summary>
    IReadOnlyList<string> KnownTags { get; }
}
```

`MainWindow` implements it (or a thin adapter does), forwarding `Navigate` to the
existing `NavigateTo`. `KnownTags` is derived from `_tabs` so the tool schema and
the UI can never drift apart.

### 3.2 `AssistantService` — the conversation + tool loop

Uses the official **`Anthropic`** NuGet package and its `BetaToolRunner`, which
drives the request → tool-exec → loop cycle for us.

```csharp
// Services/Ai/AssistantService.cs
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Microsoft.UI.Dispatching;

namespace BBSApp.Services.Ai;

public sealed class AssistantService
{
    private readonly AnthropicClient _client = new();      // reads ANTHROPIC_API_KEY
    private readonly IAppCommandBus _bus;
    private readonly DispatcherQueue _ui;                  // marshal tool side-effects to UI thread
    private readonly List<BetaMessageParam> _history = new();

    public AssistantService(IAppCommandBus bus, DispatcherQueue ui)
    { _bus = bus; _ui = ui; }

    public async Task<string> AskAsync(string userText)
    {
        _history.Add(new() { Role = Role.User, Content = userText });

        var runner = _client.Beta.Messages.ToolRunner(new MessageCreateParams
        {
            Model     = "claude-opus-5",
            MaxTokens = 4096,
            Thinking  = new ThinkingConfigAdaptive(),         // adaptive thinking
            OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
            System    = SystemPrompt,                          // §4
            Tools     = AssistantTools.All,                    // §3.3
            Messages  = _history,
        });

        string final = "";
        await foreach (BetaMessage msg in runner)
            foreach (var block in msg.Content)
                if (block.TryPickText(out var t)) final = t.Text;

        return final;
    }
}
```

> Notes carried over from the SDK reference:
> - Model is the exact string `claude-opus-5`.
> - `Thinking = new ThinkingConfigAdaptive()` — `budget_tokens` is removed; use adaptive.
> - Effort lives inside `OutputConfig`, not top-level.
> - For long/streamed answers, switch to `client.Messages.CreateStreaming(...)` and read the final message.
> - Handle `stop_reason == "refusal"` before reading content; opt into `fallbacks: "default"` for robustness.

### 3.3 `AssistantTools` — the capability surface

Each tool is a **wrapper over an existing service**, with side effects marshalled
to the UI thread. Handlers return short JSON/text results.

| Tool | Backing call | Direction |
|------|--------------|-----------|
| `navigate(tag)` | `IAppCommandBus.Navigate` | drive UI |
| `get_project_summary()` | `ProjectStore.Current` (name, persona, levels, per‑module row counts, markups) | read |
| `get_project_json()` | `ProjectStore.Current.ToJson()` (optionally scoped to one module to bound tokens) | read |
| `add_element_row(kind, fields)` | append to the matching `ObservableCollection<Dictionary<string,string>>`, then `Notify()` | write |
| `update_setting(path, value)` | covers, diameters, cost % (`Markups`), yields | write |
| `run_calc(kind, settings, rows)` | `EngineClient.Generate` — **read‑only preview/validation** | validate |
| `get_estimate_summary()` | `ProjectStore.Current.LastEstimate` | read |

Sketch of one tool (raw JSON schema + handler, as the C# `BetaToolRunner` expects):

```csharp
// Services/Ai/AssistantTools.cs  (excerpt)
new BetaRunnableTool(
    name: "add_element_row",
    description: "Append a row to a module's table (columns, beams, masonry, …). "
               + "Fields are the same key/value pairs the data-entry grid uses.",
    inputSchema: /* { kind: enum[...], fields: object } */,
    run: input => _ui.EnqueueAsync(() =>
    {
        var col = ProjectStore.Current.CollectionFor(input.Kind); // small dispatcher on tag
        col.Add(input.Fields.ToDictionary());
        ProjectStore.Current.Notify();                            // UI refresh + dirty flag
        return $"Added 1 row to {input.Kind} (now {col.Count}).";
    }));
```

`CollectionFor(kind)` is a tiny new switch mirroring `NavigateTo` — the one piece
of glue that maps a tag to its `ObservableCollection`.

---

## 4. System prompt (guardrails)

The system prompt encodes the non‑negotiables:

- **The engine is ground truth.** Never compute bar‑bending schedules, quantities,
  or estimate totals yourself. To produce or check numbers, call `run_calc` (which
  invokes `bbs_engine.dll`) and report what it returns. Quantities follow
  IS 456 / IS 1200 conventions — always defer to the engine.
- **Mutations are explicit and reversible‑minded.** Prefer adding rows over editing
  many at once; summarise what you changed. Do not trigger Save/New/Open or
  overwrite files — those require the user.
- **Stay in scope.** Do exactly what was asked; surface a recommendation rather than
  silently expanding scope.

---

## 5. UI wedge — the command bar

Smallest change with the widest reach: upgrade the existing floating calculator
(`CalcInput` / `EvaluateCalc`, `MainWindow.xaml.cs:584`) into an **Ask / command**
bar.

- If `QuickCalc.TryEvaluate` succeeds → show the arithmetic result (today's behavior, unchanged).
- Otherwise → hand the text to `AssistantService.AskAsync` and render the reply,
  with a subtle "working" state while the tool loop runs.

No new window, no new navigation entry — it reuses the FAB that already floats over
every page.

---

## 6. Threading, config, safety

- **UI thread.** All tool side effects (`Navigate`, collection mutations, `Notify`)
  marshal through `DispatcherQueue` — the SDK calls run off the UI thread.
- **API key.** `AnthropicClient` reads `ANTHROPIC_API_KEY` from the environment;
  for a packaged desktop app, expose a field in **File → Settings** stored in
  `%LocalAppData%\AQCCore\`. The assistant is **opt‑in** and inert without a key.
- **Privacy / licensing.** `get_project_json` sends project data to a cloud service.
  Call this out in‑product (a one‑time consent), keep it opt‑in, and note the AGPL
  network clause in `LICENSING.md` if the assistant is ever hosted as a service.
- **Determinism preserved.** Because numbers only ever come back from
  `EngineClient.Generate`, the assistant cannot introduce estimation drift — it can
  only orchestrate and explain.

---

## 7. Phased rollout

| Phase | Deliverable | Risk | Status |
|-------|-------------|------|--------|
| **0** | `IAppCommandBus` refactor — route `NavigateTo` through the bus. No AI. | none (pure refactor) | ✅ done |
| **1** | Read‑only assistant: command bar → `AssistantService` with `navigate`, `get_project_summary`, `run_calc` (RCC kinds). Answer questions, drive navigation, explain checks. Opt‑in via `ANTHROPIC_API_KEY`. | low | ✅ done |
| **2** | Mutations: `add_element_row` (RCC), `update_setting` (covers, markup %, RMC flag), each gated by a modal confirmation dialog + a success toast. | medium (writes state) | ✅ done |
| **3a** | Correspondence drafting: `create_correspondence` write tool — the assistant drafts the body and inserts an editable, unnumbered draft into the office register (confirmed). | medium | ✅ done |
| **3b** | Model‑chosen opening→wall assignment: `list_takeoff` (read) + `commit_opening` (confirmed) let the assistant pick the wall and run the deterministic `OpeningScheduleLinker.Commit`. The heuristic stays as the fallback/default. | medium (tool‑layer, additive) | ✅ done |
| **3c** | Vision drawing read: `read_drawing` sends the loaded takeoff PDF to Claude (native PDF input) and returns an analysis the assistant can act on. | medium (tool‑layer, additive) | ✅ done |
| **4** | Linked‑item derivation: `list_link_rules` (read) + `add_link_rule` / `apply_links` (confirmed) let the assistant read, author and apply the data‑driven derivation model (masonry→plaster→paint, flooring→skirting) so derived quantities reach the BOQ/estimate. | medium (tool‑layer, additive) | ✅ done |
| **5** | Level awareness: `list_levels` (read) + `add_level` (confirmed) let the assistant read the storeys (heights, column clear height) and add one; member/take‑off rows target a storey via their `level` field. | low (tool‑layer, additive) | ✅ done |
| **6** | Civil‑BOQ writes: `add_civil_row` (confirmed) adds a measurement row to the civil sheets (masonry, plaster, flooring, painting, PCC, …), mirroring the RCC `add_element_row` pattern. | medium (writes state) | ✅ done |
| **7** | Row edit / delete: `edit_row` + `delete_row` (confirmed) change or remove any RCC or civil row, addressed by kind + mark. | medium (writes state) | ✅ done |

**Phase 1 notes as built:**
- Uses the official `Anthropic` .NET SDK (`claude-opus-5`, adaptive thinking) via a **manual tool loop** in `AssistantService` — the SDK's `BetaToolRunner` handler-registration API isn't in the reference docs, so the loop is hand-written to stay on documented ground.
- `run_calc` is restricted to the eight RCC kinds the engine's generate path handles (`columns`, `beams`, `pedestals`, `lintels`, `slabs`, `footings`, `walls`, `stairs`); it clones rows before `ExpandForGenerate` so preview never mutates the project. Civil BOQ uses different calculators — deferred.
- The assistant is **inert without `ANTHROPIC_API_KEY`** (`AssistantService.TryCreate` returns null); the command bar then shows a one‑line hint instead of calling out.
- **In‑app key entry (added):** on first use with no key, the command bar prompts for the Anthropic API key in a dialog and stores it via `AssistantSettings` under `%LocalAppData%\AQCCore\` (same folder as rate books). `TryCreate` reads the stored key first, then falls back to the `ANTHROPIC_API_KEY` env var. A rejected key clears itself and re‑prompts. The key is **encrypted with Windows DPAPI** (per‑user, `System.Security.Cryptography.ProtectedData`); a plaintext key from an earlier build is migrated to the encrypted blob on read.
- **Not yet implemented:** streaming the reply token‑by‑token (would improve UX but isn't required for read‑only Q&A).

**Phase 2 notes as built:**
- Writes are gated by a **hard modal confirmation** (`IAssistantConfirm`, implemented on `MainWindow` with a `ContentDialog` mirroring `About_Click`) — the dialog, not the chat, is the confirmation. The tool loop went async (`ExecuteAsync` + an async UI marshal) so a handler can await the dialog.
- `add_element_row` seeds a new row from the last row of that kind (so grades/section/level carry over), applies the caller's `fields`, stamps defaults, and auto‑assigns a fresh mark via `MemberSheetHelper` — scoped to the eight RCC kinds. On approval it appends, calls `Notify()`, toasts, and navigates to the page so the change is visible.
- `update_setting` covers the six RCC nominal covers, the four estimate markup %s, and the `concrete_from_rmc` flag — all verified `ProjectStore`/`EstimateMarkups` members; each shows an old→new diff in the dialog.
- Everything is **in‑memory only** — mutations set the dirty flag but nothing is saved until the user saves the project. Civil‑BOQ row adds, row editing/deletion, and levels are deferred (more coupling: masonry wall‑build, finish derivation, openings).

**Phase 3 notes as built:**
- **3a (done):** `create_correspondence` reuses the Phase 2 confirmation gate. The assistant writes the body itself (its own generation) and passes it in; the tool inserts an `OfficeDocument` **draft** (`Finalized = false`, no number) under the current persona into `ProjectStore.Office.Documents`, shows the would‑be number via `PreviewNumber` in the dialog, then navigates to the Correspondence page. Numbering stays a deliberate user action (finalize) — the assistant never finalizes.
- **3b (done) — model‑chosen opening→wall, additively.** Rather than rewire the synchronous takeoff UI commit path (the risky part), the value lands at the tool layer: `list_takeoff` exposes measured walls + openings + BOQ wall marks, and `commit_opening(opening_id, kind?, wall_mark?)` lets the **model** choose the wall and runs the deterministic `OpeningScheduleLinker.Commit` (confirmed, then navigates). If the model omits the wall, `SuggestWallMark` is the fallback — so the heuristic isn't removed, it's the default the AI can override with reasoning.
- **3c (done) — vision drawing read, additively.** `read_drawing(question?)` reads `Takeoff.PdfPath` and sends the **PDF straight to Claude** (native base64 PDF input — no PDF‑to‑image rendering, no `Windows.Data.Pdf`), returning an analysis of rooms/walls/openings/dimensions. This is advisory: the AI gets eyes on the drawing and can then act through the confirmed write tools. Capped at 20 MB.
- **Deliberately still deferred:** pixel‑level auto‑pre‑fill that detects geometry from the drawing and injects scaled `TakeoffItem`s into the canvas. That needs the manual scale calibration (`MmPerPx`), reliable pixel→mm mapping, and iterative visual tuning against real drawings. `read_drawing` (advisory vision) is the interim answer.

**Phase 4 notes as built:**
- **Linked‑item derivation over the tool layer.** The data‑driven derivation model (`LinkRuleBook` → `DerivationEngine`, masonry→plaster→paint, flooring→skirting) is now assistant‑drivable, additively, without touching the `Item links` UI. `list_link_rules` (read) returns the rules plus the live derived totals from `DerivationEngine.Preview`/`Totals`; `add_link_rule` (confirmed) authors a new derivation edge (source/target trade keys, basis, factor, per‑item) validated against `LinkTradeRegistry`/`LinkBasisInfo`; `apply_links` (confirmed) runs `DerivationEngine.Apply` to materialise the derived quantities into the take‑off sheets so they flow into the BOQ and estimate.
- Both writes reuse the Phase 2 confirmation gate (`IAssistantConfirm`), navigate to the `links` page on success, and stay **in‑memory only** — nothing is saved until the user saves. Apply is idempotent (prior applied rows are replaced) and undoable from the page (Clear applied); the system prompt reminds the assistant a target trade should be measured either directly or via links, not both.
- **Priced preview (added).** `list_link_rules` and both apply paths (page + tool) now price the derived quantities against the active rate book via `DerivationEngine.Price`, which reuses the estimate's canonical `EstimateCalculator.CodeForElement` mapping. Applied link rows carry an explicit `item_code` so they price on the same code the estimate would infer — no drift, and the derived cost (with any unpriced codes) is visible before applying. A rule can pin a specific rate code (`RateCodeOverride`) or a manual unit rate (`RateOverride`) for its derived lines — a manual rate flows onto the applied row (`rate`) and is honoured by the estimate (`CivilLine.RateOverride`), so a derived quantity prices even without a rate-book entry.

**Phase 5 notes as built:**
- `list_levels` returns each storey's id/name/height/slab/beam and the derived column clear height; `add_level` appends a `LevelDef` (id `Lvl{n}`, then `RenumberLevels`) behind the Phase 2 confirmation gate and navigates to the Levels page. "Select a storey" already worked — `add_element_row` takes a `level` field — so this only adds the missing "create" half; the system prompt tells the assistant to stamp the right `level` id on rows it adds.

**Phase 6 notes as built:**
- `add_civil_row(trade, fields)` appends a measurement row to the civil sheet for `trade` via `ProjectStore.SheetForTrade` (masonry, plaster, pcc, earthwork, ssm, flooring, painting, waterproofing, dpc, coping, screed, vdf, skirting, parapet, plinth_protection). Rather than seed from the last row (civil geometry is per‑row), it starts clean and lets each calculator's own field defaults (`S(r, key, default)`) fill anything the caller omits; only `mark` (auto — a per‑kind prefix + next integer) and `level` (last row's, else Lvl0) are stamped. Confirmed via the Phase 2 gate, then navigates to the trade's page. **Deliberately excluded:** masonry openings and door/window schedules — the openings/finishes coupling and schedule fields have their own flows (`commit_opening`, the takeoff), so this covers plain measurement rows only.

**Phase 7 notes as built:**
- `edit_row(kind, mark, fields)` and `delete_row(kind, mark)` operate on any RCC or civil sheet through a shared `ResolveSheet` (RCC `CollectionFor` first, else `SheetForTrade`). A row is addressed by its **mark**; `FindByMark` refuses the operation when no row — or more than one — carries that mark (marks are unique within a sheet because the add tools auto‑number them). Edit shows an old→new diff of only the changed keys and no‑ops when nothing differs; delete shows a level + dimensions summary. Both use the Phase 2 confirmation gate, are in‑memory only, and navigate to the kind's page.

Phase 0 is worth doing on its own — exposing `NavigateTo` as a command bus is a
clean improvement regardless of whether AI ships.

---

## 8. Files touched (Phases 0–1)

| File | Change |
|------|--------|
| `BBSApp/MainWindow.xaml.cs` | Implement `IAppCommandBus`; route `NavigateTo`; wire the command bar to `AssistantService`. |
| `BBSApp/Services/Ai/IAppCommandBus.cs` | *new* — command interface. |
| `BBSApp/Services/Ai/AssistantService.cs` | *new* — conversation + tool loop. |
| `BBSApp/Services/Ai/AssistantTools.cs` | *new* — tool definitions + handlers. |
| `BBSApp/Services/ProjectStore.cs` | add `CollectionFor(string kind)` helper (mirrors `NavigateTo`). |
| `BBSApp/BBSApp.csproj` | add `Anthropic` NuGet package reference. |
