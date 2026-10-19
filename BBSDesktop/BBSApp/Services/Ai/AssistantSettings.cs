// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Security.Cryptography;
using System.Text;

namespace BBSApp.Services.Ai;

/// <summary>
/// Persists the assistant's Anthropic API key under %LocalAppData%\O-CoreOp\,
/// encrypted with Windows DPAPI (<see cref="DataProtectionScope.CurrentUser"/>).
/// The stored blob can only be decrypted by the same Windows user on the same
/// machine. A pre-DPAPI plaintext key from an earlier build is migrated on read.
/// </summary>
public static class AssistantSettings
{
    private static string Dir =>
        Branding.AppDataDirectory;

    private static string DatFile => Path.Combine(Dir, "assistant.dat");        // DPAPI-encrypted blob
    private static string LegacyPlaintextFile => Path.Combine(Dir, "assistant.key"); // pre-DPAPI plaintext

    /// <summary>The stored API key, or null if none is set. Setting null/blank clears it.</summary>
    public static string? ApiKey
    {
        get
        {
            try
            {
                if (File.Exists(DatFile))
                {
                    var plain = ProtectedData.Unprotect(
                        File.ReadAllBytes(DatFile), null, DataProtectionScope.CurrentUser);
                    var text = Encoding.UTF8.GetString(plain).Trim();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }

                // Migrate a plaintext key written by an earlier build, then remove it.
                if (File.Exists(LegacyPlaintextFile))
                {
                    var text = File.ReadAllText(LegacyPlaintextFile).Trim();
                    TryDelete(LegacyPlaintextFile);
                    if (string.IsNullOrWhiteSpace(text)) return null;
                    Store(text);
                    return text;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (string.IsNullOrWhiteSpace(value))
                    TryDelete(DatFile);
                else
                    Store(value.Trim());
                TryDelete(LegacyPlaintextFile);
            }
            catch
            {
                // Best effort — the assistant simply stays disabled if this fails.
            }
        }
    }

    private static void Store(string key)
    {
        Directory.CreateDirectory(Dir);
        var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(DatFile, enc);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}
