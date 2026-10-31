// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Security.Cryptography;

namespace Office.Ai;

/// <summary>
/// Where the shared model file lives and whether it is whole. O-CoreOp and O-Draft read the same file, so it sits in one
/// per-user folder that neither app owns.
/// </summary>
public static class LocalModel
{
    /// <summary>Olmo-3-7B-Instruct, Q4_K_M, from unsloth/Olmo-3-7B-Instruct-GGUF (Apache-2.0), revision a2a9ae53f5a95087616b3f45ca96e9d2631344ee.</summary>
    public const string FileName = "Olmo-3-7B-Instruct-Q4_K_M.gguf";
    public const long Bytes = 4_471_602_048;
    public const string Sha256 = "0de0bc63fae8a73a48e167be48f149c9544bd4af61d223cd3f331c6ea664c5c4";
    public const string DownloadUrl =
        "https://huggingface.co/unsloth/Olmo-3-7B-Instruct-GGUF/resolve/a2a9ae53f5a95087616b3f45ca96e9d2631344ee/" + FileName;
    /// <summary>Environment variable naming a model file; it wins over every other place.</summary>
    public const string Env = "OSTUDIO_MODEL";

    /// <summary>%LOCALAPPDATA%\HumanCentricWorks\models, the folder both apps use.</summary>
    public static string SharedDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HumanCentricWorks", "models");

    public static IReadOnlyList<string> Candidates()
    {
        var list = new List<string>();
        var env = Environment.GetEnvironmentVariable(Env);
        if (!string.IsNullOrWhiteSpace(env)) list.Add(env);
        list.Add(Path.Combine(SharedDirectory, FileName));
        list.Add(Path.Combine(AppContext.BaseDirectory, "models", FileName));
        return list;
    }

    /// <summary>The llama.cpp release the apps are tested with (ggml-org/llama.cpp, MIT licence), Vulkan build for Windows x64.</summary>
    public const string RuntimeRelease = "b11976";
    public const string RuntimeUrl = "https://github.com/ggml-org/llama.cpp/releases/download/" + RuntimeRelease + "/llama-" + RuntimeRelease + "-bin-win-vulkan-x64.zip";
    public const string RuntimeSha256 = "fd94feb540bb6dc3c95e2dac24ef5ced3bfeb7e09175db4d6291f898158b8c10";
    /// <summary>Environment variable naming llama-server.exe; it wins over every other place.</summary>
    public const string ServerEnv = "OSTUDIO_LLAMA_SERVER";

    /// <summary>%LOCALAPPDATA%\HumanCentricWorks\runtime\llama-b11976-vulkan, shared by both apps like the model.</summary>
    public static string RuntimeDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HumanCentricWorks", "runtime", "llama-" + RuntimeRelease + "-vulkan");

    /// <summary>The first llama-server.exe found, or null.</summary>
    public static string? FindServer()
    {
        var env = Environment.GetEnvironmentVariable(ServerEnv);
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(env)) list.Add(env);
        list.Add(Path.Combine(RuntimeDirectory, "llama-server.exe"));
        list.Add(Path.Combine(AppContext.BaseDirectory, "runtime", "llama-server.exe"));
        return list.FirstOrDefault(File.Exists);
    }

    /// <summary>The first candidate that is a file, or null.</summary>
    public static string? Find() => Candidates().FirstOrDefault(File.Exists);

    /// <summary>Null when the file looks whole; otherwise what is wrong, in words fit for the status line.</summary>
    public static string? Problem(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return $"The model file {path} does not exist.";
            Span<byte> head = stackalloc byte[4];
            using (var f = info.OpenRead())
                if (f.Read(head) != 4 || head[0] != 'G' || head[1] != 'G' || head[2] != 'U' || head[3] != 'F')
                    return $"{path} is not a GGUF model file.";
            // The shipped file has a known size; a custom file through OSTUDIO_MODEL is only checked for the magic.
            if (Path.GetFileName(path).Equals(FileName, StringComparison.OrdinalIgnoreCase) && info.Length != Bytes)
                return $"{path} is {info.Length:N0} bytes, expected {Bytes:N0}: the download is incomplete.";
            return null;
        }
        catch (Exception ex) { return $"{path}: {ex.Message}"; }
    }

    /// <summary>SHA-256 of the file, to compare with <see cref="Sha256"/> (reads all 4.5 GB; run it off the UI thread).</summary>
    public static async Task<string> HashAsync(string path, CancellationToken ct = default)
    {
        await using var f = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
    }
}
