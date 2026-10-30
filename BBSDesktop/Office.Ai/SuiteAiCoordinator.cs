// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

namespace Office.Ai;

/// <summary>
/// One Olmo host per machine on loopback :8742. O-Studio starts it when a company workspace is open; sibling apps use <see cref="RemoteLlm"/>.
/// O-CoreOp alone may still host when the port is free (legacy solo use).
/// </summary>
public static class SuiteAiCoordinator
{
    static AiCoreListener? _host;

    public static bool IsHost => _host is not null;

    public static bool PreferClient =>
        string.Equals(Environment.GetEnvironmentVariable("OSAS_AI_ROLE"), "client", StringComparison.OrdinalIgnoreCase);

    public static bool PreferHost =>
        string.Equals(Environment.GetEnvironmentVariable("OSAS_AI_ROLE"), "host", StringComparison.OrdinalIgnoreCase);

    /// <summary>Start listening when this process should own the model (O-Studio workspace, or solo CoreOp).</summary>
    public static AiCoreListener? StartHostIfNeeded(int port = AiCoreListener.DefaultPort)
    {
        if (PreferClient)
            return null;
        if (_host is not null)
            return _host;
        _host = AiCoreListener.TryStart(port);
        return _host;
    }

    public static void StopHost()
    {
        _host?.Dispose();
        _host = null;
        LocalLlmHost.Shutdown();
    }

    /// <summary>Local in-process llama when this app hosts; remote when another app already serves :8742.</summary>
    public static async Task<(LocalLlm? local, RemoteLlm? remote, string? problem)> ResolveLlmAsync(CancellationToken ct = default)
    {
        if (PreferClient || (!PreferHost && await RemoteLlm.IsAvailableAsync(ct).ConfigureAwait(false)))
        {
            var remote = await RemoteLlm.ConnectAsync(ct).ConfigureAwait(false);
            if (remote is not null)
                return (null, remote, null);
            if (PreferClient)
                return (null, null, "Open O-Studio and sign in — the assistant model (Olmo 3 7B) runs there.");
        }

        if (!LocalLlmHost.Installed)
        {
            return (null, null, LocalModel.Find() is null
                ? $"The assistant model is not installed. Put {LocalModel.FileName} in {LocalModel.SharedDirectory}."
                : $"The model runtime is not installed. Unpack llama.cpp {LocalModel.RuntimeRelease} (Vulkan, Windows x64) into {LocalModel.RuntimeDirectory}.");
        }

        StartHostIfNeeded();
        try
        {
            var local = await LocalLlmHost.GetAsync(ct).ConfigureAwait(false);
            return (local, null, null);
        }
        catch (Exception ex)
        {
            return (null, null, ex.Message);
        }
    }
}
