// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using BBSApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.WinUI.UI.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BBSApp.Views;

public sealed partial class ContractsPage : Page
{
    public enum ContractsTab { Contracts, Rates, Terms }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] KindNames =
        { "Item-rate work order", "Lump-sum work order", "Tender" };

    private readonly ContractRegister _reg = ProjectStore.Current.ContractBook;
    private readonly ObservableCollection<ContractRow> _contractRows = new();
    private readonly ObservableCollection<ContractLineVm> _lineRows = new();
    private readonly ObservableCollection<RateRowVm> _rateRows = new();
    private readonly ObservableCollection<TermRow> _termRows = new();
    private readonly ContractsTab _initialTab;
    private Contract? _current;
    private StandardTerm? _currentTerm;
    private bool _loading;

    public ContractsPage(ContractsTab tab = ContractsTab.Contracts)
    {
        _initialTab = tab;
        InitializeComponent();
        BBSApp.Controls.PivotTabs.HideHeaders(MainPivot);
        _reg.EnsureSeeded();
        _loading = true;
        NewKindBox.ItemsSource = KindNames; NewKindBox.SelectedIndex = 0;
        KindBox.ItemsSource = KindNames;
        PrefixBox.Text = _reg.Prefix;

        foreach (var c in _reg.Contracts) _contractRows.Add(new ContractRow(_reg, c, Company));
        ContractList.ItemsSource = _contractRows;
        LinesGrid.ItemsSource = _lineRows;

        foreach (var r in _reg.Rates) _rateRows.Add(new RateRowVm(r));
        RatesGrid.ItemsSource = _rateRows;

        foreach (var t in _reg.Terms) _termRows.Add(new TermRow(t));
        TermsList.ItemsSource = _termRows;
        _loading = false;

        Loaded += (_, _) =>
        {
            MainPivot.SelectedIndex = _initialTab switch
            {
                ContractsTab.Rates => 1,
                ContractsTab.Terms => 2,
                _ => 0
            };
            if (_contractRows.Count > 0) ContractList.SelectedIndex = 0; else LoadContract(null);
            UpdateSummary();
        };
    }

    private string Company => ProjectStore.Current.Info.CompanyDisplay;
    private void Pivot_Changed(object sender, SelectionChangedEventArgs e) { }

    private void UpdateSummary()
    {
        int fin = _reg.Contracts.Count(c => c.Finalized);
        SummaryText.Text = $"{_reg.Contracts.Count} contract(s) · {fin} finalized · "
            + $"{_reg.Rates.Count} rate items · {_reg.Terms.Count} standard terms.";
    }

    private static int KindToIndex(ContractKind k) => (int)k;
    private static ContractKind IndexToKind(int i) => (ContractKind)Math.Clamp(i, 0, 2);
    private ContractRow? RowFor(Contract c) => _contractRows.FirstOrDefault(r => r.C == c);

    // ---------- Contract editor ----------
    private void ContractList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SaveContract();
        _current = (ContractList.SelectedItem as ContractRow)?.C;
        LoadContract(_current);
    }

    private void LoadContract(Contract? c)
    {
        _loading = true;
        _current = c;
        EditorPanel.Opacity = c is null ? 0.5 : 1;
        _lineRows.Clear();
        if (c is null)
        {
            KindBox.SelectedIndex = 0;
            AwardBox.Date = DateTimeOffset.Now;
            CompletionBox.Date = DateTimeOffset.Now.AddDays(30);
            TitleBox.Text = ContractorNameBox.Text = ContractorAddressBox.Text = ScopeBox.Text = "";
            TermsBox.Text = "";
            RetentionBox.Value = 5; LumpSumBox.Value = 0;
            NumberText.Text = "—";
            SetLocked(true);
            _loading = false;
            return;
        }
        KindBox.SelectedIndex = KindToIndex(c.Kind);
        AwardBox.Date = new DateTimeOffset(c.AwardDate);
        CompletionBox.Date = new DateTimeOffset(c.CompletionDate);
        TitleBox.Text = c.Title;
        ContractorNameBox.Text = c.ContractorName;
        ContractorAddressBox.Text = c.ContractorAddress;
        ScopeBox.Text = c.Scope;
        RetentionBox.Value = c.RetentionPct;
        LumpSumBox.Value = c.LumpSumValue;
        TermsBox.Text = string.Join("\n", c.Terms);
        foreach (var l in c.Lines) _lineRows.Add(new ContractLineVm(l, RefreshTotal));
        RefreshTotal();
        UpdateKindPanels(c.Kind);
        UpdateNumberText();
        SetLocked(c.Finalized);
        _loading = false;
    }

    private void SaveContract()
    {
        if (_current is null || _current.Finalized) return;
        _current.Kind = IndexToKind(KindBox.SelectedIndex);
        if (AwardBox.Date is { } a) _current.AwardDate = a.DateTime.Date;
        if (CompletionBox.Date is { } cd) _current.CompletionDate = cd.DateTime.Date;
        _current.Title = TitleBox.Text ?? "";
        _current.ContractorName = ContractorNameBox.Text ?? "";
        _current.ContractorAddress = ContractorAddressBox.Text ?? "";
        _current.Scope = ScopeBox.Text ?? "";
        _current.RetentionPct = double.IsNaN(RetentionBox.Value) ? 0 : RetentionBox.Value;
        _current.LumpSumValue = double.IsNaN(LumpSumBox.Value) ? 0 : LumpSumBox.Value;
        _current.Terms.Clear();
        foreach (var line in (TermsBox.Text ?? "").Replace("\r\n", "\n").Split('\n'))
            if (!string.IsNullOrWhiteSpace(line)) _current.Terms.Add(line.Trim());
        RowFor(_current)?.Refresh();
    }

    private void SetLocked(bool locked)
    {
        bool en = !locked;
        foreach (var ctl in new Control[] { KindBox, AwardBox, CompletionBox, TitleBox, ContractorNameBox,
                     ContractorAddressBox, ScopeBox, RetentionBox, LumpSumBox })
            ctl.IsEnabled = en;
        TermsBox.IsReadOnly = locked;
        LinesGrid.IsReadOnly = locked;
        LockBar.IsOpen = locked;
    }

    private void UpdateKindPanels(ContractKind k)
    {
        bool itemRate = k != ContractKind.LumpSumWorkOrder;
        LinesPanel.Visibility = itemRate ? Visibility.Visible : Visibility.Collapsed;
        LumpSumBox.Visibility = itemRate ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateNumberText()
    {
        if (_current is null) { NumberText.Text = "—"; return; }
        NumberText.Text = _current.Finalized && !string.IsNullOrWhiteSpace(_current.Number)
            ? _current.Number
            : _reg.PreviewNumber(_current, Company) + " (draft)";
    }

    private void RefreshTotal()
    {
        double total = _lineRows.Sum(v => v.L.Amount);
        TotalText.Text = "Total: " + total.ToString("N2", Inv);
        RowFor(_current!)?.Refresh();
    }

    private void KindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _current is null) return;
        _current.Kind = IndexToKind(KindBox.SelectedIndex);
        UpdateKindPanels(_current.Kind);
        UpdateNumberText();
    }

    private void NewContract_Click(object sender, RoutedEventArgs e)
    {
        SaveContract();
        var c = new Contract
        {
            Kind = IndexToKind(NewKindBox.SelectedIndex),
            IssuedByRole = ProjectStore.Current.Parties.Active
        };
        _reg.Contracts.Add(c);
        var row = new ContractRow(_reg, c, Company);
        _contractRows.Add(row);
        ProjectStore.Current.Notify();
        ContractList.SelectedItem = row;
        UpdateSummary();
    }

    private void SaveContract_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { AppNotify.Info("Nothing to save", "Create a contract first."); return; }
        SaveContract();
        UpdateNumberText();
        ProjectStore.Current.Notify();
        AppNotify.Success("Saved", Contract.KindDisplay(_current.Kind));
    }

    private async void FinalizeContract_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { AppNotify.Info("Nothing to finalize", "Create a contract first."); return; }
        SaveContract();
        if (_current.Finalized) { AppNotify.Info("Already finalized", _current.Number); return; }
        var dlg = new ContentDialog
        {
            Title = "Finalize & assign number",
            Content = $"Assign number {_reg.PreviewNumber(_current, Company)} and lock this "
                      + $"{Contract.KindDisplay(_current.Kind)} (value Rs. {_current.Value:N2})?",
            PrimaryButtonText = "Finalize",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        _reg.Finalize(_current, Company);
        RowFor(_current)?.Refresh();
        LoadContract(_current);
        ProjectStore.Current.Notify();
        UpdateSummary();
        AppNotify.Success("Finalized", _current.Number);
    }

    private void DeleteContract_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var c = _current;
        int idx = _contractRows.IndexOf(RowFor(c)!);
        _reg.Contracts.Remove(c);
        var row = RowFor(c);
        if (row is not null) _contractRows.Remove(row);
        ProjectStore.Current.Notify();
        UpdateSummary();
        if (_contractRows.Count > 0) ContractList.SelectedIndex = Math.Clamp(idx, 0, _contractRows.Count - 1);
        else LoadContract(null);
    }

    private void Prefix_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _reg.Prefix = (PrefixBox.Text ?? "").Trim();
        ProjectStore.Current.Notify();
        foreach (var r in _contractRows) r.Refresh();
        UpdateNumberText();
    }

    private async void ExportContract_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { AppNotify.Info("Nothing to export", "Create a contract first."); return; }
        SaveContract();
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
        picker.SuggestedFileName = string.IsNullOrWhiteSpace(_current.Number)
            ? $"{_current.KindCode}_{_current.AwardDate:yyyyMMdd}"
            : _current.Number.Replace('/', '-');
        picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        if (PdfExport.ExportContract(file.Path, ProjectStore.Current, _current, out var err))
            AppNotify.Success("Contract exported", file.Name);
        else
            AppNotify.Error("Export failed", err ?? "Could not write PDF.");
    }

    // ---------- line items ----------
    private void LinesGrid_CellEditEnded(object sender, DataGridCellEditEndedEventArgs e)
    {
        RefreshTotal();
        ProjectStore.Current.Notify();
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.Finalized) return;
        var l = new ContractLine { Description = "New item", Unit = "no", Qty = 1, Rate = 0 };
        _current.Lines.Add(l);
        _lineRows.Add(new ContractLineVm(l, RefreshTotal));
        RefreshTotal();
        ProjectStore.Current.Notify();
    }

    private async void AddFromRates_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.Finalized) return;
        var list = new ListView
        {
            ItemsSource = _reg.Rates.Select(r => $"{r.Code}  ·  {r.Description}  ·  {r.Unit}  ·  {r.Rate:0.##}").ToList(),
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 340
        };
        var dlg = new ContentDialog
        {
            Title = "Add item from schedule of rates",
            Content = new ScrollViewer { Content = list },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        int i = list.SelectedIndex;
        if (i < 0 || i >= _reg.Rates.Count) return;
        var r = _reg.Rates[i];
        var l = new ContractLine { Description = r.Description, Unit = r.Unit, Qty = 1, Rate = r.Rate };
        _current.Lines.Add(l);
        _lineRows.Add(new ContractLineVm(l, RefreshTotal));
        RefreshTotal();
        ProjectStore.Current.Notify();
    }

    private void DeleteLine_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.Finalized) return;
        if (LinesGrid.SelectedItem is not ContractLineVm vm) return;
        _current.Lines.Remove(vm.L);
        _lineRows.Remove(vm);
        RefreshTotal();
        ProjectStore.Current.Notify();
    }

    private async void AddTerm_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.Finalized) return;
        var list = new ListView
        {
            ItemsSource = _reg.Terms.Select(t => t.Title).ToList(),
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 340
        };
        var dlg = new ContentDialog
        {
            Title = "Add standard term",
            Content = new ScrollViewer { Content = list },
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        int i = list.SelectedIndex;
        if (i < 0 || i >= _reg.Terms.Count) return;
        string text = _reg.Terms[i].Text;
        TermsBox.Text = string.IsNullOrWhiteSpace(TermsBox.Text) ? text : TermsBox.Text.TrimEnd() + "\n" + text;
    }

    // ---------- schedule of rates ----------
    private void AddRate_Click(object sender, RoutedEventArgs e)
    {
        var r = new SorItem { Code = "NEW", Description = "New rate item", Unit = "no", Rate = 0 };
        _reg.Rates.Add(r);
        _rateRows.Add(new RateRowVm(r));
        ProjectStore.Current.Notify();
        UpdateSummary();
    }

    private void DeleteRate_Click(object sender, RoutedEventArgs e)
    {
        if (RatesGrid.SelectedItem is not RateRowVm vm) return;
        _reg.Rates.Remove(vm.R);
        _rateRows.Remove(vm);
        ProjectStore.Current.Notify();
        UpdateSummary();
    }

    // ---------- terms library ----------
    private void TermsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentTerm = (TermsList.SelectedItem as TermRow)?.T;
        _loading = true;
        TermTitleBox.Text = _currentTerm?.Title ?? "";
        TermTextBox.Text = _currentTerm?.Text ?? "";
        _loading = false;
    }

    private void TermField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || _currentTerm is null) return;
        _currentTerm.Title = TermTitleBox.Text ?? "";
        _currentTerm.Text = TermTextBox.Text ?? "";
        _termRows.FirstOrDefault(r => r.T == _currentTerm)?.Refresh();
        ProjectStore.Current.Notify();
    }

    private void AddTermLib_Click(object sender, RoutedEventArgs e)
    {
        var t = new StandardTerm { Title = "New clause", Text = "" };
        _reg.Terms.Add(t);
        var row = new TermRow(t);
        _termRows.Add(row);
        TermsList.SelectedItem = row;
        ProjectStore.Current.Notify();
        UpdateSummary();
    }

    private void DeleteTermLib_Click(object sender, RoutedEventArgs e)
    {
        if (TermsList.SelectedItem is not TermRow row) return;
        _reg.Terms.Remove(row.T);
        _termRows.Remove(row);
        _currentTerm = null;
        TermTitleBox.Text = TermTextBox.Text = "";
        ProjectStore.Current.Notify();
        UpdateSummary();
    }
}

