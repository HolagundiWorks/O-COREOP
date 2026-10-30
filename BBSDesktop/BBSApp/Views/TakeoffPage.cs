// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Aqc.CadImport;
using BBSApp.Services;
using BBSApp.Services.Shilpi;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BBSApp.Views;

/// <summary>
/// Take-off. Drawings and PDFs are measured in O-Draft, which records the take-off in the project file (<c>.osp</c>) that O-CoreOp has open too.
/// This page lists the drawings recorded there, reads one drawing's take-off into the same import preview as
/// File > Import from CAD (nothing is written before the user accepts), and fetches the annotated drawing PDF O-Draft generated.
/// </summary>
public sealed class TakeoffPage : Page
{
    readonly TextBlock _file = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    readonly Button _refresh = new() { Content = "Refresh" };
    readonly ListView _drawings = new() { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 260, MinHeight = 60 };
    readonly Button _read = new() { Content = "Read take-off…" };
    readonly Button _annotated = new() { Content = "Fetch annotated drawing" };
    readonly StackPanel _stored = new() { Spacing = 6 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(5) };
    List<ShilpiDrawing> _list = new();
    readonly HashSet<uint> _complete = new();
    bool _seenList;
    bool _busy;

    public TakeoffPage()
    {
        var title = new TextBlock { Text = "Take-off", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        AutomationProperties.SetName(_drawings, "Drawings with a recorded take-off");

        Content = new ScrollViewer
        {
            Padding = new Thickness(24),
            Content = new StackPanel
            {
                Spacing = 14, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left,
                Children =
                {
                    title,
                    BuildToolBar(),
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = "Drawings and PDFs are measured in O-Draft with its take-off toolbars, which record the take-off in the project file. "
                             + "That file is this project's file, so a take-off appears here as soon as O-Draft has recorded it. Pick a drawing, read it, review what would change, "
                             + "then import; rows can be edited here by hand afterwards. "
                             + "Importing again updates the same rows and never duplicates them. Plaster, painting, skirting and shuttering are derived here, not imported.",
                    },
                    _file,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Drawings", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } } },
                    _drawings,
                    new TextBlock { Text = "Annotated drawings stored with this project", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) },
                    _stored,
                    _status,
                },
            },
        };

        _refresh.Click += async (_, _) => await RefreshAsync();
        _read.Click += async (_, _) => await ReadAsync();
        _annotated.Click += async (_, _) => await FetchAnnotatedAsync();
        _drawings.SelectionChanged += (_, _) => UpdateButtons();
        _poll.Tick += async (_, _) =>
        {
            // the file is read again only when it has changed (O-Draft recorded or updated a take-off)
            var p = ProjectFile;
            DateTime now = p is not null && File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue;
            if (_busy || now == _stamp) return;
            _stamp = now;
            UpdateMode();
            await RefreshAsync(quiet: true);
        };
        Loaded += async (_, _) =>
        {
            UpdateMode(); ShowStored(); _poll.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => { _poll.Stop(); ServedProject.Changed -= OnServedChanged; };
        Loaded += (_, _) => ServedProject.Changed += OnServedChanged;
    }

