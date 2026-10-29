// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using BBSApp.Services;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BBSApp.Views;

public sealed partial class StoresPage : Page
{
    public enum StoresTab { Orders, Grn, Inventory, Masters }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly StoresBook _s = ProjectStore.Current.Stores;
    private readonly ObservableCollection<PoRow> _poRows = new();
    private readonly ObservableCollection<StoreLineVm> _poLineRows = new();
    private readonly ObservableCollection<GrnRow> _grnRows = new();
    private readonly ObservableCollection<StoreLineVm> _grnLineRows = new();
    private readonly ObservableCollection<StoreMasterVm<Supplier>> _supplierRows = new();
    private readonly ObservableCollection<StoreMasterVm<Warehouse>> _warehouseRows = new();
    private readonly ObservableCollection<InventoryRowVm> _invRows = new();
    private readonly List<string> _supplierIds = new();
    private readonly List<string> _warehouseIds = new();
    private readonly List<string> _poIds = new();
    private readonly StoresTab _initialTab;
    private PurchaseOrder? _po;
    private Grn? _grn;
    private bool _loading;

    public StoresPage(StoresTab tab = StoresTab.Orders)
    {
        _initialTab = tab;
        InitializeComponent();
        BBSApp.Controls.PivotTabs.HideHeaders(MainPivot);
        _s.EnsureSeeded();
        _loading = true;
        PrefixBox.Text = _s.Prefix;

        foreach (var p in _s.Orders) _poRows.Add(new PoRow(p));
        PoList.ItemsSource = _poRows;
        PoLinesGrid.ItemsSource = _poLineRows;

        foreach (var g in _s.Grns) _grnRows.Add(new GrnRow(g));
        GrnList.ItemsSource = _grnRows;
        GrnLinesGrid.ItemsSource = _grnLineRows;

        foreach (var sup in _s.Suppliers) _supplierRows.Add(new StoreMasterVm<Supplier>(sup));
        SuppliersGrid.ItemsSource = _supplierRows;
        foreach (var wh in _s.Warehouses) _warehouseRows.Add(new StoreMasterVm<Warehouse>(wh));
        WarehousesGrid.ItemsSource = _warehouseRows;
        InventoryGrid.ItemsSource = _invRows;
        _loading = false;

        Loaded += (_, _) =>
        {
            RebuildCombos();
            MainPivot.SelectedIndex = _initialTab switch
            { StoresTab.Grn => 1, StoresTab.Inventory => 2, StoresTab.Masters => 3, _ => 0 };
            if (_poRows.Count > 0) PoList.SelectedIndex = 0; else LoadPo(null);
            if (_grnRows.Count > 0) GrnList.SelectedIndex = 0; else LoadGrn(null);
            RefreshInventory();
            UpdateSummary();
        };
    }

    private string Company => ProjectStore.Current.Info.CompanyDisplay;

    private void UpdateSummary()
    {
        SummaryText.Text = $"{_s.Orders.Count} PO(s) · {_s.Grns.Count(g => g.Received)} received GRN(s) · "
            + $"{_s.Suppliers.Count} supplier(s) · {_s.Warehouses.Count} store(s).";
    }

