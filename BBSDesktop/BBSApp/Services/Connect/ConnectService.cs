// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Aorms.Connect;

namespace BBSApp.Services.Connect;

public enum ConnectState
{
    /// <summary>Not signed in: O-CoreOp works on its own (Community).</summary>
    SignedOut,
    SignedIn,
    /// <summary>Signed in on another computer; the user can take the session over here.</summary>
    SessionReplaced,
    /// <summary>Signed in, but the studio isn't on a connected plan: Community mode.</summary>
    NotConnected,
}

/// <summary>
/// The app's AORMS sign-in (suite sign-in: shared Credential Manager entry + this app's own session).
/// Uses Aorms.Connect; the old Aorms.Bridge flow is separate and untouched.
/// </summary>
public static class ConnectService
{
    const string DefaultUrl = "https://aorms.in";
    static ConnectClient? _client;

    public static ConnectState State { get; private set; } = ConnectState.SignedOut;
    public static string? LastNotice { get; private set; }
    public static SessionInfo? Session => _client?.Session;
    public static string BaseUrl => Environment.GetEnvironmentVariable("AORMS_URL")?.Trim() is { Length: > 0 } u ? u : DefaultUrl;
    /// <summary>Email of the saved suite login, to prefill the sign-in box.</summary>
    public static string? RememberedEmail { get { try { return new CredentialSharedSignInStore().Load()?.Email; } catch { return null; } } }
    public static string ClientLabel => $"O-CoreOp on {Environment.MachineName}";
    public static event Action? Changed;

    internal static ConnectClient Client()
    {
        if (_client is not null) return _client;
        var c = new ConnectClient(new Uri(BaseUrl.TrimEnd('/') + "/"), new CredentialTokenStore(), shared: new CredentialSharedSignInStore(), app: "aqc");
        c.SessionReplaced += () => Set(ConnectState.SessionReplaced, "Signed in on another computer.");
        c.NotConnected += e => Set(ConnectState.NotConnected, NotConnectedMessage(e));
        c.SignedOut += () => Set(ConnectState.SignedOut, "Your AORMS sign-in has ended. Sign in again.");
        return _client = c;
    }

    static void Set(ConnectState s, string? notice)
    {
        State = s; LastNotice = notice;
        Changed?.Invoke();
    }

    /// <summary>One line for the status badge.</summary>
    public static string StatusText => State switch
    {
        ConnectState.SignedIn => $"AORMS · {Session?.Studio.Name ?? Session?.Account.Email}",
        ConnectState.SessionReplaced => "AORMS · taken over elsewhere",
        ConnectState.NotConnected => "AORMS · Community",
        _ => "AORMS · signed out",
    };

    public static string Details => State switch
    {
        ConnectState.SignedIn when Session is { } s =>
            $"{s.Account.Email} · {s.Studio.Name} · role {s.Role} · plan {s.Entitlement.Plan ?? "?"}{(s.CanWrite ? "" : " (read-only)")}",
        _ => LastNotice ?? "Not signed in. O-CoreOp works on its own until you sign in.",
    };

    /// <summary>Silent sign-in at app start. Never takes a session over from another computer.</summary>
    public static async Task RestoreAsync()
    {
        try
        {
            var c = Client();
            switch (await c.RestoreAsync().ConfigureAwait(true))
            {
                case RestoreOutcome.Restored: Set(ConnectState.SignedIn, null); break;
                case RestoreOutcome.SessionReplaced: Set(ConnectState.SessionReplaced, "You are signed in on another computer. Sign in here to use AORMS from this one."); break;
                case RestoreOutcome.NotConnected: break; // NotConnected event already set the state
                case RestoreOutcome.SignedOut: Set(ConnectState.SignedOut, "Your AORMS sign-in has ended. Sign in again."); break;
                default: Set(ConnectState.SignedOut, null); break;
            }
        }
        catch (ConnectException e) { Set(State, Friendly(e)); }   // e.g. AORMS unreachable: keep working, say why
        catch (Exception e) { Set(State, "Could not restore the AORMS sign-in: " + e.Message); }
    }