// ---------- view-models ----------
public sealed class ContractRow : INotifyPropertyChanged
{
    private readonly ContractRegister _reg;
    private readonly string _company;
    public Contract C { get; }
    public ContractRow(ContractRegister reg, Contract c, string company) { _reg = reg; C = c; _company = company; }

    public string Header
    {
        get
        {
            string num = C.Finalized && !string.IsNullOrWhiteSpace(C.Number) ? C.Number : "(draft)";
            return $"{Contract.KindDisplay(C.Kind)} · {num}";
        }
    }
    public string Sub
    {
        get
        {
            string who = string.IsNullOrWhiteSpace(C.ContractorName) ? "(no contractor)" : C.ContractorName;
            return $"{who} · Rs. {C.Value:N0}";
        }
    }
    public void Refresh() { OnP(nameof(Header)); OnP(nameof(Sub)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class ContractLineVm : INotifyPropertyChanged
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly Action _changed;
    public ContractLine L { get; }
    public ContractLineVm(ContractLine l, Action changed) { L = l; _changed = changed; }

    public string Description { get => L.Description; set { L.Description = value ?? ""; OnP(); } }
    public string Unit { get => L.Unit; set { L.Unit = value ?? ""; OnP(); } }
    public string QtyText
    {
        get => L.Qty.ToString("0.###", Inv);
        set { if (double.TryParse(value, NumberStyles.Float, Inv, out var d)) L.Qty = d; OnP(nameof(QtyText)); OnP(nameof(AmountText)); _changed(); }
    }
    public string RateText
    {
        get => L.Rate.ToString("0.##", Inv);
        set { if (double.TryParse(value, NumberStyles.Float, Inv, out var d)) L.Rate = d; OnP(nameof(RateText)); OnP(nameof(AmountText)); _changed(); }
    }
    public string AmountText => L.Amount.ToString("0.00", Inv);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class RateRowVm : INotifyPropertyChanged
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public SorItem R { get; }
    public RateRowVm(SorItem r) { R = r; }
    public string Code { get => R.Code; set { R.Code = value ?? ""; OnP(); } }
    public string Description { get => R.Description; set { R.Description = value ?? ""; OnP(); } }
    public string Unit { get => R.Unit; set { R.Unit = value ?? ""; OnP(); } }
    public string RateText
    {
        get => R.Rate.ToString("0.##", Inv);
        set { if (double.TryParse(value, NumberStyles.Float, Inv, out var d)) R.Rate = d; OnP(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class TermRow : INotifyPropertyChanged
{
    public StandardTerm T { get; }
    public TermRow(StandardTerm t) { T = t; }
    public string Title { get => T.Title; set { T.Title = value ?? ""; OnP(); } }
    public void Refresh() => OnP(nameof(Title));
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
