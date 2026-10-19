// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text.Json.Nodes;
using Aorms.Connect;

namespace BBSApp.Services.Connect;

/// <summary>What a pull-latest found.</summary>
public sealed record PullPreview(int Incoming, int UnsentLocal, bool NeedsReview, long HeadSeq, List<SyncRow> Rows);

/// <summary>
/// Keeps the open project in step with its online project at AORMS (handoff items 4.1 and 4.3). The open project is the source of truth for
/// what the user typed: about once a second (the window's timer calls <see cref="TickAsync"/>) it works out which rows changed since the
/// server was last brought in line (<see cref="RowBaseline"/>), puts them in a file-backed <see cref="Outbox"/>, and lets
/// <see cref="ProjectSync"/> send them under the edit lease. Unsent edits survive a restart. Rows changed elsewhere come in on
/// <see cref="PullLatestAsync"/> / <see cref="ApplyPull"/>, which asks first when there are unsent local changes.
/// </summary>
public static class ProjectSyncHost
{
    static ProjectSync? _sync;
    static RowBaseline? _baseline;
    static FileOutboxStore? _outboxStore;
    static string _aqcId = "";
    static string _settingsHash = "";
    static bool _dirty, _busy;
    static string? _notice;

    public static bool Active => _sync is not null;
    public static string? Notice => _notice;
    public static event Action? Changed;

    /// <summary>Marks the project as edited; the next tick works out what changed.</summary>
    public static void MarkDirty() => _dirty = true;

    public static string OutboxFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AQCCore", "outbox");

    public static string StateText => _sync is null ? "" : _sync.State switch
    {
        SyncState.Synced => "synced",
        SyncState.Pending => $"{_sync.Outbox.Count} unsent",
        SyncState.Offline => $"offline, {_sync.Outbox.Count} unsent",
        SyncState.ReadOnlyRole => "read-only (your role cannot edit)",
        SyncState.ReadOnlyLeaseHeld => "read-only (someone else is editing)",
        SyncState.SessionReplaced => "taken over on another computer",
        SyncState.SignedOut => "sign in again",
        _ => _sync.State.ToString(),
    };

    // ---- starting and stopping ----

    /// <summary>
    /// Starts syncing the open project if it is bound to an online project and the user is signed in. Safe to call every tick: it does nothing
    /// when already running for this project, and restarts when the open project changed.
    /// </summary>
    public static async Task EnsureStartedAsync()
    {
        var info = ProjectStore.Current.Info;
        var wanted = info.AormsAqcId?.Trim() ?? "";
        if (ConnectService.State != ConnectState.SignedIn || wanted.Length == 0) { if (Active) Stop(); return; }
        if (Active && _aqcId == wanted) return;
        if (Active) Stop();
        Start(wanted, info.AormsHeadSeq);
        // a project file opened on a machine with no baseline: take the local file as the baseline and let the user pull the server's changes
        _notice = _baseline!.Loaded ? null : "This project has not synced on this computer before. Use File > Pull latest from AORMS to bring in the online version.";
        Changed?.Invoke();
        await Task.CompletedTask;
    }

    static void Start(string aqcId, long headSeq)
    {
        Directory.CreateDirectory(OutboxFolder);
        _aqcId = aqcId;
        _outboxStore = new FileOutboxStore(Path.Combine(OutboxFolder, aqcId + ".json"));
        var outbox = new Outbox(_outboxStore);
        _baseline = new RowBaseline(Path.Combine(OutboxFolder, aqcId + ".base.json"));
        if (!_baseline.Loaded) { ProjectStore.Current.MintRowIds(); _baseline.Reset(ProjectMapper.ToSync(ProjectStore.Current.ToJson()).Rows); }
        _sync = new ProjectSync(ConnectService.Client(), aqcId, headSeq, outbox);
        _settingsHash = HashSettings();
        _sync.StateChanged += _ => Changed?.Invoke();
        _sync.RowRejected += (row, why) => { _notice = $"AORMS refused a {row.Section} row: {why}"; Changed?.Invoke(); };
        if (_outboxStore.QuarantinedPath is { } bad) _notice = $"A damaged unsent-changes file was set aside: {bad}";
    }

    public static void Stop()
    {
        _sync = null; _baseline = null; _outboxStore = null; _aqcId = ""; _dirty = false;
        Changed?.Invoke();
    }

    // ---- the tick ----

