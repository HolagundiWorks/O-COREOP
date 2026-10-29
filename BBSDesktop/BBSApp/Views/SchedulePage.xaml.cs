// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BBSApp.Services;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BBSApp.Views;

public sealed partial class SchedulePage : Page
{
    public enum ScheduleTab { Activities, Network, Gantt }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly ProjectSchedule _schedule = ProjectStore.Current.Schedule;
    private readonly ObservableCollection<ActivityRowVm> _rows = new();
    private readonly ScheduleTab _initialTab;
    private bool _loading;

    public SchedulePage(ScheduleTab tab = ScheduleTab.Activities)
    {
        _initialTab = tab;
        InitializeComponent();
        BBSApp.Controls.PivotTabs.HideHeaders(SchedulePivot);
        _loading = true;
        StartDatePicker.Date = new DateTimeOffset(_schedule.StartDate);
        SelectWorkWeek(_schedule.WorkingDaysPerWeek);
        ActivityGrid.ItemsSource = _rows;
        RebuildRows();

        Network.NodeMoved += OnNodeMoved;
        Network.LinkRequested += OnLinkRequested;
        Network.SelectionChanged += OnNodeSelected;

        Loaded += (_, _) =>
        {
            SchedulePivot.SelectedIndex = _initialTab switch
            {
                ScheduleTab.Network => 1,
                ScheduleTab.Gantt => 2,
                _ => 0
            };
            Recompute();
        };
        _loading = false;
    }

    private void SelectWorkWeek(int days)
    {
        foreach (var obj in WorkWeekBox.Items)
            if (obj is ComboBoxItem ci && ci.Tag is string t && t == days.ToString())
            {
                WorkWeekBox.SelectedItem = ci;
                return;
            }
        WorkWeekBox.SelectedIndex = 1; // 6-day default
    }

    private void RebuildRows()
    {
        _rows.Clear();
        foreach (var a in _schedule.Activities)
            _rows.Add(new ActivityRowVm(_schedule, a));
    }

    private void Recompute()
    {
        var result = ScheduleCalculator.Compute(_schedule);
        foreach (var vm in _rows) vm.RefreshComputed();
        string cycle = result.HasCycle ? "  ·  ⚠ circular dependency" : "";
        SummaryText.Text = _schedule.Activities.Count == 0
            ? "No activities yet — add manually or seed from the project."
            : $"{result.ActivityCount} activities · {result.ProjectDurationDays:0.#} working days · "
              + $"finish {result.FinishDate:dd MMM yyyy} · {result.CriticalCount} on critical path{cycle}";

        if (SchedulePivot.SelectedIndex == 1) Network.Render(_schedule);
        else if (SchedulePivot.SelectedIndex == 2) Gantt.Render(_schedule);
    }

