// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.UI.Dispatching;

namespace BBSApp.Services.Ai;

/// <summary>
/// Read-only assistant (Phase 1): holds one conversation, drives a manual tool
/// loop against the Claude API, and marshals every tool side effect onto the UI
/// thread. The C++ engine stays the source of truth — the assistant only
/// orchestrates and explains via <see cref="AssistantTools"/>.
/// </summary>
public sealed class AssistantService
{
    private const string Model = "claude-opus-5";
    private const int MaxToolIterations = 6;

    private readonly AnthropicClient _client;
    private readonly AssistantTools _tools;
    private readonly DispatcherQueue _ui;
    private readonly List<MessageParam> _history = new();

    private AssistantService(AnthropicClient client, AssistantTools tools, DispatcherQueue ui)
    {
        _client = client;
        _tools = tools;
        _ui = ui;
    }

    /// <summary>
    /// Create the assistant if an API key is configured; otherwise return null so
    /// the feature stays inert. Opt-in by design.
    /// </summary>
    public static AssistantService? TryCreate(IAppCommandBus bus, IAssistantConfirm confirm, DispatcherQueue ui)
    {
        var key = AssistantSettings.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return null;
        var client = new AnthropicClient { ApiKey = key };
        return new AssistantService(client, new AssistantTools(bus, confirm, client), ui);
    }

    public async Task<string> AskAsync(string userText)
    {
        _history.Add(new MessageParam { Role = Role.User, Content = userText });
        string finalText = "";

        for (int iter = 0; iter < MaxToolIterations; iter++)
        {
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = 8192,
                Thinking = new ThinkingConfigAdaptive(),
                OutputConfig = new OutputConfig { Effort = Effort.Medium },
                System = new List<TextBlockParam> { new() { Text = SystemPrompt } },
                Tools = [.. _tools.Definitions.Select(t => (ToolUnion)t)],
                Messages = [.. _history],
            });

            if (response.StopReason == "refusal")
            {
                finalText = "I can't help with that request.";
                _history.Add(new MessageParam { Role = Role.Assistant, Content = finalText });
                return finalText;
            }

            // Rebuild the assistant turn for history. Thinking/redacted-thinking and
            // tool_use blocks MUST be echoed back unchanged for the round-trip.
            var assistantContent = new List<ContentBlockParam>();
            var toolResults = new List<ContentBlockParam>();

            foreach (var block in response.Content)
            {
                if (block.TryPickText(out TextBlock? text))
                {
                    finalText = text.Text;
                    assistantContent.Add(new TextBlockParam { Text = text.Text });
                }
                else if (block.TryPickThinking(out ThinkingBlock? thinking))
                {
                    assistantContent.Add(new ThinkingBlockParam
                    {
                        Thinking = thinking.Thinking,
                        Signature = thinking.Signature
                    });
                }
                else if (block.TryPickRedactedThinking(out RedactedThinkingBlock? redacted))
                {
                    assistantContent.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                }
                else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                {
                    assistantContent.Add(new ToolUseBlockParam
                    {
                        ID = toolUse.ID,
                        Name = toolUse.Name,
                        Input = toolUse.Input
                    });
                    string result = await OnUiAsync(() => _tools.ExecuteAsync(toolUse.Name, toolUse.Input));
                    toolResults.Add(new ToolResultBlockParam
                    {
                        ToolUseID = toolUse.ID,
                        Content = result
                    });
                }
            }

            // Skip an empty assistant turn (no blocks at all) — re-sending empty
            // content on the next turn can 400. When tools ran, assistantContent
            // holds their tool_use blocks, so this never drops a needed turn.
            if (assistantContent.Count > 0)
                _history.Add(new MessageParam { Role = Role.Assistant, Content = assistantContent });

            if (toolResults.Count == 0)
                return finalText;   // no tool calls this turn → final answer

            _history.Add(new MessageParam { Role = Role.User, Content = toolResults });
        }

        return string.IsNullOrEmpty(finalText)
            ? "Stopped after several tool steps without a final answer."
            : finalText;
    }

    /// <summary>Run a tool handler on the UI thread and await its result (never blocks the UI thread).</summary>
    private Task<string> OnUiAsync(Func<Task<string>> work)
    {
        var tcs = new TaskCompletionSource<string>();
        bool queued = _ui.TryEnqueue(async () =>
        {
            try { tcs.SetResult(await work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        if (!queued)
            tcs.SetException(new InvalidOperationException("UI thread unavailable."));
        return tcs.Task;
    }

    private const string SystemPrompt =
        "You are the built-in assistant for O-CoreOp (formerly AQC-Core), a Windows desktop app for construction "
      + "quantity take-off and costing (RCC bar bending schedules, civil BOQ, estimates) by "
      + "Human Centric Works, Hospet.\n\n"
      + "Ground rules:\n"
      + "- The C++ engine is the single source of truth for all numbers. Never compute bar "
      + "bending schedules, quantities, or estimate totals yourself — call run_calc and report "
      + "what it returns. Quantities follow IS 456 / IS 1200 conventions.\n"
      + "- Use get_project_summary to understand the current project before answering questions "
      + "about it. Use navigate to take the user to a relevant page when that helps.\n"
      + "- You can change project data with add_element_row (add one RCC member), add_civil_row (add "
      + "one civil-BOQ measurement row — masonry, plaster, pcc, flooring, painting, etc.; dimensions "
      + "in mm, mark auto-assigned), and update_setting (covers, markup %, concrete-from-RMC). They apply "
      + "in memory only and "
      + "require the user to approve the change in a confirmation dialog — that dialog IS the "
      + "confirmation, so call the tool with the concrete change rather than asking in chat first. "
      + "Make one clear change at a time and report exactly what changed (or that the user "
      + "cancelled). Nothing is saved until the user saves the project.\n"
      + "- You can also edit_row and delete_row (RCC or civil), addressing a row by its kind and mark "
      + "(marks are unique within a sheet); both are confirmed and fail if the mark is missing or "
      + "ambiguous. Use get_project_summary / navigate to find the mark first if unsure.\n"
      + "- You can draft office correspondence: when asked for a letter, notice, site "
      + "instruction, certificate, etc., write the full body yourself and call "
      + "create_correspondence with the type, a subject, and the body. It is added as an editable "
      + "draft under the current persona and stays unnumbered until the user finalizes it (same "
      + "confirmation dialog applies).\n"
      + "- For the drawing takeoff: use list_takeoff to see measured walls and openings, "
      + "read_drawing to visually analyze the loaded PDF, and commit_opening to link an opening "
      + "to a wall as a door/window/deduct (confirmed). When committing an opening, choose the "
      + "wall yourself from the masonry walls on that opening's level rather than relying only on "
      + "the geometric default.\n"
      + "- For linked estimation: link rules derive one trade's quantity from another (plaster from "
      + "masonry face area, painting from plaster, skirting from flooring perimeter). Use "
      + "list_link_rules to see the rules and what they currently derive, add_link_rule to create a "
      + "new derivation edge (confirmed), and apply_links to materialise the derived quantities into "
      + "the take-off so they reach the BOQ and estimate (confirmed). Warn the user that a target "
      + "trade should be measured either directly or via links, not both.\n"
      + "- For storeys: use list_levels to see the levels (ids like Lvl0, Lvl1 with heights and column "
      + "clear height) and add_level to add one (confirmed). When you add members or take-off rows for a "
      + "particular storey, pass that level id in the row's `level` field so quantities land on the right "
      + "floor.\n"
      + "- Be concise and direct. Lead with the answer; keep caveats brief.";
}
