// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;

namespace BBSApp.Services;

/// <summary>
/// Hands the item derivation graph (work items and their link rules) to O-Dynamo to edit visually, and takes the
/// rules back when the user saves there. O-Dynamo is a separate program, as it is for O-Draft; the two share one
/// JSON file (<c>datamodel.json</c>, version 1) and nothing else.
/// </summary>
public sealed class DataModelBridge : IDisposable
{
    public const string HostExe = "ODynamo.Host.exe";

    private readonly string _dir = Path.Combine(Branding.AppDataDirectory, "datamodel");
    private FileSystemWatcher? _watcher;
    private DateTime _lastApplied = DateTime.MinValue;
    private System.Diagnostics.Process? _process;

    /// <summary>The one bridge, so a save in O-Dynamo still arrives after the user has left the Data model page.</summary>
    public static DataModelBridge Shared { get; } = new();

    /// <summary>Raised on the UI thread after the user saved links in O-Dynamo; the argument is how many rules there now are.</summary>
    public event Action<int>? RulesApplied;

    public string FilePath => Path.Combine(_dir, "datamodel.json");
    private string HostPathFile => Path.Combine(_dir, "host.txt");

    /// <summary>Where O-Dynamo is: the path the user chose, <c>ODYNAMO_HOST</c>, beside this app, or its usual install folder.</summary>
    public string? FindHost()
    {
        var candidates = new List<string?>
        {
            File.Exists(HostPathFile) ? File.ReadAllText(HostPathFile).Trim() : null,
            Environment.GetEnvironmentVariable("ODYNAMO_HOST"),
            Path.Combine(AppContext.BaseDirectory, "ODynamo", HostExe),
            Path.Combine(AppContext.BaseDirectory, HostExe),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "O-Dynamo", HostExe),
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
    }

    public void RememberHost(string path)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(HostPathFile, path);
    }

    /// <summary>The graph as the file O-Dynamo reads: every trade, with its unit and rate, and every rule.</summary>
    public JsonObject Build()
    {
        RateBookStore.Current.EnsureLoaded();
        var version = RateBookStore.Current.ActiveOrFirst();
        var rates = version is null
            ? new Dictionary<string, RateItem>(StringComparer.OrdinalIgnoreCase)
            : RateBookStore.Current.IndexByCode(version);

        var trades = new JsonArray();
        foreach (var t in LinkTradeRegistry.All)
        {
            var code = EstimateCalculator.CodeForElement(t.Key, t.Unit);
            var rate = rates.TryGetValue(code, out var ri) && ri.Rate > 0
                ? "₹" + ri.Rate.ToString("0.##", CultureInfo.InvariantCulture)
                : "";
            trades.Add(new JsonObject { ["key"] = t.Key, ["display"] = t.Display, ["unit"] = t.Unit, ["rate"] = rate });
        }
        return new JsonObject
        {
            ["v"] = 1,
            ["kind"] = "datamodel",
            ["saved"] = false,
            ["trades"] = trades,
            ["rules"] = ProjectStore.Current.LinkRules.ToJson(),
        };
    }

    /// <summary>Writes the graph, starts watching the file for a save, and starts O-Dynamo on it.</summary>
    public void Open(string hostPath)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, Build().ToJsonString());
        _lastApplied = File.GetLastWriteTimeUtc(FilePath);
        Watch();
        _process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = hostPath,
            Arguments = $"--datamodel \"{FilePath}\"",
            UseShellExecute = false,
        });
    }

    private void Watch()
    {
        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(_dir, "datamodel.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, EnableRaisingEvents = true };
        _watcher.Changed += (_, _) => TryApply();
        _watcher.Created += (_, _) => TryApply();
        _watcher.Renamed += (_, _) => TryApply();
    }

    /// <summary>Takes the rules from the file when O-Dynamo marked it saved. Returns false if there was nothing new or it could not be read.</summary>
    public bool TryApply()
    {
        try
        {
            Thread.Sleep(150); // the editor replaces the file; let it finish
            var stamp = File.GetLastWriteTimeUtc(FilePath);
            if (stamp <= _lastApplied) return false;
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject root
                || root["saved"]?.GetValue<bool>() != true
                || root["rules"] is not JsonArray rules)
                return false;
            _lastApplied = stamp;

            var incoming = rules.OfType<JsonObject>().Select(LinkRule.FromJson)
                .Where(r => LinkTradeRegistry.ByKey(r.SourceTrade) is not null && LinkTradeRegistry.ByKey(r.TargetTrade) is not null)
                .ToList();
            var apply = () =>
            {
                var book = ProjectStore.Current.LinkRules.Rules;
                book.Clear();
                foreach (var r in incoming) book.Add(r);
                ProjectStore.Current.Notify();
                RulesApplied?.Invoke(incoming.Count);
            };
            var queue = App.MainWindow?.DispatcherQueue;
            if (queue is null) apply(); else queue.TryEnqueue(() => apply());
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
