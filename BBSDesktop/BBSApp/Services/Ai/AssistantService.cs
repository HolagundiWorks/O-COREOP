// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.UI.Dispatching;
using Office.Ai;
using Office.Shared;

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

    private readonly AnthropicClient? _client;
    private readonly Func<string, int, IProgress<string>?, CancellationToken, Task<string>>? _complete;
    private readonly AssistantTools _tools;
    private readonly DispatcherQueue _ui;
    private readonly List<MessageParam> _history = new();
    private readonly List<ChatMessage> _chat = new();

    private AssistantService(AnthropicClient? client, Func<string, int, IProgress<string>?, CancellationToken, Task<string>>? complete, AssistantTools tools, DispatcherQueue ui)
    {
        _client = client;
        _complete = complete;
        _tools = tools;
        _ui = ui;
    }

    /// <summary>Why the assistant could not start, in words for the reply card (null when it started).</summary>
    public static string? LastProblem { get; private set; }

    /// <summary>
    /// Create the assistant. Olmo 3 7B is used through O-Studio's AI Core when it is running, or in-process when this app hosts solo.
    /// Without the model, a stored Anthropic key is the fallback.
    /// </summary>
    public static async Task<AssistantService?> TryCreateAsync(IAppCommandBus bus, IAssistantConfirm confirm, DispatcherQueue ui)
    {
        LastProblem = null;
        var (local, remote, problem) = await Task.Run(() => SuiteAiCoordinator.ResolveLlmAsync()).ConfigureAwait(false);
        if (problem is null && (local is not null || remote is not null))
        {
            Func<string, int, IProgress<string>?, CancellationToken, Task<string>> complete = local is not null
                ? (p, n, prog, ct) => local.CompleteAsync(p, n, prog, ct)
                : (p, n, prog, ct) => remote!.CompleteAsync(p, n, prog, ct);
            return new AssistantService(null, complete, new AssistantTools(bus, confirm, null), ui);
        }
        if (problem is not null)
            LastProblem = problem;

        var key = AssistantSettings.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(key))
        {
            var client = new AnthropicClient { ApiKey = key };
            return new AssistantService(client, null, new AssistantTools(bus, confirm, client), ui);
        }

        LastProblem = LocalModel.Find() is null
            ? $"The assistant model is not installed. Put {LocalModel.FileName} in {LocalModel.SharedDirectory}."
            : $"The model runtime is not installed. Unpack llama.cpp {LocalModel.RuntimeRelease} (Vulkan, Windows x64) into {LocalModel.RuntimeDirectory}.";
        return null;
    }

    public Task<string> AskAsync(string userText, IProgress<string>? partial = null) =>
        _complete is not null ? AskLocalAsync(userText, partial) : AskCloudAsync(userText);

    private const int MaxLocalCalls = 3;

    /// <summary>The same tool loop against the local model: it answers in prose or asks for functions, results go back as environment turns.</summary>
    private async Task<string> AskLocalAsync(string userText, IProgress<string>? partial)
    {
        _chat.Add(new ChatMessage(ChatRole.User, userText));
        string toolsJson = ToolsJson();
        string instructions = SystemPrompt + "\n- Call a function only when it is needed, using the <function_calls> format; otherwise answer in plain prose.";
        string last = "";

        for (int iter = 0; iter < MaxToolIterations; iter++)
        {
            string prompt;
            while ((prompt = OlmoChat.Build(instructions, toolsJson, _chat)).Length / 3 + LocalLlm.ReserveTokens > LocalLlm.ContextTokens && _chat.Count > 1)
                _chat.RemoveRange(0, Math.Min(2, _chat.Count - 1));        // drop the oldest turns first

            string answer = await _complete!(prompt, 700, partial, CancellationToken.None);
            var calls = OlmoChat.ParseCalls(answer, out string prose);
            _chat.Add(new ChatMessage(ChatRole.Assistant, answer));
            if (prose.Length > 0) last = prose;
            if (calls.Count == 0) return string.IsNullOrWhiteSpace(prose) ? answer : prose;

            var results = new StringBuilder();
            foreach (var call in calls.Take(MaxLocalCalls))
            {
                string result = await OnUiAsync(() => _tools.ExecuteAsync(call.Name, call.Arguments));
                results.Append(call.Name).Append(" -> ").AppendLine(result);
            }
            _chat.Add(new ChatMessage(ChatRole.Tool, results.ToString().TrimEnd()));
        }
        return string.IsNullOrEmpty(last) ? "Stopped after several tool steps without a final answer." : last;
    }

    /// <summary>The tool signatures as the Olmo template expects them: a JSON array of name, description and a parameters schema.</summary>
    private string ToolsJson()
    {
        var list = _tools.Definitions.Select(t => new Dictionary<string, object?>
        {
            ["name"] = t.Name,
            ["description"] = t.Description,
            ["parameters"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = t.InputSchema.Properties ?? new Dictionary<string, JsonElement>(),
                ["required"] = t.InputSchema.Required ?? new List<string>(),
            },
        });
        return JsonSerializer.Serialize(list);
    }

    private async Task<string> AskCloudAsync(string userText)
    {
        if (_client is null) return "The assistant is not available.";
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
        "You are the built-in assistant for O-CoreOp, a Windows desktop app for construction "
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
      + "- Drawing take-off is done in O-Draft and recorded in the project file (.osp); the user reads it in with the "
      + "Take-off page (Project › Take-off (O-Draft)). You cannot measure drawings or read PDFs.\n"
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
