// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text;
using System.Text.Json;

namespace Office.Shared;

public enum ChatRole { System, User, Assistant, Tool }

public sealed record ChatMessage(ChatRole Role, string Content);

/// <summary>One function call the model asked for: a name and its JSON-valued arguments.</summary>
public sealed record ToolCall(string Name, IReadOnlyDictionary<string, JsonElement> Arguments);

/// <summary>
/// The Olmo 3 Instruct chat format, as plain text, so no model is needed to build or test it. It follows the model's own
/// template (allenai/Olmo-3-7B-Instruct, chat_template.jinja): ChatML turns; the system turn carries the tool signatures in
/// &lt;functions&gt;; the model answers a call as <c>&lt;function_calls&gt;name(arg=json, ...)&lt;/function_calls&gt;</c>; a tool result
/// goes back in an <c>environment</c> turn.
/// Text that came from a drawing, a project or a tool result is data: it is only wrapped here, and the special tokens in it are
/// broken up so it cannot close a turn and forge another.
/// </summary>
public static class OlmoChat
{
    public const string TurnEnd = "<|im_end|>";
    public const string AssistantOpen = "<|im_start|>assistant\n";

    /// <summary>Break the format's special tokens inside untrusted text with a zero-width space.</summary>
    public static string Neutralise(string text) =>
        text.Replace("<|im_", "<​|im_").Replace("<|endoftext|>", "<​|endoftext|>");

    /// <summary>The system turn: our instructions followed by the tool signatures (a JSON array).</summary>
    public static string SystemTurn(string instructions, string toolsJson) =>
        $"<|im_start|>system\n{Neutralise(instructions)}<functions>{toolsJson}</functions>{TurnEnd}\n";

    public static string UserTurn(string text) => $"<|im_start|>user\n{Neutralise(text)}{TurnEnd}\n";

    /// <summary>A tool result, shown to the model in an environment turn.</summary>
    public static string EnvironmentTurn(string text) => $"<|im_start|>environment\n{Neutralise(text)}{TurnEnd}\n";

    /// <summary>The whole conversation as a prompt that ends where the assistant starts answering.</summary>
    public static string Build(string instructions, string toolsJson, IEnumerable<ChatMessage> messages)
    {
        var sb = new StringBuilder(SystemTurn(instructions, toolsJson));
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case ChatRole.User: sb.Append(UserTurn(m.Content)); break;
                case ChatRole.Tool: sb.Append(EnvironmentTurn(m.Content)); break;
                case ChatRole.Assistant: sb.Append("<|im_start|>assistant\n").Append(m.Content).Append(TurnEnd).Append('\n'); break;
            }
        }
        sb.Append(AssistantOpen);
        return sb.ToString();
    }

    const string OpenTag = "<function_calls>", CloseTag = "</function_calls>";

    /// <summary>
    /// The calls in a finished answer, and the prose around them. A block that cannot be read gives no call (nothing runs).
    /// </summary>
    public static IReadOnlyList<ToolCall> ParseCalls(string answer, out string prose)
    {
        var calls = new List<ToolCall>();
        var text = new StringBuilder();
        int pos = 0;
        while (pos < answer.Length)
        {
            int open = answer.IndexOf(OpenTag, pos, StringComparison.Ordinal);
            if (open < 0) { text.Append(answer, pos, answer.Length - pos); break; }
            text.Append(answer, pos, open - pos);
            int bodyStart = open + OpenTag.Length;
            int close = answer.IndexOf(CloseTag, bodyStart, StringComparison.Ordinal);
            int bodyEnd = close < 0 ? answer.Length : close;
            ParseBlock(answer.Substring(bodyStart, bodyEnd - bodyStart), calls);
            pos = close < 0 ? answer.Length : close + CloseTag.Length;
        }
        prose = text.ToString().Trim();
        return calls;
    }

    static void ParseBlock(string body, List<ToolCall> calls)
    {
        int i = 0;
        while (i < body.Length)
        {
            while (i < body.Length && (char.IsWhiteSpace(body[i]) || body[i] == ',' || body[i] == ';')) i++;
            int nameStart = i;
            while (i < body.Length && (char.IsLetterOrDigit(body[i]) || body[i] == '_' || body[i] == '.')) i++;
            if (i == nameStart || i >= body.Length || body[i] != '(') return;
            string name = body.Substring(nameStart, i - nameStart);
            i++;
            var args = new Dictionary<string, JsonElement>();
            while (true)
            {
                while (i < body.Length && (char.IsWhiteSpace(body[i]) || body[i] == ',')) i++;
                if (i >= body.Length) return;                 // unterminated: no call
                if (body[i] == ')') { i++; break; }
                int keyStart = i;
                while (i < body.Length && (char.IsLetterOrDigit(body[i]) || body[i] == '_')) i++;
                if (i == keyStart || i >= body.Length || body[i] != '=') return;
                string key = body.Substring(keyStart, i - keyStart);
                i++;
                int valueStart = i;
                i = SkipValue(body, i);
                args[key] = ParseValue(body.Substring(valueStart, i - valueStart).Trim());
            }
            calls.Add(new ToolCall(name, args));
        }
    }

    /// <summary>Index just after one argument value: up to a top-level comma or the closing parenthesis.</summary>
    static int SkipValue(string s, int i)
    {
        int depth = 0;
        char quote = '\0';
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (quote != '\0')
            {
                if (c == '\\') i++;
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c == '"' || c == '\'') quote = c;
            else if (c == '[' || c == '{' || c == '(') depth++;
            else if (c == ']' || c == '}') depth--;
            else if (c == ')') { if (depth == 0) return i; depth--; }
            else if (c == ',' && depth == 0) return i;
        }
        return i;
    }

    static JsonElement ParseValue(string token)
    {
        string t = token switch { "True" => "true", "False" => "false", "None" => "null", _ => token };
        if (t.Length >= 2 && t[0] == '\'' && t[^1] == '\'')
            t = JsonSerializer.Serialize(t[1..^1].Replace("\\'", "'"));
        try { using var doc = JsonDocument.Parse(t); return doc.RootElement.Clone(); }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(token));   // an unquoted word: keep it as text
            return doc.RootElement.Clone();
        }
    }
}
