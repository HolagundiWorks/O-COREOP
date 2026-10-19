// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Aorms.Connect;

namespace BBSApp.Services.Connect;

/// <summary>One Windows Credential Manager generic credential. Secrets never go in firm.db or a plain file.</summary>
public sealed class CredentialVault
{
    const uint CredTypeGeneric = 1;
    const uint PersistLocalMachine = 2;
    const int ErrorNotFound = 1168;

    readonly string _target;
    public CredentialVault(string target) => _target = target;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    static extern void CredFree(IntPtr buffer);

    /// <summary>The stored blob and user name, or null when nothing is stored (or it cannot be read).</summary>
    public (string Blob, string? User)? Read()
    {
        if (!CredRead(_target, CredTypeGeneric, 0, out var ptr)) return null;
        try
        {
            var c = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (c.CredentialBlob == IntPtr.Zero || c.CredentialBlobSize == 0) return null;
            var bytes = new byte[c.CredentialBlobSize];
            Marshal.Copy(c.CredentialBlob, bytes, 0, bytes.Length);
            return (Encoding.UTF8.GetString(bytes), c.UserName);
        }
        finally { CredFree(ptr); }
    }

    public void Write(string blob, string? user)
    {
        var bytes = Encoding.UTF8.GetBytes(blob);
        var mem = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, mem, bytes.Length);
            var c = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = _target,
                UserName = user,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = mem,
                Persist = PersistLocalMachine,
            };
            if (!CredWrite(ref c, 0))
                throw new InvalidOperationException($"Could not save the sign-in to Windows Credential Manager (error {Marshal.GetLastWin32Error()}).");
        }
        finally { Marshal.FreeHGlobal(mem); }
    }

    public void Delete()
    {
        if (!CredDelete(_target, CredTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
            throw new InvalidOperationException($"Could not remove the sign-in from Windows Credential Manager (error {Marshal.GetLastWin32Error()}).");
    }
}

/// <summary>The sign-in shared by the suite apps on this machine: target <c>AORMS/Connect</c>, user name = email.</summary>
public sealed class CredentialSharedSignInStore : ISharedSignInStore
{
    public const string Target = "AORMS/Connect";
    readonly CredentialVault _vault = new(Target);

    public SharedSignIn? Load()
    {
        try
        {
            if (_vault.Read() is not { } r) return null;
            using var doc = JsonDocument.Parse(r.Blob);
            var root = doc.RootElement;
            if (!root.TryGetProperty("refreshToken", out var t) || t.GetString() is not { Length: > 0 } token) return null;
            var email = root.TryGetProperty("email", out var e) ? e.GetString() : r.User;
            var at = root.TryGetProperty("updatedAt", out var u) && u.TryGetInt64(out var secs)
                ? DateTimeOffset.FromUnixTimeSeconds(secs) : DateTimeOffset.UtcNow;
            return new SharedSignIn(token, email, at);
        }
        catch (JsonException) { return null; }
    }

    public void Save(SharedSignIn s) =>
        _vault.Write(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["v"] = 1, ["refreshToken"] = s.RefreshToken, ["email"] = s.Email, ["updatedAt"] = s.UpdatedAt.ToUnixTimeSeconds(),
        }), s.Email);

    public void Clear() => _vault.Delete();
}

/// <summary>This app's own entry (<c>AORMS/Connect/aqc</c>): its session id, plus a fallback copy of the refresh token.</summary>
public sealed class CredentialTokenStore : IConnectTokenStore
{
    public const string Target = "AORMS/Connect/aqc";
    readonly CredentialVault _vault = new(Target);

    public StoredSession? Load()
    {
        try
        {
            if (_vault.Read() is not { } r) return null;
            using var doc = JsonDocument.Parse(r.Blob);
            var root = doc.RootElement;
            return new StoredSession(
                root.TryGetProperty("refreshToken", out var t) ? t.GetString() ?? "" : "",
                root.TryGetProperty("sessionId", out var s) ? s.GetString() : null,
                root.TryGetProperty("email", out var e) ? e.GetString() : r.User);
        }
        catch (JsonException) { return null; }
    }

    public void Save(StoredSession s) =>
        _vault.Write(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["v"] = 1, ["refreshToken"] = s.RefreshToken, ["sessionId"] = s.SessionId, ["email"] = s.Email,
        }), s.Email);

    public void Clear() => _vault.Delete();
}