    private void Pivot_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        RebuildCombos();
        if (MainPivot.SelectedIndex == 2) RefreshInventory();
    }

    private void RebuildCombos()
    {
        _loading = true;
        // suppliers
        _supplierIds.Clear();
        var sup = new List<string> { "(none)" }; _supplierIds.Add("");
        foreach (var s in _s.Suppliers) { sup.Add(string.IsNullOrWhiteSpace(s.Name) ? "(unnamed)" : s.Name); _supplierIds.Add(s.Id); }
        int poSup = PoSupplierBox.SelectedIndex;
        PoSupplierBox.ItemsSource = sup;
        PoSupplierBox.SelectedIndex = _po is null ? 0 : Math.Max(0, _supplierIds.IndexOf(_po.SupplierId));
        // warehouses
        _warehouseIds.Clear();
        var wh = new List<string>();
        foreach (var w in _s.Warehouses) { wh.Add(string.IsNullOrWhiteSpace(w.Name) ? "(unnamed)" : w.Name); _warehouseIds.Add(w.Id); }
        PoWarehouseBox.ItemsSource = wh.ToList();
        GrnWarehouseBox.ItemsSource = wh.ToList();
        if (_po is not null) PoWarehouseBox.SelectedIndex = Math.Max(0, _warehouseIds.IndexOf(_po.WarehouseId));
        if (_grn is not null) GrnWarehouseBox.SelectedIndex = Math.Max(0, _warehouseIds.IndexOf(_grn.WarehouseId));
        // POs for GRN
        _poIds.Clear();
        var poList = new List<string> { "(none)" }; _poIds.Add("");
        foreach (var p in _s.Orders) { poList.Add(string.IsNullOrWhiteSpace(p.Number) ? "(draft PO)" : p.Number); _poIds.Add(p.Id); }
        GrnPoBox.ItemsSource = poList;
        if (_grn is not null) GrnPoBox.SelectedIndex = Math.Max(0, _poIds.IndexOf(_grn.PoId));
        _loading = false;
    }

    // ---------------- Purchase orders ----------------
    private PoRow? PoRowFor(PurchaseOrder p) => _poRows.FirstOrDefault(r => r.P == p);

    private void PoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SavePo();
        _po = (PoList.SelectedItem as PoRow)?.P;
        LoadPo(_po);
    }

    private void LoadPo(PurchaseOrder? p)
    {
        _loading = true;
        _po = p;
        PoEditor.Opacity = p is null ? 0.5 : 1;
        _poLineRows.Clear();
        if (p is null)
        {
            PoDateBox.Date = DateTimeOffset.Now;
            PoSupplierBox.SelectedIndex = 0;
            if (PoWarehouseBox.Items.Count > 0) PoWarehouseBox.SelectedIndex = 0;
            PoNotesBox.Text = "";
            PoNumberText.Text = "—";
            SetPoLocked(true);
            RefreshPoTotal();
            _loading = false;
            return;
        }
        PoDateBox.Date = new DateTimeOffset(p.Date);
        PoSupplierBox.SelectedIndex = Math.Max(0, _supplierIds.IndexOf(p.SupplierId));
        PoWarehouseBox.SelectedIndex = Math.Max(0, _warehouseIds.IndexOf(p.WarehouseId));
        PoNotesBox.Text = p.Notes;
        foreach (var l in p.Lines) _poLineRows.Add(new StoreLineVm(l, RefreshPoTotal));
        UpdatePoNumber();
        SetPoLocked(p.Placed);
        RefreshPoTotal();
        _loading = false;
    }

    private void SavePo()
    {
        if (_po is null || _po.Placed) return;
        if (PoDateBox.Date is { } d) _po.Date = d.DateTime.Date;
        int si = PoSupplierBox.SelectedIndex;
        _po.SupplierId = si >= 0 && si < _supplierIds.Count ? _supplierIds[si] : "";
        _po.SupplierName = si > 0 && PoSupplierBox.SelectedItem is string s ? s : "";
        int wi = PoWarehouseBox.SelectedIndex;
        _po.WarehouseId = wi >= 0 && wi < _warehouseIds.Count ? _warehouseIds[wi] : "";
        _po.Notes = PoNotesBox.Text ?? "";
        PoRowFor(_po)?.Refresh();
    }

    private void SetPoLocked(bool locked)
    {
        bool en = !locked;
        foreach (var c in new Control[] { PoDateBox, PoSupplierBox, PoWarehouseBox, PoNotesBox }) c.IsEnabled = en;
        PoLinesGrid.IsReadOnly = locked;
        PoLockBar.IsOpen = locked;
    }

    private void UpdatePoNumber()
    {
        if (_po is null) { PoNumberText.Text = "—"; return; }
        PoNumberText.Text = _po.Placed && !string.IsNullOrWhiteSpace(_po.Number)
            ? _po.Number : _s.Preview("PO", _po.Date, Company) + " (draft)";
    }

    private void RefreshPoTotal()
    {
        double total = _poLineRows.Sum(v => v.L.Amount);
        PoTotalText.Text = "Total: " + total.ToString("N2", Inv);
        PoRowFor(_po!)?.Refresh();
    }

    private void PoSupplier_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) UpdatePoNumber(); }
    private void PoLines_CellEditEnded(object sender, DataGridCellEditEndedEventArgs e) { RefreshPoTotal(); ProjectStore.Current.Notify(); }

    private void NewPo_Click(object sender, RoutedEventArgs e)
    {
        SavePo();
        var p = new PurchaseOrder();
        if (_s.Warehouses.Count > 0) p.WarehouseId = _s.Warehouses[0].Id;
        _s.Orders.Add(p);
        var row = new PoRow(p); _poRows.Add(row);
        RebuildCombos();
        ProjectStore.Current.Notify();
        PoList.SelectedItem = row;
        UpdateSummary();
    }

    private void SavePo_Click(object sender, RoutedEventArgs e)
    {
        if (_po is null) { AppNotify.Info("Nothing to save", "Create a PO first."); return; }
        SavePo(); UpdatePoNumber(); RebuildCombos(); ProjectStore.Current.Notify();
        AppNotify.Success("Saved", "Purchase order");
    }

    private async void PlacePo_Click(object sender, RoutedEventArgs e)
    {
        if (_po is null) { AppNotify.Info("Nothing to place", "Create a PO first."); return; }
        SavePo();
        if (_po.Placed) { AppNotify.Info("Already placed", _po.Number); return; }
        var dlg = new ContentDialog
        {
            Title = "Place purchase order",
            Content = $"Assign {_s.Preview("PO", _po.Date, Company)} (total Rs. {_po.Total:N2}) and lock this PO?",
            PrimaryButtonText = "Place", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        _po.Number = _s.Assign("PO", _po.Date, Company);
        _po.Placed = true;
        PoRowFor(_po)?.Refresh(); LoadPo(_po); RebuildCombos();
        ProjectStore.Current.Notify(); UpdateSummary();
        AppNotify.Success("PO placed", _po.Number);
    }

    private void DeletePo_Click(object sender, RoutedEventArgs e)
    {
        if (_po is null) return;
        var p = _po; int idx = _poRows.IndexOf(PoRowFor(p)!);
        _s.Orders.Remove(p);
        var row = PoRowFor(p); if (row is not null) _poRows.Remove(row);
        RebuildCombos(); ProjectStore.Current.Notify(); UpdateSummary();
        if (_poRows.Count > 0) PoList.SelectedIndex = Math.Clamp(idx, 0, _poRows.Count - 1); else LoadPo(null);
    }

    private void AddPoLine_Click(object sender, RoutedEventArgs e)
    {
        if (_po is null || _po.Placed) return;
        var l = new StoreLine { Material = "Material", Unit = "no", Qty = 1, Rate = 0 };
        _po.Lines.Add(l); _poLineRows.Add(new StoreLineVm(l, RefreshPoTotal));
        RefreshPoTotal(); ProjectStore.Current.Notify();
    }
    private void DeletePoLine_Click(object sender, RoutedEventArgs e)
    {
        if (_po is null || _po.Placed || PoLinesGrid.SelectedItem is not StoreLineVm vm) return;
        _po.Lines.Remove(vm.L); _poLineRows.Remove(vm); RefreshPoTotal(); ProjectStore.Current.Notify();
    }

    private async void ExportPo_Click(object sender, RoutedEventArgs e)
    {
        if (_po is null) { AppNotify.Info("Nothing to export", "Create a PO first."); return; }
        SavePo();
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
        picker.SuggestedFileName = string.IsNullOrWhiteSpace(_po.Number) ? $"PO_{_po.Date:yyyyMMdd}" : _po.Number.Replace('/', '-');
        picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        if (PdfExport.ExportStorePurchaseOrder(file.Path, ProjectStore.Current, _po, out var err))
            AppNotify.Success("PO exported", file.Name);
        else AppNotify.Error("Export failed", err ?? "Could not write PDF.");
    }

    // ---------------- GRN ----------------
    private GrnRow? GrnRowFor(Grn g) => _grnRows.FirstOrDefault(r => r.G == g);

    private void GrnList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SaveGrn();
        _grn = (GrnList.SelectedItem as GrnRow)?.G;
        LoadGrn(_grn);
    }

    private void LoadGrn(Grn? g)
    {
        _loading = true;
        _grn = g;
        GrnEditor.Opacity = g is null ? 0.5 : 1;
        _grnLineRows.Clear();
        if (g is null)
        {
            GrnDateBox.Date = DateTimeOffset.Now;
            GrnPoBox.SelectedIndex = 0;
            if (GrnWarehouseBox.Items.Count > 0) GrnWarehouseBox.SelectedIndex = 0;
            GrnSupplierBox.Text = "";
            GrnNumberText.Text = "—";
            SetGrnLocked(true);
            _loading = false;
            return;
        }
        GrnDateBox.Date = new DateTimeOffset(g.Date);
        GrnPoBox.SelectedIndex = Math.Max(0, _poIds.IndexOf(g.PoId));
        GrnWarehouseBox.SelectedIndex = Math.Max(0, _warehouseIds.IndexOf(g.WarehouseId));
        GrnSupplierBox.Text = g.SupplierName;
        foreach (var l in g.Lines) _grnLineRows.Add(new StoreLineVm(l, () => { }));
        UpdateGrnNumber();
        SetGrnLocked(g.Received);
        _loading = false;
    }

    private void SaveGrn()
    {
        if (_grn is null || _grn.Received) return;
        if (GrnDateBox.Date is { } d) _grn.Date = d.DateTime.Date;
        int pi = GrnPoBox.SelectedIndex;
        _grn.PoId = pi >= 0 && pi < _poIds.Count ? _poIds[pi] : "";
        int wi = GrnWarehouseBox.SelectedIndex;
        _grn.WarehouseId = wi >= 0 && wi < _warehouseIds.Count ? _warehouseIds[wi] : "";
        _grn.SupplierName = GrnSupplierBox.Text ?? "";
        GrnRowFor(_grn)?.Refresh();
    }

    private void SetGrnLocked(bool locked)
    {
        bool en = !locked;
        foreach (var c in new Control[] { GrnDateBox, GrnPoBox, GrnWarehouseBox, GrnSupplierBox }) c.IsEnabled = en;
        GrnLinesGrid.IsReadOnly = locked;
        GrnLockBar.IsOpen = locked;
    }

    private void UpdateGrnNumber()
    {
        if (_grn is null) { GrnNumberText.Text = "—"; return; }
        GrnNumberText.Text = _grn.Received && !string.IsNullOrWhiteSpace(_grn.Number)
            ? _grn.Number : _s.Preview("GRN", _grn.Date, Company) + " (draft)";
    }

    private void GrnPo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _grn is null || _grn.Received) return;
        int pi = GrnPoBox.SelectedIndex;
        if (pi <= 0 || pi >= _poIds.Count) return;
        var po = _s.Orders.FirstOrDefault(x => x.Id == _poIds[pi]);
        if (po is null) return;
        _grn.Lines.Clear(); _grnLineRows.Clear();
        foreach (var l in po.Lines)
        {
            var nl = new StoreLine { Material = l.Material, Unit = l.Unit, Qty = l.Qty, Rate = l.Rate };
            _grn.Lines.Add(nl); _grnLineRows.Add(new StoreLineVm(nl, () => { }));
        }
        if (string.IsNullOrWhiteSpace(GrnSupplierBox.Text)) GrnSupplierBox.Text = po.SupplierName;
        if (!string.IsNullOrWhiteSpace(po.WarehouseId)) GrnWarehouseBox.SelectedIndex = Math.Max(0, _warehouseIds.IndexOf(po.WarehouseId));
        ProjectStore.Current.Notify();
    }

    private void GrnLines_CellEditEnded(object sender, DataGridCellEditEndedEventArgs e) => ProjectStore.Current.Notify();

    private void NewGrn_Click(object sender, RoutedEventArgs e)
    {
        SaveGrn();
        var g = new Grn();
        if (_s.Warehouses.Count > 0) g.WarehouseId = _s.Warehouses[0].Id;
        _s.Grns.Add(g);
        var row = new GrnRow(g); _grnRows.Add(row);
        ProjectStore.Current.Notify();
        GrnList.SelectedItem = row; UpdateSummary();
    }

    private void SaveGrn_Click(object sender, RoutedEventArgs e)
    {
        if (_grn is null) { AppNotify.Info("Nothing to save", "Create a GRN first."); return; }
        SaveGrn(); UpdateGrnNumber(); ProjectStore.Current.Notify();
        AppNotify.Success("Saved", "Goods receipt note");
    }

    private async void ReceiveGrn_Click(object sender, RoutedEventArgs e)
    {
        if (_grn is null) { AppNotify.Info("Nothing to receive", "Create a GRN first."); return; }
        SaveGrn();
        if (_grn.Received) { AppNotify.Info("Already received", _grn.Number); return; }
        var dlg = new ContentDialog
        {
            Title = "Receive goods",
            Content = $"Assign {_s.Preview("GRN", _grn.Date, Company)} and add {_grn.Lines.Count} line(s) to "
                      + $"{_s.WarehouseName(_grn.WarehouseId)} stock? The GRN will be locked.",
            PrimaryButtonText = "Receive", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        _grn.Number = _s.Assign("GRN", _grn.Date, Company);
        _grn.Received = true;
        GrnRowFor(_grn)?.Refresh(); LoadGrn(_grn);
        ProjectStore.Current.Notify(); RefreshInventory(); UpdateSummary();
        AppNotify.Success("Goods received", _grn.Number + " added to stock");
    }

    private void DeleteGrn_Click(object sender, RoutedEventArgs e)
    {
        if (_grn is null) return;
        var g = _grn; int idx = _grnRows.IndexOf(GrnRowFor(g)!);
        _s.Grns.Remove(g);
        var row = GrnRowFor(g); if (row is not null) _grnRows.Remove(row);
        ProjectStore.Current.Notify(); RefreshInventory(); UpdateSummary();
        if (_grnRows.Count > 0) GrnList.SelectedIndex = Math.Clamp(idx, 0, _grnRows.Count - 1); else LoadGrn(null);
    }

    private void AddGrnLine_Click(object sender, RoutedEventArgs e)
    {
        if (_grn is null || _grn.Received) return;
        var l = new StoreLine { Material = "Material", Unit = "no", Qty = 1, Rate = 0 };
        _grn.Lines.Add(l); _grnLineRows.Add(new StoreLineVm(l, () => { }));
        ProjectStore.Current.Notify();
    }
    private void DeleteGrnLine_Click(object sender, RoutedEventArgs e)
    {
        if (_grn is null || _grn.Received || GrnLinesGrid.SelectedItem is not StoreLineVm vm) return;
        _grn.Lines.Remove(vm.L); _grnLineRows.Remove(vm); ProjectStore.Current.Notify();
    }

    // ---------------- Inventory ----------------
    private void RefreshInventory()
    {
        _invRows.Clear();
        foreach (var r in _s.Inventory()) _invRows.Add(new InventoryRowVm(r));
    }
    private void RefreshInventory_Click(object sender, RoutedEventArgs e) => RefreshInventory();

    private async void IssueMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_s.Warehouses.Count == 0) { AppNotify.Info("No store", "Add a warehouse in Masters first."); return; }
        var matBox = new TextBox { Header = "Material", PlaceholderText = "Cement (OPC 53)" };
        var unitBox = new TextBox { Header = "Unit", PlaceholderText = "bag" };
        var qtyBox = new NumberBox { Header = "Quantity", Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
        var whBox = new ComboBox { Header = "From store", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = _s.Warehouses.Select(w => w.Name).ToList(), SelectedIndex = 0 };
        var toBox = new TextBox { Header = "Issued to", PlaceholderText = "Site / gang" };
        var panel = new StackPanel { Spacing = 8, Children = { matBox, unitBox, qtyBox, whBox, toBox } };
        var dlg = new ContentDialog
        {
            Title = "Issue material (stock out)",
            Content = panel, PrimaryButtonText = "Issue", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(matBox.Text) || double.IsNaN(qtyBox.Value) || qtyBox.Value <= 0)
        { AppNotify.Warning("Incomplete", "Enter a material and a positive quantity."); return; }
        int wi = whBox.SelectedIndex;
        var iss = new StockIssue
        {
            Material = matBox.Text.Trim(),
            Unit = unitBox.Text ?? "",
            Qty = qtyBox.Value,
            WarehouseId = wi >= 0 && wi < _s.Warehouses.Count ? _s.Warehouses[wi].Id : "",
            IssuedTo = toBox.Text ?? "",
            Number = _s.Assign("ISS", DateTime.Today, Company)
        };
        _s.Issues.Add(iss);
        ProjectStore.Current.Notify();
        RefreshInventory();
        AppNotify.Success("Issued", $"{iss.Qty:0.##} {iss.Unit} {iss.Material} ({iss.Number})");
    }

    // ---------------- Masters ----------------
    private void Prefix_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _s.Prefix = (PrefixBox.Text ?? "").Trim();
        ProjectStore.Current.Notify();
        foreach (var r in _poRows) r.Refresh();
        foreach (var r in _grnRows) r.Refresh();
        UpdatePoNumber(); UpdateGrnNumber();
    }

    private void AddSupplier_Click(object sender, RoutedEventArgs e)
    {
        var s = new Supplier { Name = "New supplier" };
        _s.Suppliers.Add(s); _supplierRows.Add(new StoreMasterVm<Supplier>(s));
        ProjectStore.Current.Notify(); UpdateSummary();
    }
    private void DeleteSupplier_Click(object sender, RoutedEventArgs e)
    {
        if (SuppliersGrid.SelectedItem is not StoreMasterVm<Supplier> vm) return;
        _s.Suppliers.Remove(vm.Item); _supplierRows.Remove(vm);
        ProjectStore.Current.Notify(); UpdateSummary();
    }
    private void AddWarehouse_Click(object sender, RoutedEventArgs e)
    {
        var w = new Warehouse { Name = "New store" };
        _s.Warehouses.Add(w); _warehouseRows.Add(new StoreMasterVm<Warehouse>(w));
        ProjectStore.Current.Notify(); UpdateSummary();
    }
    private void DeleteWarehouse_Click(object sender, RoutedEventArgs e)
    {
        if (WarehousesGrid.SelectedItem is not StoreMasterVm<Warehouse> vm) return;
        _s.Warehouses.Remove(vm.Item); _warehouseRows.Remove(vm);
        ProjectStore.Current.Notify(); UpdateSummary();
    }
}