    /// <summary>Called about once a second from the UI thread.</summary>
    public static async Task TickAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await EnsureStartedAsync().ConfigureAwait(true);
            if (_sync is null || _baseline is null) return;
            if (_dirty) { _dirty = false; Enqueue(); }
            await _sync.TickAsync().ConfigureAwait(true);
            if (_sync.State is SyncState.Synced && SettingsChanged()) await PushSettingsAsync().ConfigureAwait(true);
            ProjectStore.Current.Info.AormsHeadSeq = _sync.HeadSeq;
        }
        catch (ConnectException e) { _notice = ConnectService.Describe(e); }
        catch (Exception e) { _notice = "Sync: " + e.Message; }
        finally { _busy = false; }
    }

    /// <summary>Works out what changed since the last sync and puts it in the outbox.</summary>
    static void Enqueue()
    {
        var store = ProjectStore.Current;
        store.MintRowIds();
        var rows = ProjectMapper.ToSync(store.ToJson()).Rows;
        var (up, del) = _baseline!.Diff(rows);
        if (up.Count == 0 && del.Count == 0) return;
        _sync!.Outbox.UpsertMany(up.Concat(del));
        _sync.NoteChange();
        Changed?.Invoke();
    }

    // ---- settings (levels, markups, covers, personas …) are one block, sent when it changes and the rows are flushed ----

    static string HashSettings() => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(ProjectMapper.ToSync(ProjectStore.Current.ToJson()).Settings.ToJsonString())));

    static bool SettingsChanged() => HashSettings() != _settingsHash;

    static async Task PushSettingsAsync()
    {
        var settings = ProjectMapper.ToSync(ProjectStore.Current.ToJson()).Settings;
        var h = HashSettings();
        await _sync!.PushSettingsAsync(settings).ConfigureAwait(true);
        _settingsHash = h;
    }

    // ---- push online ----

    public sealed record PushOutcome(string AqcId, int Rows, int Pending);

    /// <summary>
    /// Binds the open project to an AORMS project (<paramref name="projectOfficeId"/>) and sends it: the first rows with the binding, the rest in
    /// batches under the edit lease. If a later batch fails the project is bound with the rows so far and the remainder waits in the outbox.
    /// </summary>
    public static async Task<PushOutcome> PushOnlineAsync(string projectOfficeId)
    {
        var store = ProjectStore.Current;
        if (ConnectService.Session is not { CanWrite: true })
            throw new InvalidOperationException("Your AORMS role cannot send a project online (it needs permission to write).");
        store.MintRowIds();
        var doc = store.ToJson();
        var result = await new ProjectsApi(ConnectService.Client()).PushOnlineAsync(projectOfficeId, doc).ConfigureAwait(true);
        store.Info.AormsProjectOfficeId = projectOfficeId;
        store.Info.AormsAqcId = result.AqcProjectId;
        store.Info.AormsHeadSeq = result.HeadSeq;
        store.Notify();

        Directory.CreateDirectory(OutboxFolder);
        var baseline = new RowBaseline(Path.Combine(OutboxFolder, result.AqcProjectId + ".base.json"));
        var all = ProjectMapper.ToSync(store.ToJson()).Rows;
        var pendingKeys = result.Pending.Select(p => (p.Section, p.RowId)).ToHashSet();
        baseline.Reset(all.Where(r => !pendingKeys.Contains((r.Section, r.RowId))));   // what the server holds now
        if (result.Pending.Count > 0)
        {
            var box = new Outbox(new FileOutboxStore(Path.Combine(OutboxFolder, result.AqcProjectId + ".json")));
            box.UpsertMany(result.Pending);
        }
        Stop();   // restart cleanly on the next tick, from the files just written
        return new PushOutcome(result.AqcProjectId, result.Rows, result.Pending.Count);
    }

    // ---- pull latest ----

    /// <summary>Asks the server for changes made elsewhere. Nothing is applied yet.</summary>
    public static async Task<PullPreview> PullLatestAsync()
    {
        if (_sync is null) throw new InvalidOperationException("This project is not synced with AORMS. Push it online or link it to an online project first.");
        if (_dirty) { _dirty = false; Enqueue(); }
        var catchUp = await _sync.CatchUpAsync().ConfigureAwait(true);
        return new PullPreview(catchUp.Rows.Count, _sync.Outbox.Count, catchUp.NeedsReview, catchUp.HeadSeq, catchUp.Rows);
    }

    /// <summary>
    /// Brings the changes in. Where the user also has unsent edits to the same rows, the incoming version wins and the unsent one is dropped
    /// (the preview told them). The project is rebuilt from its own rows plus the incoming ones; settings, the take-off state and the last
    /// estimate are kept.
    /// </summary>
    public static void ApplyPull(PullPreview pull)
    {
        if (_sync is null || _baseline is null) return;
        var store = ProjectStore.Current;
        store.MintRowIds();
        var newDoc = ProjectMapper.ApplyDelta(store.ToJson(), pull.Rows);
        foreach (var r in pull.Rows) _sync.Outbox.Drop(r.Section, r.RowId);
        _baseline.Adopt(pull.Rows);
        var keepPath = store.FilePath;
        store.LoadFrom(newDoc);
        store.FilePath = keepPath;
        _sync.AdoptHead(pull.HeadSeq);
        store.Info.AormsHeadSeq = _sync.HeadSeq;
        _settingsHash = HashSettings();
        store.Notify();
        Changed?.Invoke();
    }
}
