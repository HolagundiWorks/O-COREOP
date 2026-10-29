// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Text;
using BBSApp.Controls;
using BBSApp.Services;
using BBSApp.Services.Company;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Office.Shared;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BBSApp.Views;

public sealed partial class EstimatePage : Page
{
    private EstimateResult? _result;
    private readonly ResultTable _civilTable = new();
    private readonly ResultTable _matTable = new();
    private readonly ResultTable _steelTable = new();
    private readonly Dictionary<string, CheckBox> _levelBoxes = new();
    private bool _buildingLevels;

    public EstimatePage()
    {
        InitializeComponent();
        _civilTable.SetAutomationName("Civil estimate");
        _matTable.SetAutomationName("Materials estimate");
        _steelTable.SetAutomationName("Steel estimate");
        CivilHost.Child = _civilTable;
        MatHost.Child = _matTable;
        SteelHost.Child = _steelTable;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RateBookStore.Current.EnsureLoaded();
        BuildLevelChecks(selectAll: true);
        RefreshVersionCombo();
        RefreshPhaseCombo();
        if (ProjectStore.Current.LastEstimate is { } last)
            ShowResult(last);
        else
            ProjectRefText.Text = ProjectStore.Current.Shared is null
                ? "Company project: not linked."
                : $"Company project linked (id {ProjectStore.Current.Shared.ProjectId}). Calculate to attach a ProjectRef / ArtifactRef.";
        ProjectStore.Current.Changed += OnStoreChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ProjectStore.Current.Changed -= OnStoreChanged;
    }

    private void OnStoreChanged()
    {
        var selected = SelectedLevels();
        BuildLevelChecks(selectAll: false);
        foreach (var kv in _levelBoxes)
            kv.Value.IsChecked = selected.Contains(kv.Key) || selected.Count == 0;
        if (_levelBoxes.Count > 0 && !_levelBoxes.Values.Any(c => c.IsChecked == true))
            foreach (var cb in _levelBoxes.Values) cb.IsChecked = true;
    }