// ---------------- view-models ----------------
public sealed class PoRow : INotifyPropertyChanged
{
    public PurchaseOrder P { get; }
    public PoRow(PurchaseOrder p) { P = p; }
    public string Header => P.Placed && !string.IsNullOrWhiteSpace(P.Number) ? P.Number : "PO · (draft)";
    public string Sub => $"{(string.IsNullOrWhiteSpace(P.SupplierName) ? "(no supplier)" : P.SupplierName)} · {P.Date:dd MMM yyyy} · Rs. {P.Total:N0}";
    public void Refresh() { OnP(nameof(Header)); OnP(nameof(Sub)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class GrnRow : INotifyPropertyChanged
{
    public Grn G { get; }
    public GrnRow(Grn g) { G = g; }
    public string Header => G.Received && !string.IsNullOrWhiteSpace(G.Number) ? G.Number : "GRN · (draft)";
    public string Sub => $"{(string.IsNullOrWhiteSpace(G.SupplierName) ? "(no supplier)" : G.SupplierName)} · {G.Date:dd MMM yyyy} · {G.Lines.Count} line(s)";
    public void Refresh() { OnP(nameof(Header)); OnP(nameof(Sub)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class StoreLineVm : INotifyPropertyChanged
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly Action _changed;
    public StoreLine L { get; }
    public StoreLineVm(StoreLine l, Action changed) { L = l; _changed = changed; }
    public string Material { get => L.Material; set { L.Material = value ?? ""; OnP(); } }
    public string Unit { get => L.Unit; set { L.Unit = value ?? ""; OnP(); } }
    public string QtyText { get => L.Qty.ToString("0.###", Inv); set { if (double.TryParse(value, NumberStyles.Float, Inv, out var d)) L.Qty = d; OnP(nameof(QtyText)); OnP(nameof(AmountText)); _changed(); } }
    public string RateText { get => L.Rate.ToString("0.##", Inv); set { if (double.TryParse(value, NumberStyles.Float, Inv, out var d)) L.Rate = d; OnP(nameof(RateText)); OnP(nameof(AmountText)); _changed(); } }
    public string AmountText => L.Amount.ToString("0.00", Inv);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class StoreMasterVm<T> : INotifyPropertyChanged where T : class
{
    public T Item { get; }
    public StoreMasterVm(T item) { Item = item; }
    public string Name
    {
        get => Item is Supplier s ? s.Name : Item is Warehouse w ? w.Name : "";
        set { if (Item is Supplier s) s.Name = value ?? ""; else if (Item is Warehouse w) w.Name = value ?? ""; OnP(); }
    }
    public string Contact { get => (Item as Supplier)?.Contact ?? ""; set { if (Item is Supplier s) s.Contact = value ?? ""; OnP(); } }
    public string Gstin { get => (Item as Supplier)?.Gstin ?? ""; set { if (Item is Supplier s) s.Gstin = value ?? ""; OnP(); } }
    public string Address { get => (Item as Supplier)?.Address ?? ""; set { if (Item is Supplier s) s.Address = value ?? ""; OnP(); } }
    public string Location { get => (Item as Warehouse)?.Location ?? ""; set { if (Item is Warehouse w) w.Location = value ?? ""; OnP(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class InventoryRowVm
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public InventoryRowVm(StockRow r)
    {
        Material = r.Material; Unit = r.Unit; Warehouse = r.Warehouse;
        ReceivedText = r.Received.ToString("0.###", Inv);
        IssuedText = r.Issued.ToString("0.###", Inv);
        InStockText = r.InStock.ToString("0.###", Inv);
    }
    public string Material { get; }
    public string Unit { get; }
    public string Warehouse { get; }
    public string ReceivedText { get; }
    public string IssuedText { get; }
    public string InStockText { get; }
}
