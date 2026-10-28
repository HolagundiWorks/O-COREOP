// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Aqc.CadImport;
using BBSApp.Services;
using BBSApp.Services.Shilpi;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BBSApp.Views;

/// <summary>
/// Take-off. Drawings and PDFs are measured in O-Draft, which records the take-off in ShilpiDB; O-Draft and O-CoreOp run side by side on
/// one shared server. This page lists the drawings recorded there, reads one drawing's take-off into the same import preview as
/// File > Import from CAD (nothing is written before the user accepts), and fetches the annotated drawing PDF O-Draft generated.
/// </summary>
public sealed class TakeoffPage : Page
{
    readonly RadioButtons _mode = new() { Header = "Read the take-off from", Items = { "The shared take-off server (O-Draft running alongside)", "A take-off file (.vdb)" }, SelectedIndex = 0 };
    readonly TextBox _gateway = new() { Header = "Server address", Text = ShilpiTakeoffService.DefaultGateway, MinWidth = 300 };
    readonly TextBlock _file = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    readonly Button _choose = new() { Content = "Choose take-off file…" };
    readonly Button _start = new() { Content = "Start the shared server" };
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
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = "Drawings and PDFs are measured in O-Draft, which records the take-off in ShilpiDB. Both apps use one shared take-off server, so a "
                             + "take-off appears here as soon as O-Draft has recorded it. Pick a drawing, read it, review what would change, then import. "
                             + "Importing again updates the same rows and never duplicates them. Plaster, painting, skirting and shuttering are derived here, not imported.",
                    },
                    _mode,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom, Children = { _gateway, _start } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _choose, _file } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Drawings", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }, _refresh } },
                    _drawings,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _read, _annotated } },
                    new TextBlock { Text = "Annotated drawings stored with this project", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) },
                    _stored,
                    _status,
                },
            },
        };

        _mode.SelectionChanged += (_, _) => { UpdateMode(); _ = RefreshAsync(); };
        _start.Click += async (_, _) => await StartAsync();
        _choose.Click += async (_, _) => await ChooseAsync();
        _refresh.Click += async (_, _) => await RefreshAsync();
        _read.Click += async (_, _) => await ReadAsync();
        _annotated.Click += async (_, _) => await FetchAnnotatedAsync();
        _drawings.SelectionChanged += (_, _) => UpdateButtons();
        _poll.Tick += async (_, _) => { if (!_busy && _mode.SelectedIndex == 0) await RefreshAsync(quiet: true); };
        Loaded += async (_, _) =>
        {
            UpdateMode(); ShowStored(); _poll.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _poll.Stop();
    }

    bool UseFile => _mode.SelectedIndex == 1;

    TakeoffSource? Source()
    {
        if (!UseFile) return new TakeoffSource.Gateway(_gateway.Text.Trim());
        var path = ProjectStore.Current.Info.TakeoffVdbPath;
        return string.IsNullOrWhiteSpace(path) ? null : new TakeoffSource.VdbFile(path);
    }

    ShilpiDrawing? Selected => (_drawings.SelectedItem as ListViewItem)?.Tag as ShilpiDrawing;

    void UpdateMode()
    {
        _gateway.Visibility = _start.Visibility = UseFile ? Visibility.Collapsed : Visibility.Visible;
        _choose.Visibility = _file.Visibility = UseFile ? Visibility.Visible : Visibility.Collapsed;
        var p = ProjectStore.Current.Info.TakeoffVdbPath;
        _file.Text = string.IsNullOrWhiteSpace(p) ? "None chosen yet." : p;
        UpdateButtons();
    }

    void UpdateButtons()
    {
        var d = Selected;
        _refresh.IsEnabled = !_busy && Source() is not null;
        _read.IsEnabled = !_busy && d is { IsComplete: true };
        _annotated.IsEnabled = !_busy && d?.Annotated is not null;
        _start.IsEnabled = !_busy;
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

    async Task StartAsync()
    {
        if (_busy) return;
        _busy = true; UpdateButtons();
        try
        {
            var started = await ShilpiTakeoffService.EnsureSharedServerAsync(new Progress<string>(s => _status.Text = s));
            _status.Text = started ? "The shared take-off server is running. O-Draft can record to it now." : "The shared take-off server was already running.";
        }
        catch (Exception ex) { _status.Text = ex.Message; AppNotify.Error("Take-off", ex.Message); }
        finally { _busy = false; UpdateButtons(); }
        await RefreshAsync();
    }

    async Task ChooseAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
        picker.FileTypeFilter.Add(".vdb");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        ProjectStore.Current.Info.TakeoffVdbPath = file.Path;
        ProjectStore.Current.Notify();
        UpdateMode();
        await RefreshAsync();
    }

    async Task RefreshAsync(bool quiet = false)
    {
        var src = Source();
        if (src is null) { _list = new(); FillList(); if (!quiet) _status.Text = "Choose a take-off file."; return; }
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
                    text += " Open or link a company-file project so drawings that carry a project_ref can be marked as this project.";
                _status.Text = text;
            }
        }
        catch (Exception ex)
        {
            _list = new(); FillList();
            if (!quiet) _status.Text = ex.Message;
            else _status.Text = "The shared take-off server is not reachable. " + (ShilpiTakeoffService.FindBinDirectory() is null ? "ShilpiDB is not installed." : "Press Start the shared server.");
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
