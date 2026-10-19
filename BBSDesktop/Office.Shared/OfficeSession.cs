// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Office.Shared;

/// <summary>An error the office engine reported. The message is written to be shown to the person using the app.</summary>
public sealed class OfficeException : Exception
{
    public OfficeException(int status, string message) : base(message) => Status = status;

    /// <summary>1 a domain error, 2 a bad request, 3 an invalid handle, 4 an internal error.</summary>
    public int Status { get; }
}

/// <summary>
/// One session with the office engine (office.dll). It follows the contract in the O-Studio repository, docs/office-abi.md:
/// create a session, call operations by name with a JSON object, destroy the session. A session holds at most one company file open.
/// </summary>
public sealed class OfficeSession : IDisposable
{
    public const uint ApiVersion = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeResult
    {
        public int Status;
        public IntPtr Data;
        public UIntPtr DataLength;
        public IntPtr Error;
    }

    private static class Native
    {
        private const string Library = "office";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint office_api_version();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern NativeResult office_session_create([MarshalAs(UnmanagedType.LPUTF8Str)] string appDir);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern NativeResult office_session_destroy(ulong handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern NativeResult office_call(ulong handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string op, [MarshalAs(UnmanagedType.LPUTF8Str)] string? argsJson);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern void office_result_free(NativeResult result);
    }

    private readonly object _gate = new();
    private ulong _handle;

    private OfficeSession(ulong handle) => _handle = handle;

    /// <summary>Folder containing office.dll, set once at start so the loader finds it. Without it the usual search path is used.</summary>
    public static void UseLibraryFolder(string folder)
    {
        NativeLibrary.SetDllImportResolver(typeof(OfficeSession).Assembly, (name, _, _) =>
        {
            if (name != "office")
                return IntPtr.Zero;
            var path = Path.Combine(folder, "office.dll");
            return File.Exists(path) && NativeLibrary.TryLoad(path, out var handle) ? handle : IntPtr.Zero;
        });
    }

    /// <summary>True when office.dll can be loaded and speaks the version this build knows.</summary>
    public static bool IsAvailable()
    {
        try
        {
            return Native.office_api_version() == ApiVersion;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Starts a session. <paramref name="appDir"/> holds only the recent-companies list and temporary working copies.</summary>
    public static OfficeSession Create(string appDir)
    {
        Directory.CreateDirectory(appDir);
        var json = Take(Native.office_session_create(appDir));
        return new OfficeSession(json!["handle"]!.GetValue<ulong>());
    }

    private static JsonNode? Take(NativeResult result)
    {
        try
        {
            if (result.Status != 0)
            {
                var message = result.Error == IntPtr.Zero ? "The office engine reported an error." : Marshal.PtrToStringUTF8(result.Error) ?? "The office engine reported an error.";
                throw new OfficeException(result.Status, message);
            }
            if (result.Data == IntPtr.Zero || result.DataLength == UIntPtr.Zero)
                return null;
            var bytes = new byte[(int)result.DataLength];
            Marshal.Copy(result.Data, bytes, 0, bytes.Length);
            return JsonNode.Parse(Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            Native.office_result_free(result);
        }
    }

    /// <summary>Runs an operation. The arguments are a JSON object (or null); the result is the engine's JSON, or null.</summary>
    public JsonNode? Call(string operation, JsonObject? args = null)
    {
        lock (_gate)
        {
            if (_handle == 0)
                throw new ObjectDisposedException(nameof(OfficeSession));
            return Take(Native.office_call(_handle, operation, args?.ToJsonString()));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle == 0)
                return;
            var handle = _handle;
            _handle = 0;
            try
            {
                Take(Native.office_session_destroy(handle));
            }
            catch (OfficeException)
            {
                // The session is gone either way; there is nothing useful to do with a failure to close it.
            }
        }
    }
}