    private void Pivot_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (SchedulePivot.SelectedIndex == 1) Network.Render(_schedule);
        else if (SchedulePivot.SelectedIndex == 2) Gantt.Render(_schedule);
    }

    private void StartDate_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_loading || args.NewDate is null) return;
        _schedule.StartDate = args.NewDate.Value.DateTime.Date;
        ProjectStore.Current.Notify();
        Recompute();
    }

    private void WorkWeek_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (WorkWeekBox.SelectedItem is ComboBoxItem ci && ci.Tag is string t && int.TryParse(t, out var d))
        {
            _schedule.WorkingDaysPerWeek = d;
            ProjectStore.Current.Notify();
            Recompute();
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var a = new ScheduleActivity { Name = $"Activity {_schedule.Activities.Count + 1}", DurationDays = 3 };
        _schedule.Activities.Add(a);
        _rows.Add(new ActivityRowVm(_schedule, a));
        ProjectStore.Current.Notify();
        Recompute();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        ScheduleActivity? target =
            (ActivityGrid.SelectedItem as ActivityRowVm)?.A
            ?? (Network.SelectedId is { } id ? _schedule.Find(id) : null);
        if (target is null)
        {
            AppNotify.Info("Nothing selected", "Select an activity row (or a network node) to delete.");
            return;
        }
        _schedule.Activities.Remove(target);
        foreach (var other in _schedule.Activities)
            other.Links.RemoveAll(l => l.PredecessorId == target.Id);
        RebuildRows();
        ProjectStore.Current.Notify();
        Recompute();
    }

    private async void Seed_Click(object sender, RoutedEventArgs e)
    {
        if (_schedule.Activities.Count > 0)
        {
            var dlg = new ContentDialog
            {
                Title = "Seed schedule from project",
                Content = "This replaces the current activities with a generated construction sequence "
                          + "based on your levels and BOQ elements. Continue?",
                PrimaryButtonText = "Replace",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        }
        _schedule.Activities.Clear();
        _schedule.Activities.AddRange(ScheduleSeeder.Build(ProjectStore.Current));
        RebuildRows();
        ProjectStore.Current.Notify();
        Recompute();
        AppNotify.Success("Schedule seeded", $"{_schedule.Activities.Count} activities generated. Review and adjust durations.");
    }

    private void Recalc_Click(object sender, RoutedEventArgs e) => Recompute();

    private void ActivityGrid_CellEditEnded(object sender, DataGridCellEditEndedEventArgs e)
    {
        ProjectStore.Current.Notify();
        Recompute();
    }

    // ---- network events ----
    private void OnNodeMoved(string id, double x, double y) => ProjectStore.Current.Notify();

    private void OnNodeSelected(string? id)
    {
        if (id is null) return;
        var vm = _rows.FirstOrDefault(r => r.A.Id == id);
        if (vm is not null) ActivityGrid.SelectedItem = vm;
    }

    private void OnLinkRequested(string fromId, string toId)
    {
        var succ = _schedule.Find(toId);
        var pred = _schedule.Find(fromId);
        if (succ is null || pred is null) return;
        if (succ.Links.Any(l => l.PredecessorId == fromId))
        {
            AppNotify.Info("Already linked", $"{pred.Name} → {succ.Name} exists.");
            return;
        }
        var link = new ActivityLink { PredecessorId = fromId, Type = DependencyType.FS };
        succ.Links.Add(link);
        var result = ScheduleCalculator.Compute(_schedule);
        if (result.HasCycle)
        {
            succ.Links.Remove(link); // revert
            AppNotify.Error("Circular dependency", $"Linking {pred.Name} → {succ.Name} would create a loop.");
            Recompute();
            return;
        }
        ProjectStore.Current.Notify();
        foreach (var vm in _rows) vm.RefreshComputed();
        Recompute();
    }

    private void DeleteLink_Click(object sender, RoutedEventArgs e)
    {
        var sel = Network.SelectedId is { } id ? _schedule.Find(id)
                  : (ActivityGrid.SelectedItem as ActivityRowVm)?.A;
        if (sel is null)
        {
            AppNotify.Info("Select a node", "Click a network node (or a row) first, then delete its incoming links.");
            return;
        }
        if (sel.Links.Count == 0)
        {
            AppNotify.Info("No links", $"{sel.Name} has no predecessors.");
            return;
        }
        sel.Links.Clear();
        ProjectStore.Current.Notify();
        Recompute();
    }

    // ---- gantt zoom ----
    private void GanttZoomIn_Click(object sender, RoutedEventArgs e) { Gantt.PxPerDay = Math.Min(60, Gantt.PxPerDay + 6); Gantt.Render(_schedule); }
    private void GanttZoomOut_Click(object sender, RoutedEventArgs e) { Gantt.PxPerDay = Math.Max(8, Gantt.PxPerDay - 6); Gantt.Render(_schedule); }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_schedule.Activities.Count == 0)
        {
            AppNotify.Info("Nothing to export", "Add or seed activities first.");
            return;
        }
        Recompute();
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
        picker.SuggestedFileName = (ProjectStore.Current.Name.Replace(' ', '_')) + "_schedule";
        picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        if (PdfExport.ExportSchedule(file.Path, ProjectStore.Current, out var err))
            AppNotify.Success("Schedule exported", file.Name);
        else
            AppNotify.Error("Export failed", err ?? "Could not write PDF.");
    }
}