    private void RefreshPhaseCombo()
    {
        var keep = (PhaseCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        PhaseCombo.Items.Clear();
        PhaseCombo.Items.Add(new ComboBoxItem { Content = "All phases", Tag = "" });
        foreach (var name in ProjectStore.Current.PhaseNames(billing: false))
            PhaseCombo.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        PhaseCombo.SelectedItem = PhaseCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, keep, StringComparison.OrdinalIgnoreCase))
            ?? PhaseCombo.Items.FirstOrDefault();
    }

    private void RefreshVersionCombo()
    {
        var store = RateBookStore.Current;
        VersionCombo.Items.Clear();
        foreach (var v in store.Versions.OrderBy(x => x.Name))
            VersionCombo.Items.Add(new ComboBoxItem { Content = v.Name, Tag = v.Id });

        string? prefer = ProjectStore.Current.LastEstimateRateBookVersionId ?? store.ActiveVersionId;
        ComboBoxItem? pick = null;
        foreach (ComboBoxItem item in VersionCombo.Items)
        {
            if (string.Equals(item.Tag as string, prefer, StringComparison.OrdinalIgnoreCase))
            {
                pick = item;
                break;
            }
        }
        VersionCombo.SelectedItem = pick ?? VersionCombo.Items.FirstOrDefault();
    }

    private void BuildLevelChecks(bool selectAll)
    {
        _buildingLevels = true;
        LevelChecks.Children.Clear();
        _levelBoxes.Clear();
        foreach (var lv in ProjectStore.Current.Levels)
        {
            var cb = new CheckBox
            {
                Content = string.IsNullOrWhiteSpace(lv.Name) ? lv.Id : $"{lv.Id} · {lv.Name}",
                Tag = lv.Id,
                IsChecked = selectAll
            };
            cb.Checked += (_, _) => { if (!_buildingLevels) { /* levels only used on Calculate */ } };
            cb.Unchecked += (_, _) => { };
            _levelBoxes[lv.Id] = cb;
            LevelChecks.Children.Add(cb);
        }
        _buildingLevels = false;
    }

    private HashSet<string> SelectedLevels() =>
        _levelBoxes.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void SelectAllLevels_Click(object sender, RoutedEventArgs e)
    {
        foreach (var cb in _levelBoxes.Values) cb.IsChecked = true;
    }

    private void SelectNoLevels_Click(object sender, RoutedEventArgs e)
    {
        foreach (var cb in _levelBoxes.Values) cb.IsChecked = false;
    }

    private void Calculate_Click(object sender, RoutedEventArgs e)
    {
        RateBookStore.Current.EnsureLoaded();
        var levels = SelectedLevels();
        if (levels.Count == 0)
        {
            Info.Title = "Estimate";
            Info.Message = "Select at least one storey.";
            Info.Severity = InfoBarSeverity.Warning;
            Info.IsOpen = true;
            return;
        }
        if (VersionCombo.SelectedItem is not ComboBoxItem { Tag: string verId })
        {
            Info.Title = "Estimate";
            Info.Message = "Select a rate book version (Outputs → Rate book to create one).";
            Info.Severity = InfoBarSeverity.Warning;
            Info.IsOpen = true;
            return;
        }
        var ver = RateBookStore.Current.Find(verId);
        if (ver is null)
        {
            Info.Title = "Estimate";
            Info.Message = "Rate book version not found.";
            Info.Severity = InfoBarSeverity.Error;
            Info.IsOpen = true;
            return;
        }

        var phase = (PhaseCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var result = MaterialsCalculator.WithPhase(phase, () => EstimateCalculator.Build(ProjectStore.Current, ver, levels));
        result.Phase = phase;
        // The first calculation is Rev 0; calculating again makes the next revision.
        result.Revision = ProjectStore.Current.LastEstimate is { } previous ? Office.Shared.RevisionLabels.Next(previous.Revision) : 0;
        EstimateCalculator.AttachCompanyArtifactRef(result, ProjectStore.Current);
        ProjectStore.Current.LastEstimate = result;
        ProjectStore.Current.LastEstimateRateBookVersionId = ver.Id;
        ProjectStore.Current.IsDirty = true;
        ProjectStore.Current.Notify();
        ShowResult(result);
    }

    /// <summary>Two extra lines under the totals: the civil items by estimation phase and by running-bill phase. Empty when no item has a phase.</summary>
    private static string PhaseSummary(EstimateResult result)
    {
        static string Line(string title, IReadOnlyList<(string Phase, double Amount)> split) =>
            split.Count == 0 || split.All(s => s.Phase == Office.Shared.PhaseTotals.NoPhase)
                ? ""
                : "\n" + title + ": " + string.Join("  ·  ", split.Select(s => $"{s.Phase} ₹ {s.Amount:N2}"));
        return Line("Civil items by estimation phase", result.CivilByPhase(false)) + Line("Civil items by running-bill phase", result.CivilByPhase(true));
    }

    private void ShowResult(EstimateResult result)
    {
        _result = result;
        TotalText.Text = $"₹ {result.GrandTotal:N2}  ·  {Office.Shared.RevisionLabels.Estimate(result.Revision, result.Phase)}";
        var mk = result.Markups;
        MarkupText.Text =
            $"Base ₹ {mk.BaseTotal:N2}  ·  Elec {mk.ElectricalPct:0.##}% ₹ {mk.ElectricalAmount:N2}  ·  " +
            $"Plumb {mk.PlumbingPct:0.##}% ₹ {mk.PlumbingAmount:N2}  ·  Esc {mk.EscalationPct:0.##}% ₹ {mk.EscalationAmount:N2}  ·  " +
            $"Fees {mk.ConsultingFeePct:0.##}% ₹ {mk.ConsultingFeeAmount:N2}"
            + PhaseSummary(result);
        RefreshProjectRefCaption(result);
        CivilTitle.Text = $"Abstract of cost — Civil / finishes / doors / windows ({result.Civil.Count})";
        MatTitle.Text = $"Abstract of cost — Materials ({result.Materials.Count})";
        SteelTitle.Text = $"Abstract of cost — Steel ({result.Steel.Count})";

        var (civilRows, next) = DsrEstimateFormat.ToRows(result.Civil, 1);
        var (matRows, next2) = DsrEstimateFormat.ToRows(result.Materials, next);
        var (steelRows, _) = DsrEstimateFormat.ToRows(result.Steel, next2);

        _civilTable.SetTable(DsrEstimateFormat.Headers, civilRows);
        _matTable.SetTable(DsrEstimateFormat.Headers, matRows);
        _steelTable.SetTable(DsrEstimateFormat.Headers, steelRows);

        Info.Title = "Estimate (DSR)";
        string miss = result.MissingCodes.Count == 0
            ? "all item codes priced"
            : $"{result.MissingCodes.Count} missing rate(s): {string.Join(", ", result.MissingCodes.Take(8))}"
              + (result.MissingCodes.Count > 8 ? "…" : "");
        var link = ProjectStore.Current.Shared is null
            ? "not linked to a company-file project — open one from File → Company projects so O-Studio can use this total"
            : "linked to the company file — save the project so O-Studio fee proposals can read this total";
        Info.Message = $"{result.RateBookVersionName} · grand total ₹ {result.GrandTotal:N2} · {miss} · {link}";
        Info.Severity = result.MissingCodes.Count > 0 || ProjectStore.Current.Shared is null
            ? InfoBarSeverity.Warning
            : InfoBarSeverity.Success;
        Info.IsOpen = true;
    }

    private void RefreshProjectRefCaption(EstimateResult result)
    {
        var company = BBSApp.Services.Company.CompanyService.Current;
        var link = ProjectStore.Current.Shared;
        if (company is null || link is null)
        {
            ProjectRefText.Text = "Studio project: not linked. A take-off recorded from a Studio project links it and puts a ProjectRef on this estimate.";
            return;
        }
        var artifact = result.ArtifactRef;
        var rev = artifact?["revision"]?.GetValue<uint>() ?? 0;
        ProjectRefText.Text =
            $"Company project · {company.Name} · project id {link.ProjectId} · ProjectRef v1 · estimate ArtifactRef revision {rev}. " +
            "O-Studio reads the grand total from the saved project file.";
    }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null)
        {
            Info.Title = "PDF";
            Info.Message = "Calculate an estimate first.";
            Info.Severity = InfoBarSeverity.Warning;
            Info.IsOpen = true;
            return;
        }
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
        picker.SuggestedFileName = "estimate_" + (_result.RateBookVersionName ?? "rates").Replace(' ', '_');
        picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        var annotated = await TakeoffAnnotatedPdf.TryCaptureAsync(ProjectStore.Current);
        var stored = await StoredAnnotatedPdf.RenderAllAsync(ProjectStore.Current);
        if (PdfExport.ExportEstimate(file.Path, ProjectStore.Current, _result, SelectedLevels(), out var err, annotated, stored))
        {
            Info.Title = "PDF";
            Info.Message = annotated is null && stored.Count == 0
                ? "Saved " + file.Path
                : "Saved (includes annotated drawing): " + file.Path;
            Info.Severity = InfoBarSeverity.Success;
        }
        else
        {
            Info.Title = "PDF";
            Info.Message = err ?? "Export failed";
            Info.Severity = InfoBarSeverity.Error;
        }
        Info.IsOpen = true;
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_result is null)
        {
            Info.Title = "CSV";
            Info.Message = "Calculate an estimate first.";
            Info.Severity = InfoBarSeverity.Warning;
            Info.IsOpen = true;
            return;
        }
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
        picker.SuggestedFileName = "estimate";
        picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", DsrEstimateFormat.Headers.Select(Csv)));
        int sl = 1;
        void dump(IEnumerable<EstimateLine> lines)
        {
            var (rows, next) = DsrEstimateFormat.ToRows(lines, sl);
            sl = next;
            foreach (var row in rows)
                sb.AppendLine(string.Join(",", row.Select(Csv)));
        }
        dump(_result.Civil);
        dump(_result.Materials);
        dump(_result.Steel);
        var mk = _result.Markups;
        sb.AppendLine($",Base total,,,,,,,{mk.BaseTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},");
        sb.AppendLine($",Electrical {mk.ElectricalPct:0.##}%,,,,,,,{mk.ElectricalAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},");
        sb.AppendLine($",Plumbing {mk.PlumbingPct:0.##}%,,,,,,,{mk.PlumbingAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},");
        sb.AppendLine($",Escalation {mk.EscalationPct:0.##}%,,,,,,,{mk.EscalationAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},");
        sb.AppendLine($",Consulting fees {mk.ConsultingFeePct:0.##}%,,,,,,,{mk.ConsultingFeeAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},");
        sb.AppendLine($",Grand total,,,,,,,{_result.GrandTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},");

        await FileIOWrite(file.Path, sb.ToString());
        Info.Title = "CSV";
        Info.Message = "Saved " + file.Path;
        Info.Severity = InfoBarSeverity.Success;
        Info.IsOpen = true;
    }

    private static string Csv(string s)
    {
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static async System.Threading.Tasks.Task FileIOWrite(string path, string text)
    {
        await System.IO.File.WriteAllTextAsync(path, text);
    }
}