    /// <summary>Password sign-in. Returns null on success, or a message to show the user.</summary>
    public static async Task<string?> SignInAsync(string email, string password)
    {
        try
        {
            await Client().SignInAsync(email.Trim(), password, ClientLabel).ConfigureAwait(true);
            Set(ConnectState.SignedIn, null);
            return null;
        }
        catch (ConnectException e) when (e.IsNotConnected)
        {
            Set(ConnectState.NotConnected, NotConnectedMessage(e));
            return NotConnectedMessage(e);
        }
        catch (ConnectException e) { return Friendly(e); }
        catch (HttpRequestException) { return $"Could not reach AORMS at {BaseUrl}. Check your connection."; }
        catch (TaskCanceledException) { return $"AORMS at {BaseUrl} did not answer in time."; }
        catch (Exception e) { return "Sign-in failed: " + e.Message; }
    }

    /// <summary>"Sign in here" after the session was taken over on another computer.</summary>
    public static async Task<string?> TakeOverAsync()
    {
        try
        {
            await Client().TakeOverSessionAsync(ClientLabel).ConfigureAwait(true);
            Set(ConnectState.SignedIn, null);
            return null;
        }
        catch (ConnectException e) when (e.IsNotConnected) { Set(ConnectState.NotConnected, NotConnectedMessage(e)); return NotConnectedMessage(e); }
        catch (ConnectException e) { return Friendly(e); }
        catch (Exception e) { return "Could not take over the session: " + e.Message; }
    }

    /// <summary>The studio's AORMS projects (needs a connected sign-in). Throws a ConnectException the caller can show with <see cref="Describe"/>.</summary>
    public static Task<List<OnlineProject>> ListProjectsAsync() => new ProjectsApi(Client()).ListAsync();

    /// <summary>The newest CAD take-off (O-Draft's <c>aqcexport</c> file) published for a bound project, as bridge JSON; null when none.</summary>
    public static async Task<string?> PullCadTakeoffAsync(string aqcId)
    {
        var r = await new CadBridgeApi(Client()).DownloadLatestAsync(aqcId).ConfigureAwait(true);
        return r?.json;
    }

    /// <summary>A message for any failure from a Connect call.</summary>
    public static string Describe(Exception e) => e switch
    {
        ConnectException c when c.IsNotConnected => NotConnectedMessage(c),
        ConnectException c when c.IsSessionReplaced => "You are signed in to AORMS on another computer. Use File > Sign in to AORMS to take it over.",
        ConnectException c => Friendly(c),
        HttpRequestException => $"Could not reach AORMS at {BaseUrl}. Check your connection.",
        TaskCanceledException => $"AORMS at {BaseUrl} did not answer in time.",
        _ => e.Message,
    };

    /// <summary>Signs out of the shared AORMS login (the other suite apps find it empty and ask to sign in).</summary>
    public static void SignOut()
    {
        try { _client?.SignOut(); } catch (Exception e) { LastNotice = "Sign-out could not clear the saved login: " + e.Message; }
        if (_client is null)
        {
            try { new CredentialTokenStore().Clear(); new CredentialSharedSignInStore().Clear(); } catch { /* nothing stored */ }
        }
        Set(ConnectState.SignedOut, null);
    }

    static string NotConnectedMessage(ConnectException e) => e.Reason switch
    {
        "EXPIRED" => "Your studio's AORMS plan has expired. O-CoreOp continues in Community mode.",
        "NO_STUDIO" => "This account isn't part of an AORMS studio. O-CoreOp continues in Community mode.",
        _ => "Your studio's AORMS plan doesn't include connected mode (Studio plan or higher). O-CoreOp continues in Community mode.",
    };

    static string Friendly(ConnectException e) => e.Code switch
    {
        "invalid_credentials" => "Wrong email or password.",
        "not_linked" => "This account isn't linked to an AORMS studio yet.",
        "pending" => "This account is waiting for approval.",
        "rate_limited" => $"Too many attempts. Try again in {e.RetryAfterSeconds ?? 60} seconds.",
        "update_required" => e.Message,
        "unavailable" => "AORMS isn't available right now. Try again shortly.",
        _ => e.Message,
    };
}