/// <summary>Row wrapper over a <see cref="ScheduleActivity"/> for the editable grid.</summary>
public sealed class ActivityRowVm : INotifyPropertyChanged
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly ProjectSchedule _s;
    public ScheduleActivity A { get; }

    public ActivityRowVm(ProjectSchedule s, ScheduleActivity a) { _s = s; A = a; }

    public string IndexText => _s.IndexOf(A).ToString(Inv);

    public string Name
    {
        get => A.Name;
        set { A.Name = value ?? ""; OnP(); }
    }

    public string Wbs
    {
        get => A.Wbs;
        set { A.Wbs = value ?? ""; OnP(); }
    }

    public string DurationText
    {
        get => A.DurationDays.ToString("0.#", Inv);
        set
        {
            if (double.TryParse(value, NumberStyles.Float, Inv, out var d)) A.DurationDays = Math.Max(0, d);
            OnP();
        }
    }

    public string PercentText
    {
        get => A.PercentComplete.ToString("0.#", Inv);
        set
        {
            if (double.TryParse(value, NumberStyles.Float, Inv, out var d)) A.PercentComplete = Math.Clamp(d, 0, 100);
            OnP();
        }
    }

    private static readonly Regex PredRe =
        new(@"^\s*(\d+)\s*([A-Za-z]{2})?\s*([+-]\d+(?:\.\d+)?)?\s*$", RegexOptions.Compiled);

    public string PredecessorsText
    {
        get
        {
            var parts = new List<string>();
            foreach (var l in A.Links)
            {
                var p = _s.Find(l.PredecessorId);
                if (p is null) continue;
                string tok = _s.IndexOf(p).ToString(Inv);
                if (l.Type != DependencyType.FS) tok += l.Type.ToString();
                if (Math.Abs(l.LagDays) > 1e-6) tok += (l.LagDays >= 0 ? "+" : "") + l.LagDays.ToString("0.#", Inv);
                parts.Add(tok);
            }
            return string.Join(", ", parts);
        }
        set
        {
            A.Links.Clear();
            foreach (var raw in (value ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = PredRe.Match(raw);
                if (!m.Success) continue;
                int idx = int.Parse(m.Groups[1].Value, Inv);
                if (idx < 1 || idx > _s.Activities.Count) continue;
                var pred = _s.Activities[idx - 1];
                if (pred == A) continue;
                var link = new ActivityLink { PredecessorId = pred.Id };
                if (m.Groups[2].Success && Enum.TryParse<DependencyType>(m.Groups[2].Value.ToUpperInvariant(), out var t))
                    link.Type = t;
                if (m.Groups[3].Success && double.TryParse(m.Groups[3].Value, NumberStyles.Float, Inv, out var lag))
                    link.LagDays = lag;
                A.Links.Add(link);
            }
            OnP();
        }
    }

    public string StartText => A.IsMilestone
        ? _s.DateForOffset(A.EarlyStart).ToString("dd MMM")
        : _s.DateForOffset(A.EarlyStart).ToString("dd MMM");
    public string FinishText => _s.DateForOffset(A.EarlyFinish).ToString("dd MMM");
    public string FloatText => A.InCycle ? "—" : A.TotalFloat.ToString("0.#", Inv);
    public string CriticalText => A.InCycle ? "cycle" : A.IsCritical ? "● yes" : "";

    public void RefreshComputed()
    {
        OnP(nameof(IndexText));
        OnP(nameof(PredecessorsText));
        OnP(nameof(StartText));
        OnP(nameof(FinishText));
        OnP(nameof(FloatText));
        OnP(nameof(CriticalText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