    /// <summary>
    /// The take-off bar: flat text buttons in groups with dividers, drawn the way O-Draft's take-off bar is (text only, no
    /// icons). Source (where the take-off is read from), Read (list and import it), Annotated (the stored drawing).
    /// </summary>
    StackPanel BuildToolBar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(bar, "Take-off tools");
        var groups = new[] { new[] { _refresh, _read }, new[] { _annotated } };
        for (int g = 0; g < groups.Length; g++)
        {
            if (g > 0) bar.Children.Add(BarDivider());
            foreach (var b in groups[g])
            {
                StyleAsTextTool(b);
                bar.Children.Add(b);
            }
        }
        return bar;
    }

    static void StyleAsTextTool(Button b)
    {
        var label = b.Content as string ?? "";
        b.Content = new TextBlock { Text = label, FontSize = Hcw.Ui.SuiteScale.FontBody, TextTrimming = TextTrimming.CharacterEllipsis };
        b.MinHeight = Hcw.Ui.SuiteScale.ControlMinHeight;
        b.Padding = new Thickness(12, 0, 12, 0);
        b.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        b.BorderThickness = new Thickness(0);
        b.CornerRadius = Hcw.Ui.SuiteScale.CornerControl;
        b.VerticalAlignment = VerticalAlignment.Center;
        if (Application.Current.Resources.TryGetValue("HcwGhostButton", out var style) && style is Style s) b.Style = s;
        AutomationProperties.SetName(b, label);
    }

    static Border BarDivider() => new()
    {
        Width = 1,
        Height = 16,
        Margin = new Thickness(6, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Background = Application.Current.Resources.TryGetValue("HcwEdgeBrush", out var edge) && edge is Brush br ? br : new SolidColorBrush(Microsoft.UI.Colors.Gray),
    };

    /// <summary>The take-off is read from the open project file, the one O-Draft records into.</summary>
    static string? ProjectFile => ProjectFiles.IsOdp(ProjectStore.Current.FilePath) ? ProjectStore.Current.FilePath : null;

    TakeoffSource? Source() =>
        ProjectFile is { } path && File.Exists(path) ? new TakeoffSource.Project(path) : null;

    DateTime _stamp = DateTime.MinValue;

    // O-Draft recorded something in the served file: read the list again now, not at the next poll.
    void OnServedChanged() => DispatcherQueue.TryEnqueue(async () => { if (!_busy) { UpdateMode(); await RefreshAsync(quiet: true); } });

    ShilpiDrawing? Selected => (_drawings.SelectedItem as ListViewItem)?.Tag as ShilpiDrawing;

    void UpdateMode()
    {
        var p = ProjectFile;
        _file.Text = p is null
            ? "No project is open. Use File > Open to open a project file (.osp), or open O-Draft's project, to read a take-off."
            : File.Exists(p) ? "Reading the take-off from " + p + ". A take-off O-Draft records appears here at once." : p + " does not exist yet: record the take-off in O-Draft, then Refresh.";
        UpdateButtons();
    }

    void UpdateButtons()
    {
        var d = Selected;
        _refresh.IsEnabled = !_busy && Source() is not null;
        _read.IsEnabled = !_busy && d is { IsComplete: true };
        _annotated.IsEnabled = !_busy && d?.Annotated is not null;
    }

    void FillList()
    {
        var selected = Selected?.Slot;
        var shared = ProjectStore.Current.Shared;
        var companyUuid = BBSApp.Services.Company.CompanyService.Current?.Uuid;
        // A drawing belongs to the open project when it names the project in the company file.
        bool Mine(ShilpiDrawing d) =>
            shared is not null && d.ProjectRefId == shared.ProjectId && !string.IsNullOrEmpty(companyUuid) && d.ProjectRefCompany == companyUuid;
        bool Other(ShilpiDrawing d) =>
            !Mine(d) && shared is not null && d.ProjectRefId is not null;
        _drawings.Items.Clear();
        // this project's drawings first, then the others
        foreach (var d in _list.OrderByDescending(Mine).ThenBy(d => d.Drawing, StringComparer.OrdinalIgnoreCase))
        {
            var bits = new List<string> { d.DisplayName };
            if (d.Problem is not null) bits.Add("unreadable: " + d.Problem);
            else
            {
                bits.Add(d.IsComplete ? "complete" : $"being recorded ({d.State})");
                bits.Add($"{d.Rows} rows");
                if (d.Annotated is not null) bits.Add("annotated drawing");
                if (Mine(d)) bits.Add("this project");
                else if (Other(d)) bits.Add("another project");
            }
            var item = new ListViewItem { Tag = d, Content = string.Join("  ·  ", bits) };
            _drawings.Items.Add(item);
            if (d.Slot == selected) _drawings.SelectedItem = item;
        }
        if (_drawings.SelectedItem is null && _drawings.Items.Count > 0) _drawings.SelectedIndex = 0;
        // a drawing recorded from a Studio project names it: take that project's details from the company file
        if (_list.FirstOrDefault(d => d.ProjectRefId is not null) is { } named)
            _ = App.MainWindow!.LinkOpenProjectToStudioAsync(named.ProjectRefCompany, named.ProjectRefId);
        UpdateButtons();
    }

    void ShowStored()
    {
        _stored.Children.Clear();
        var list = ProjectStore.Current.Info.AnnotatedDrawings;
        if (list.Count == 0) { _stored.Children.Add(new TextBlock { Text = "None yet.", Opacity = 0.7 }); return; }
        foreach (var a in list)
        {
            var open = new Button { Content = "Open", IsEnabled = a.Exists };
            AutomationProperties.SetName(open, $"Open the annotated drawing for {a.Drawing}");
            open.Click += async (_, _) =>
            {
                try { await Windows.System.Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(a.File)); }
                catch (Exception ex) { AppNotify.Error("Annotated drawing", ex.Message); }
            };
            _stored.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children = { open, new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Text = $"{a.Drawing}  ·  fetched {a.Fetched}{(a.Exists ? "" : "  ·  file missing")}" } },
            });
        }
    }

    async Task RefreshAsync(bool quiet = false)
    {
        var src = Source();
        if (src is null) { _list = new(); FillList(); if (!quiet) _status.Text = "No take-off yet: the project file does not exist or no project is open."; return; }
        if (_busy) return;
        _busy = true; UpdateButtons();
        try
        {
            _list = await ShilpiTakeoffService.ListDrawingsAsync(src);
            var newly = _seenList
                ? _list.Where(d => d.IsComplete && !_complete.Contains(d.Slot)).Select(d => d.DisplayName).ToList()
                : new List<string>();
            _complete.Clear();
            foreach (var d in _list.Where(d => d.IsComplete)) _complete.Add(d.Slot);
            _seenList = true;
            FillList();
            if (newly.Count > 0)
                _status.Text = newly.Count == 1
                    ? $"{newly[0]} take-off is complete and can be read."
                    : $"{newly.Count} take-offs are complete and can be read.";
            else if (!quiet)
            {
                var text = _list.Count == 0
                    ? "No take-off has been recorded yet. Record one in O-Draft."
                    : $"{_list.Count} drawing(s) with a recorded take-off.";
                if (_list.Count > 0 && ProjectStore.Current.Shared is null)
                    text += "";
                _status.Text = text;
            }
        }
        catch (Exception ex)
        {
            _list = new(); FillList();
            if (!quiet) _status.Text = ex.Message;
            else _status.Text = ex.Message;
        }
        finally { _busy = false; UpdateButtons(); }
    }

    async Task ReadAsync()
    {
        var src = Source(); var d = Selected;
        if (src is null || d is null || _busy) return;
        _busy = true; UpdateButtons();
        try
        {
            var result = await ShilpiTakeoffService.ReadDrawingAsync(src, d.Slot, new Progress<string>(s => _status.Text = s));
            var prep = CadImportService.Prepare(result.File);
            _status.Text = $"{result.Records} take-off records read from {d.DisplayName}"
                           + (result.Skipped.Count > 0 ? $" ({result.Skipped.Count} could not be used: {string.Join("; ", result.Skipped.Take(3))})" : "")
                           + ". " + prep.Plan.Summary + "."
                           + (result.Notes is { Count: > 0 } notes ? $" {notes.Count} value(s) were worked out from the drawing's positions or outlines because the take-off did not state them; the preview marks the ones to check." : "");
            _busy = false; UpdateButtons();
            await App.MainWindow!.ShowCadImportPreviewAsync(prep, $"Import take-off: {d.DisplayName}");
        }
        catch (Exception ex) { _status.Text = ex.Message; AppNotify.Error("Take-off", ex.Message); }
        finally { _busy = false; UpdateButtons(); }
    }

    async Task FetchAnnotatedAsync()
    {
        var src = Source(); var d = Selected;
        if (src is null || d?.Annotated is null || _busy) return;
        _busy = true; UpdateButtons();
        try
        {
            var pdf = await ShilpiTakeoffService.FetchAnnotatedPdfAsync(src, d.Slot, new Progress<string>(s => _status.Text = s));
            var entry = AnnotatedStore.Save(ProjectStore.Current, d.DrawingId, d.DisplayName, pdf, d.Annotated.Generated, d.RevisionSha256);
            _status.Text = $"Annotated drawing for {d.DisplayName} stored: {entry.File}"
                           + (string.IsNullOrWhiteSpace(ProjectStore.Current.FilePath) ? " (save the project to keep it beside the project file)." : ".");
            ShowStored();
        }
        catch (Exception ex) { _status.Text = ex.Message; AppNotify.Error("Annotated drawing", ex.Message); }
        finally { _busy = false; UpdateButtons(); }
    }
}
