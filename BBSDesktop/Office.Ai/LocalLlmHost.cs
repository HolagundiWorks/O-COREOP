// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

namespace Office.Ai;

/// <summary>
/// The one model per host process, started the first time the AI Core listener answers, and ended when the host stops.
/// </summary>
public static class LocalLlmHost
{
    static readonly SemaphoreSlim Gate = new(1, 1);
    static LocalLlm? _llm;

    /// <summary>True when the model file and the llama.cpp server are both installed (the model itself may not be loaded yet).</summary>
    public static bool Installed => LocalModel.Find() is not null && LocalModel.FindServer() is not null;

    public static bool Loaded => _llm is not null;

    /// <summary>The model, loading it on first use. Throws with a message fit for the reply card when something is missing.</summary>
    public static async Task<LocalLlm> GetAsync(CancellationToken ct = default)
    {
        if (_llm is { } ready) return ready;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_llm is { } again) return again;
            string? model = LocalModel.Find();
            if (model is null)
                throw new InvalidOperationException($"The assistant model is not installed. Put {LocalModel.FileName} in {LocalModel.SharedDirectory}.");
            string? server = LocalModel.FindServer();
            if (server is null)
                throw new InvalidOperationException($"The model runtime is not installed. Unpack llama.cpp {LocalModel.RuntimeRelease} (Vulkan, Windows x64) into {LocalModel.RuntimeDirectory}.");
            if (LocalModel.Problem(model) is { } bad) throw new InvalidOperationException(bad);
            return _llm = await LocalLlm.StartAsync(model, server, ct).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    public static void Shutdown()
    {
        _llm?.Dispose();
        _llm = null;
    }
}
