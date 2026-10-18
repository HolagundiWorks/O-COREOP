// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text.RegularExpressions;
using BBSApp.Controls;
using BBSApp.Models;
using BBSApp.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace BBSApp.Views;

/// <summary>
/// Excel-style sheet entry (fields as columns) with optional row-detail form for extras.
/// </summary>
public sealed partial class ElementPage : Page
{
    private sealed class LevelOption
    {
        public required string Id { get; init; }
        public required string Display { get; init; }
        public override string ToString() => Display;
    }

    private sealed class ExtraLine
    {
        public required string Token { get; init; }
        public required string Display { get; init; }
        public override string ToString() => Display;
    }

    private readonly ElementSpec _spec;
    private readonly ObservableCollection<Dictionary<string, string>> _rows;
    private readonly NotifyCollectionChangedEventHandler _rowsChanged;
    private readonly Dictionary<string, FrameworkElement> _editors = new();
    private readonly Dictionary<string, UIElement> _fieldHosts = new();
    private readonly List<(ExtraPanelDef def, ObservableCollection<ExtraLine> list, ListView lv)> _extras = new();
    private readonly Dictionary<string, ObservableCollection<ExtraLine>> _barLists = new();
    private GenResult? _last;
    private readonly ResultTable _bbsTable = new();
    private readonly ResultTable _summaryTable = new();
    private readonly ResultTable _checksTable = new();
    private readonly ResultTable _finalTable = new();
    private int _editIndex = -1; // -1 = new record
    private bool _sheetEditBusy;
    private bool _suppressRecordCombo;
    private bool _sheetComboFillBusy;
    private string[] _sheetColumns = Array.Empty<string>();
    private string[] _deductSheetColumns = Array.Empty<string>();
    private Dictionary<string, FieldDef> _sheetFieldByKey = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, FieldDef> _deductFieldByKey = new(StringComparer.OrdinalIgnoreCase);
    private int _deductEditRow = -1;
    private ObservableCollection<Dictionary<string, string>>? _openingRows;
    private const double SheetColMinWidth = 112;
    private const double SheetComboColWidth = 128;

    private static readonly SolidColorBrush SheetGridBrush = new(Windows.UI.Color.FromArgb(255, 180, 180, 180));
    private static readonly SolidColorBrush SheetHeaderBg = new(Windows.UI.Color.FromArgb(255, 242, 242, 242));
    private static readonly SolidColorBrush SheetCellBg = new(Windows.UI.Color.FromArgb(255, 255, 255, 255));
    private static readonly SolidColorBrush SheetHeaderFg = new(Windows.UI.Color.FromArgb(255, 50, 50, 50));
    private static readonly SolidColorBrush SheetCellFg = new(Windows.UI.Color.FromArgb(255, 30, 30, 30));
    private static readonly SolidColorBrush SheetReadonlyBg = new(Windows.UI.Color.FromArgb(255, 245, 245, 245));
    private static readonly SolidColorBrush SheetActiveRowBg = new(Windows.UI.Color.FromArgb(255, 255, 251, 235));
    private int _sheetEditRow = -1;
    private string _sheetFloorId = "Lvl0";
    private bool _suppressFloorCombo;
    private readonly List<int> _sheetRowMap = new(); // visual row → store index
    private DispatcherQueueTimer? _enterNavTimer;
    private int _enterPendingRow;
    private int _enterPendingCol;
    private bool _enterPendingShift;

    private static readonly HashSet<string> NonNumericTextKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "mark", "bars", "top_bars", "bottom_bars", "hanger_bars", "pedestal_bars"
    };

    private const int FormColumns = 3;

    /// <summary>RCC always; civil doors/windows/masonry/pcc/etc. for dimension sketches.</summary>
    private bool ShowsSketch =>
        !_spec.IsCivilBoq
        || _spec.Kind is "doors" or "windows" or "masonry" or "plaster" or "painting"
            or "pcc" or "flooring" or "earthwork" or "ssm" or "shuttering"
            or "dpc" or "coping" or "screed" or "vdf" or "skirting" or "parapet"
            or "plinth_protection" or "waterproofing";

    public ElementPage(ElementSpec spec, ObservableCollection<Dictionary<string, string>> rows)
    {
        _spec = spec;
        _rows = rows;
        _rowsChanged = (_, _) => RefreshSheet();
        InitializeComponent();
        TitleText.Text = spec.Title;
        if (spec.IsCivilBoq)
        {
            SubtitleText.Text = spec.IsComputedFromRcc
                ? spec.Subtitle + " · Sheet from RCC — set Include, Refresh as needed."
                : spec.IsFinishReconcile
                    ? spec.Subtitle + " · Reconcile exposure, then Finalize finishes."
                    : spec.Subtitle + " · Excel sheet — edit cells, then Extract quantities.";
            GenerateBtn.Content = "Extract quantities";
            _bbsTable.SetAutomationName("Quantity take-off");
            _summaryTable.SetAutomationName("Quantity summary");
        }
        else
        {
            SubtitleText.Text = "IS 456 · Excel sheet entry — each field is a column. Use Row detail for extras.";
            GenerateBtn.Content = "Generate BBS";
            _bbsTable.SetAutomationName("Bar bending schedule");
            _summaryTable.SetAutomationName("Steel summary");
        }
        ChecksPanel.Visibility = spec.HasChecks || spec.IsCivilBoq ? Visibility.Visible : Visibility.Collapsed;
        BbsHost.Child = _bbsTable;
        SummaryHost.Child = _summaryTable;
        ChecksHost.Child = _checksTable;
        if (FinalTableHost is not null)
        {
            _finalTable.SetAutomationName("Finalized finishes");
            FinalTableHost.Child = _finalTable;
        }
        if (ShowsSketch)
        {
            SketchPanel.Visibility = Visibility.Visible;
            Grid.SetColumnSpan(SheetBorder, 1);
            Diagram.SetKind(spec.Kind);
            Diagram.Update(BuildDiagramSnapshot(), DiagramPart.None);
        }
        else
        {
            SketchPanel.Visibility = Visibility.Collapsed;
            Grid.SetColumnSpan(SheetBorder, 2);
        }
        BuildForm();
        InitFloorCombo();
        BuildSheetHeader();
        if (spec.Kind == "masonry")
        {
            DeductionsPivotItem.Visibility = Visibility.Visible;
            _openingRows = ProjectStore.Current.MasonryOpenings;
            BuildDeductSheetHeader();
        }
        else
        {
            DeductionsPivotItem.Visibility = Visibility.Collapsed;
        }
        if (spec.IsComputedFromRcc)
            ApplyComputedFromRccMode();
        if (spec.IsFinishReconcile)
            ApplyFinishReconcileMode();
        if (spec.Kind == "painting")
            FinishSurfacesCalculator.SyncPaintingFromPlaster(ProjectStore.Current);
        RefreshSheet();
        if (spec.Kind == "masonry")
            RefreshDeductSheet();
        if (_rows.Count > 0)
        {
            var visible = FilteredStoreIndices().ToList();
            if (visible.Count > 0)
            {
                _sheetEditRow = visible[0];
                LoadRecord(visible[0]);
                RefreshSheet();
                if (spec.Kind == "masonry") RefreshDeductSheet();
                FocusSheetCell(0, 0);
            }
            else if (!_spec.IsComputedFromRcc && !_spec.IsFinishReconcile)
                AddSheetRow();
            else
                NewRecord();
        }
        else if (!_spec.IsComputedFromRcc && !_spec.IsFinishReconcile)
            AddSheetRow();
        else
            NewRecord();
        rows.CollectionChanged += _rowsChanged;
        ProjectStore.Current.Changed += OnStoreChanged;
        Unloaded += OnUnloaded;
    }

    private void ApplyFinishReconcileMode()
    {
        FinishSurfacesCalculator.SyncPropose(ProjectStore.Current);
        NewBtn.Visibility = Visibility.Collapsed;
        DuplicateBtn.Visibility = Visibility.Collapsed;
        DeleteBtn.Visibility = Visibility.Collapsed;
        UndoBtn.Visibility = Visibility.Collapsed;
        RefreshComputedBtn.Visibility = Visibility.Visible;
        RefreshComputedBtn.Content = "Refresh from walls & RCC";
        FinalizeFinishBtn.Visibility = Visibility.Visible;
        SaveBtn.Visibility = Visibility.Collapsed;
        EditHint.Visibility = Visibility.Collapsed;
        ComputedHint.Visibility = Visibility.Visible;
        ComputedHint.Text = "Edit Include / sides exposed / ceiling · Refresh rebuilds · Finalize → Plaster + Paint";
        EntryPivotItem.Header = "Reconcile";
        FinalPivotItem.Visibility = Visibility.Visible;
        SheetTitleText.Text = "Reconcile";
        FloorCombo.Visibility = Visibility.Collapsed;
    }

    private void ApplyComputedFromRccMode()
    {
        ShutteringCalculator.SyncStore(ProjectStore.Current);
        NewBtn.Visibility = Visibility.Collapsed;
        DuplicateBtn.Visibility = Visibility.Collapsed;
        DeleteBtn.Visibility = Visibility.Collapsed;
        UndoBtn.Visibility = Visibility.Collapsed;
        RefreshComputedBtn.Visibility = Visibility.Visible;
        SaveBtn.Visibility = Visibility.Visible;
        EditHint.Visibility = Visibility.Collapsed;
        ComputedHint.Visibility = Visibility.Visible;
    }

    private void RefreshComputed_Click(object sender, RoutedEventArgs e)
    {
        if (_spec.IsFinishReconcile)
        {
            FinishSurfacesCalculator.SyncPropose(ProjectStore.Current);
            RefreshSheet();
            RefreshFinalList();
            if (_rows.Count > 0) LoadRecord(0);
            else NewRecord();
            AppNotify.Success("Finishes refreshed", $"{_rows.Count} proposed surface(s) from walls & RCC.");
            ProjectStore.Current.Notify();
            return;
        }
        if (!_spec.IsComputedFromRcc) return;
        ShutteringCalculator.SyncStore(ProjectStore.Current);
        RefreshSheet();
        if (_rows.Count > 0) LoadRecord(0);
        else NewRecord();
        AppNotify.Success("Shuttering refreshed", $"{_rows.Count} member(s) from RCC concrete.");
        ProjectStore.Current.Notify();
    }

    private void FinalizeFinish_Click(object sender, RoutedEventArgs e)
    {
        if (!_spec.IsFinishReconcile) return;
        FinishSurfacesCalculator.Finalize(ProjectStore.Current);
        RefreshFinalList();
        int n = ProjectStore.Current.Plaster.Count(r =>
            r.TryGetValue("source", out var s) && s.StartsWith("auto_", StringComparison.OrdinalIgnoreCase));
        AppNotify.Success("Finishes finalized",
            $"{n} plaster line(s); painting qty taken from plaster. Use Extract quantities on Results.");
        MainPivot.SelectedItem = FinalPivotItem;
    }

    private void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(MainPivot.SelectedItem, FinalPivotItem))
            RefreshFinalList();
    }

    private void RefreshFinalList()
    {
        if (FinalTableHost is null) return;
        var store = ProjectStore.Current;
        FinishSurfacesCalculator.SyncPaintingFromPlaster(store);
        double plasterSum = 0, paintSum = 0;
        var rows = new List<IReadOnlyList<string>>();
        foreach (var r in store.Plaster)
        {
            double a = 0;
            if (r.TryGetValue("area_m2", out var am))
                double.TryParse(am, NumberStyles.Float, CultureInfo.InvariantCulture, out a);
            plasterSum += a;
            rows.Add(new[]
            {
                "Plaster", Get(r, "mark"), Get(r, "level"), a.ToString("0.###", CultureInfo.InvariantCulture),
                Get(r, "source_mark"), Get(r, "notes")
            });
        }
        foreach (var r in store.Painting)
        {
            double a = 0;
            if (r.TryGetValue("area_m2", out var am))
                double.TryParse(am, NumberStyles.Float, CultureInfo.InvariantCulture, out a);
            paintSum += a;
            rows.Add(new[]
            {
                "Painting", Get(r, "mark"), Get(r, "level"), a.ToString("0.###", CultureInfo.InvariantCulture),
                Get(r, "source_mark"), Get(r, "notes")
            });
        }
        _finalTable.SetTable(
            new[] { "Kind", "Mark", "Level", "Area m²", "Source", "Notes" },
            rows);
        FinalSummaryText.Text =
            $"Proposed {store.FinishPropose.Count} · Plaster {plasterSum:0.###} m² · paint {paintSum:0.###} m² (paint from plaster).";
    }

    private static string Get(Dictionary<string, string> r, string key) =>
        r.TryGetValue(key, out var v) ? v : "";

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        _rows.CollectionChanged -= _rowsChanged;
        ProjectStore.Current.Changed -= OnStoreChanged;
    }

    private void OnStoreChanged()
    {
        RefreshLevelCombo();
        if (!_sheetEditBusy && !IsSheetEditorFocused())
            RefreshSheet();
        UpdateRecordLabel();
    }

    private bool IsSheetEditorFocused()
    {
        if (SheetRowsHost is null) return false;
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focused is not null)
        {
            if (ReferenceEquals(focused, SheetRowsHost)) return true;
            focused = VisualTreeHelper.GetParent(focused);
        }
        return false;
    }

    // ——— Dense Access form (3-column grid, flat section headings) ———

    private void BuildForm()
    {
        FormHost.Children.Clear();
        FormHost.RowDefinitions.Clear();
        FormHost.ColumnDefinitions.Clear();
        _editors.Clear();
        _fieldHosts.Clear();
        _extras.Clear();
        _barLists.Clear();

        for (int c = 0; c < FormColumns; c++)
            FormHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int row = 0, col = 0;
        void EnsureRow()
        {
            while (FormHost.RowDefinitions.Count <= row)
                FormHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        foreach (var f in _spec.Fields)
        {
            if (f.Kind == FieldKind.Section)
            {
                if (col != 0) { col = 0; row++; }
                EnsureRow();
                var heading = new TextBlock
                {
                    Text = f.Label,
                    Style = (Style)Application.Current.Resources["FormSectionStyle"],
                    Margin = new Thickness(0, row == 0 ? 0 : 10, 0, 4),
                    IsTapEnabled = true
                };
                heading.PointerPressed += (_, _) => HighlightField(f.Key);
                Grid.SetRow(heading, row);
                Grid.SetColumn(heading, 0);
                Grid.SetColumnSpan(heading, FormColumns);
                FormHost.Children.Add(heading);
                _fieldHosts[f.Key] = heading;
                row++;
                col = 0;
                continue;
            }

            EnsureRow();
            var control = CreateFieldControl(f);
            control.Margin = new Thickness(0, 0, 6, 4);
            if (f.Kind == FieldKind.BarList)
            {
                if (col != 0) { col = 0; row++; EnsureRow(); }
                Grid.SetRow(control, row);
                Grid.SetColumn(control, 0);
                Grid.SetColumnSpan(control, FormColumns);
                FormHost.Children.Add(control);
                _fieldHosts[f.Key] = control;
                row++;
                col = 0;
                continue;
            }
            Grid.SetRow(control, row);
            Grid.SetColumn(control, col);
            FormHost.Children.Add(control);
            _fieldHosts[f.Key] = control;
            col++;
            if (col >= FormColumns) { col = 0; row++; }
        }

        if (_spec.Extras.Count > 0)
        {
            if (col != 0) { col = 0; row++; }
            EnsureRow();
            var extrasHost = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var ex in _spec.Extras)
                extrasHost.Children.Add(BuildExtraPanel(ex));
            Grid.SetRow(extrasHost, row);
            Grid.SetColumn(extrasHost, 0);
            Grid.SetColumnSpan(extrasHost, FormColumns);
            FormHost.Children.Add(extrasHost);
        }

        UpdateVisibility();
        ApplyLevelHeight();
        if (_spec.Kind == "columns") ApplyColumnTypeUi();
    }

    private FrameworkElement CreateFieldControl(FieldDef f)
    {
        FrameworkElement editor;
        if (f.Kind == FieldKind.Combo)
        {
            var cb = new ComboBox
            {
                Header = f.Label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = "Select"
            };
            if (f.Key == "level") FillLevelCombo(cb, f.Default);
            else if (f.Key.Equals("wall_mark", StringComparison.OrdinalIgnoreCase))
            {
                cb.Items.Add("");
                foreach (var m in WallMarkOptions())
                    if (!string.IsNullOrWhiteSpace(m)) cb.Items.Add(m);
                string prefer = f.Default ?? "";
                cb.SelectedItem = cb.Items.Contains(prefer) ? prefer : (cb.Items.Count > 0 ? cb.Items[0] : null);
            }
            else
            {
                var opts = f.Options ?? Array.Empty<string>();
                foreach (var o in opts) cb.Items.Add(o);
                cb.SelectedItem = opts.Contains(f.Default) ? f.Default : (opts.Length > 0 ? opts[0] : null);
            }
            if (f.Key == _spec.TypeKey || f.Key == "level" || f.Key == "column_type")
                cb.SelectionChanged += (_, _) =>
                {
                    UpdateVisibility();
                    if (f.Key == "level") ApplyLevelHeight();
                    if (f.Key == "column_type" || (_spec.Kind == "columns" && f.Key == _spec.TypeKey))
                        ApplyColumnTypeUi();
                };
            editor = cb;
        }
        else if (f.Kind == FieldKind.Dia)
        {
            var cb = new ComboBox
            {
                Header = f.Label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                PlaceholderText = f.OptionalDia ? "Optional" : "Ø"
            };
            if (f.OptionalDia) cb.Items.Add("");
            foreach (var d in ProjectStore.Current.Diameters) cb.Items.Add(d.ToString());
            cb.SelectedItem = string.IsNullOrEmpty(f.Default) && f.OptionalDia ? "" : f.Default;
            editor = cb;
        }
        else if (f.Kind == FieldKind.BarList)
        {
            editor = BuildBarListField(f);
        }
        else if (IsNumericField(f))
        {
            var nb = new NumberBox
            {
                Header = f.Label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Minimum = 0,
                SmallChange = 1,
                ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten
            };
            if (double.TryParse(f.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                nb.Value = v;
            else
                nb.Value = double.NaN;
            editor = nb;
        }
        else
        {
            editor = new TextBox
            {
                Header = f.Label,
                Text = f.Default,
                PlaceholderText = f.Hint ?? "",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        if (!string.IsNullOrWhiteSpace(f.Hint) && f.Kind != FieldKind.BarList)
            ToolTipService.SetToolTip(editor, f.Hint);

        AutomationProperties.SetName(editor, f.Label);
        _editors[f.Key] = editor;
        if (f.Kind != FieldKind.BarList)
            WireDiagramFocus(editor, f.Key);
        return editor;
    }

    private FrameworkElement BuildBarListField(FieldDef f)
    {
        var list = new ObservableCollection<ExtraLine>();
        _barLists[f.Key] = list;
        foreach (var (barDia, barNos) in ParseBarGroups(f.Default))
            list.Add(new ExtraLine { Token = $"{barDia}:{barNos}", Display = $"Ø{barDia} · {barNos} nos" });

        var box = new Expander
        {
            Header = f.Label,
            IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 4)
        };
        box.GotFocus += (_, _) => HighlightField(f.Key);
        box.Expanding += (_, _) => HighlightField(f.Key);
        box.PointerPressed += (_, _) => HighlightField(f.Key);

        var stack = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(f.Hint))
            stack.Children.Add(new TextBlock
            {
                Text = f.Hint,
                Style = (Style)Application.Current.Resources["CaptionSecondaryStyle"],
                TextWrapping = TextWrapping.Wrap
            });

        var lv = new ListView
        {
            ItemsSource = list,
            MinHeight = 72,
            MaxHeight = 140,
            SelectionMode = ListViewSelectionMode.Single
        };
        ScrollViewer.SetVerticalScrollBarVisibility(lv, ScrollBarVisibility.Auto);
        lv.GotFocus += (_, _) => HighlightField(f.Key);

        var diaCb = new ComboBox { Header = "Ø", PlaceholderText = "Ø", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var d in ProjectStore.Current.Diameters) diaCb.Items.Add(d.ToString());
        var nosNb = new NumberBox
        {
            Header = "Nos",
            Minimum = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Value = double.NaN
        };
        diaCb.GotFocus += (_, _) => HighlightField(f.Key);
        nosNb.GotFocus += (_, _) => HighlightField(f.Key);

        var inputGrid = new Grid { ColumnSpacing = 8 };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(diaCb, 0); Grid.SetColumn(nosNb, 1);
        inputGrid.Children.Add(diaCb); inputGrid.Children.Add(nosNb);

        var add = new Button { Content = "Add", Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
        var rem = new Button { Content = "Remove", Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
        add.Click += (_, _) =>
        {
            var d = diaCb.SelectedItem?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(d) || double.IsNaN(nosNb.Value) || nosNb.Value < 1)
            {
                ShowError("Choose diameter and nos (≥ 1).");
                return;
            }
            var nStr = FormatNum(nosNb.Value);
            list.Add(new ExtraLine { Token = $"{d}:{nStr}", Display = $"Ø{d} · {nStr} nos" });
            nosNb.Value = double.NaN;
            ErrorBar.IsOpen = false;
            RefreshDiagram();
        };
        rem.Click += (_, _) =>
        {
            if (lv.SelectedItem is ExtraLine line) list.Remove(line);
            RefreshDiagram();
        };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        btns.Children.Add(add); btns.Children.Add(rem);
        stack.Children.Add(lv); stack.Children.Add(inputGrid); stack.Children.Add(btns);
        box.Content = stack;
        return box;
    }

    private static List<(int dia, int nos)> ParseBarGroups(string? raw)
    {
        var list = new List<(int, int)>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        foreach (Match m in Regex.Matches(raw, @"(\d+)\s*:\s*(\d+)"))
            if (int.TryParse(m.Groups[1].Value, out var d) && int.TryParse(m.Groups[2].Value, out var n) && n > 0)
                list.Add((d, n));
        return list;
    }

    private DiagramPart _diagramPart = DiagramPart.None;

    private void WireDiagramFocus(FrameworkElement editor, string fieldKey)
    {
        editor.GotFocus += (_, _) => HighlightField(fieldKey);
        editor.PointerPressed += (_, _) => HighlightField(fieldKey);
        switch (editor)
        {
            case TextBox tb:
                tb.TextChanged += (_, _) => RefreshDiagram();
                break;
            case NumberBox nb:
                nb.ValueChanged += (_, _) => RefreshDiagram();
                break;
            case ComboBox cb:
                cb.SelectionChanged += (_, _) => RefreshDiagram();
                break;
        }
    }

    private void HighlightField(string fieldKey)
    {
        _diagramPart = SectionDiagram.PartForField(_spec.Kind, fieldKey);
        RefreshDiagram();
    }

    private void HighlightExtra()
    {
        _diagramPart = DiagramPart.Extra;
        Diagram.Update(BuildDiagramSnapshot(), DiagramPart.Extra, "Additional bars");
    }

    private void RefreshDiagram()
    {
        if (!ShowsSketch) return;
        Diagram.Update(BuildDiagramSnapshot(), _diagramPart);
    }

    private DiagramSnapshot BuildDiagramSnapshot()
    {
        double Num(string key)
        {
            var s = GetEditorValue(key);
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        static double ApproxLd(double dia, string concrete, string steel)
        {
            if (dia <= 0) return 0;
            double tau = concrete switch
            {
                "M20" => 1.2, "M25" => 1.4, "M30" => 1.5, "M35" => 1.7, "M40" => 1.9, _ => 1.4
            };
            double fy = steel switch
            {
                "Fe250" => 250, "Fe415" => 415, "Fe500" => 500, "Fe550" => 550, _ => 500
            };
            var store = ProjectStore.Current;
            if (store.HysdBond && steel != "Fe250")
                tau *= store.HysdBondFactor > 0 ? store.HysdBondFactor : 1.6;
            return dia * 0.87 * fy / (4 * tau);
        }

        static double MaxDiaFromBars(string bars)
        {
            double max = 0;
            foreach (Match m in Regex.Matches(bars ?? "", @"(\d+)\s*:\s*(\d+)"))
                if (double.TryParse(m.Groups[1].Value, out var d) && d > max) max = d;
            return max;
        }

        return _spec.Kind switch
        {
            "columns" => BuildColumnSnap(Num, ApproxLd, MaxDiaFromBars),
            "slabs" => new DiagramSnapshot
            {
                L = Num("span_x"),
                Ly = Num("span_y"),
                D = Num("thickness"),
                Cover = Num("cover"),
                DiaX = GetEditorValue("dia_x"),
                DiaY = GetEditorValue("dia_y"),
                SlabType = GetEditorValue("slab_type"),
                SpacingX = Num("spacing_x"),
                SpacingY = Num("spacing_y"),
                CrankCount = (int)Num("crank_count"),
                CrankRise = Num("crank_rise")
            },
            "footings" => new DiagramSnapshot
            {
                L = Num("length_l"),
                B = Num("width_b"),
                D = Num("depth"),
                Cover = Num("cover"),
                DiaL = GetEditorValue("dia_l"),
                DiaB = GetEditorValue("dia_b"),
                FootingType = GetEditorValue("footing_type"),
                ColDimL = Num("col_dim_l"),
                ColDimB = Num("col_dim_b"),
                MeshSpacingL = Num("spacing_l"),
                MeshSpacingB = Num("spacing_b"),
                TopDiaL = GetEditorValue("top_dia_l"),
                TopDiaB = GetEditorValue("top_dia_b"),
                NRisers = (int)Num("n_steps")
            },
            "walls" => new DiagramSnapshot
            {
                L = Num("wall_length"),
                Height = Num("stem_h"),
                D = Num("stem_t"),
                Cover = Num("cover"),
                DiaX = GetEditorValue("stem_v_dia"),
                DiaY = GetEditorValue("stem_h_dia"),
                DiaL = GetEditorValue("base_l_dia"),
                DiaBaseB = GetEditorValue("base_b_dia"),
                DiaBack = GetEditorValue("stem_v_back_dia"),
                Heel = Num("heel"),
                Toe = string.Equals(GetEditorValue("include_toe"), "No", StringComparison.OrdinalIgnoreCase)
                    ? 0 : Num("toe"),
                BaseThickness = Num("base_t"),
                TensionFace = GetEditorValue("tension_face"),
                StemVSpacing = Num("stem_v_spacing"),
                StemHSpacing = Num("stem_h_spacing"),
                StemVBackSpacing = Num("stem_v_back_spacing"),
                BaseLSpacing = Num("base_l_spacing"),
                BaseBSpacing = Num("base_b_spacing"),
                LinkDia = GetEditorValue("link_dia"),
                LinkSpacing = Num("link_spacing"),
                LinkLegs = Num("link_legs") >= 4 ? 4 : 2
            },
            "stairs" => new DiagramSnapshot
            {
                B = Num("flight_width"),
                D = Num("waist_t"),
                Cover = Num("cover"),
                Going = Num("going"),
                Riser = Num("riser"),
                NRisers = (int)Num("n_risers"),
                L = Num("landing_len"),
                DiaX = GetEditorValue("main_dia"),
                DiaY = GetEditorValue("dist_dia"),
                DiaL = GetEditorValue("landing_dia")
            },
            "masonry" => new DiagramSnapshot
            {
                L = Num("length"),
                Height = Num("height"),
                B = Num("thickness"),
                D = Num("thickness")
            },
            "plaster" => new DiagramSnapshot
            {
                L = Num("length"),
                Height = Num("height"),
                D = Num("thickness")
            },
            "pcc" => new DiagramSnapshot
            {
                L = Num("length"),
                B = Num("breadth"),
                D = Num("thickness"),
                Height = Num("thickness")
            },
            "earthwork" => new DiagramSnapshot
            {
                L = Num("length"),
                B = Num("breadth"),
                D = Num("depth"),
                Height = Num("depth")
            },
            "ssm" => new DiagramSnapshot
            {
                L = Num("length"),
                B = Num("breadth"),
                Height = Num("height"),
                D = Num("height")
            },
            "doors" or "windows" => new DiagramSnapshot
            {
                L = Num("width"),
                B = Num("width"),
                Height = Num("height"),
                D = Num("width")
            },
            "flooring" or "painting" or "waterproofing" or "shuttering" or "dpc"
                or "screed" or "vdf" or "skirting" or "plinth_protection" => new DiagramSnapshot
            {
                L = Num("length") > 0 ? Num("length") : Num("width"),
                Height = Num("height") > 0 ? Num("height") : Num("breadth"),
                B = Num("breadth") > 0 ? Num("breadth") : Num("thickness"),
                D = Num("thickness") > 0 ? Num("thickness") : Num("depth")
            },
            "coping" or "parapet" => new DiagramSnapshot
            {
                L = Num("length"),
                B = Num("width") > 0 ? Num("width") : Num("breadth"),
                Height = Num("height") > 0 ? Num("height") : Num("depth"),
                D = Num("depth") > 0 ? Num("depth") : Num("thickness")
            },
            _ => BuildBeamSnap(Num, ApproxLd, MaxDiaFromBars)
        };
    }

    private DiagramSnapshot BuildColumnSnap(
        Func<string, double> Num,
        Func<double, string, string, double> ApproxLd,
        Func<string, double> MaxDiaFromBars)
    {
        var bars = GetEditorValue("bars");
        var concrete = GetEditorValue("concrete_grade");
        var steel = GetEditorValue("steel_grade");
        if (string.IsNullOrWhiteSpace(steel)) steel = "Fe500";
        double dia = MaxDiaFromBars(bars);
        if (dia <= 0) dia = 16;
        double ld = ApproxLd(dia, concrete, steel);
        double ldComp = 0.8 * ld;
        double lap = Math.Max(ldComp, 24 * dia);
        bool lapOn = string.Equals(GetEditorValue("provide_lap"), "Yes", StringComparison.OrdinalIgnoreCase);
        int.TryParse(GetEditorValue("hook_angle"), out var hookAng);
        return new DiagramSnapshot
        {
            B = Num("width"),
            D = string.Equals(GetEditorValue("column_type"), "Circular", StringComparison.OrdinalIgnoreCase)
                || string.Equals(GetEditorValue("column_type"), "Square", StringComparison.OrdinalIgnoreCase)
                ? Num("width")
                : Num("depth"),
            Height = Num("height"),
            Cover = Num("cover"),
            StirrupDia = GetEditorValue("stirrup_dia"),
            TieType = GetEditorValue("tie_type"),
            ColumnType = GetEditorValue("column_type"),
            LongBars = bars,
            SpacingSupport = Num("spacing"),
            HookAngle = hookAng > 0 ? hookAng : 135,
            ProvideLap = lapOn,
            LdMm = ld,
            LapMm = lapOn ? lap : 0
        };
    }

    private DiagramSnapshot BuildBeamSnap(
        Func<string, double> Num,
        Func<double, string, string, double> ApproxLd,
        Func<string, double> MaxDiaFromBars)
    {
        var bottom = GetEditorValue("bottom_bars");
        var concrete = GetEditorValue("concrete_grade");
        var steel = GetEditorValue("steel_grade");
        if (string.IsNullOrWhiteSpace(steel)) steel = "Fe500";
        double dia = MaxDiaFromBars(bottom);
        if (dia <= 0) dia = MaxDiaFromBars(GetEditorValue("hanger_bars"));
        if (dia <= 0) dia = 16;
        double ld = ApproxLd(dia, concrete, steel);
        double lap = Math.Max(ld, 30 * dia);
        bool lapOn = string.Equals(GetEditorValue("provide_lap"), "Tension", StringComparison.OrdinalIgnoreCase);
        int.TryParse(GetEditorValue("hook_angle"), out var hookAng);
        return new DiagramSnapshot
        {
            B = Num("width"),
            D = Num("depth"),
            L = Num("span"),
            Cover = Num("cover"),
            StirrupDia = GetEditorValue("stirrup_dia"),
            Legs = Num("legs") >= 4 ? 4 : 2,
            SpacingSupport = Num("spacing_support"),
            SpacingMiddle = Num("spacing_middle"),
            HangerBars = GetEditorValue("hanger_bars"),
            TopBars = GetEditorValue("top_bars"),
            BottomBars = bottom,
            SkinDia = GetEditorValue("skin_dia"),
            SkinNos = (int)Num("skin_nos"),
            SkinSpacing = Num("skin_spacing"),
            HookAngle = hookAng > 0 ? hookAng : 135,
            EndAnchorage = GetEditorValue("end_anchorage"),
            ProvideLap = lapOn,
            LdMm = ld,
            LapMm = lapOn ? lap : 0
        };
    }

    private static bool IsNumericField(FieldDef f) =>
        f.Kind == FieldKind.Text && !NonNumericTextKeys.Contains(f.Key);

    private UIElement BuildExtraPanel(ExtraPanelDef def)
    {
        var box = new Expander
        {
            Header = def.Title,
            IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        box.GotFocus += (_, _) => HighlightExtra();
        box.Expanding += (_, _) => HighlightExtra();
        box.PointerPressed += (_, _) => HighlightExtra();
        var stack = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(def.Hint))
            stack.Children.Add(new TextBlock
            {
                Text = def.Hint,
                Style = (Style)Application.Current.Resources["CaptionSecondaryStyle"],
                TextWrapping = TextWrapping.Wrap
            });

        var list = new ObservableCollection<ExtraLine>();
        var lv = new ListView
        {
            ItemsSource = list,
            MinHeight = 96,
            MaxHeight = 160,
            SelectionMode = ListViewSelectionMode.Single
        };
        ScrollViewer.SetVerticalScrollBarVisibility(lv, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollMode(lv, ScrollMode.Enabled);
        var dia = new ComboBox { Header = "Ø", PlaceholderText = "Ø", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var d in ProjectStore.Current.Diameters) dia.Items.Add(d.ToString());
        var a = new NumberBox
        {
            Header = def.Kind == ExtraKind.Mesh ? "Length" : "Nos",
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var b = new NumberBox
        {
            Header = def.Kind switch
            {
                ExtraKind.SpanFrac => "Frac",
                ExtraKind.Mesh => "Spacing",
                _ => "Length"
            },
            Minimum = 0,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SmallChange = def.Kind == ExtraKind.SpanFrac ? 0.05 : 1
        };
        lv.GotFocus += (_, _) => HighlightExtra();
        dia.GotFocus += (_, _) => HighlightExtra();
        a.GotFocus += (_, _) => HighlightExtra();
        b.GotFocus += (_, _) => HighlightExtra();
        var inputGrid = new Grid { ColumnSpacing = 8 };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(dia, 0); Grid.SetColumn(a, 1); Grid.SetColumn(b, 2);
        inputGrid.Children.Add(dia); inputGrid.Children.Add(a); inputGrid.Children.Add(b);

        var add = new Button { Content = "Add", Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
        var rem = new Button { Content = "Remove", Style = (Style)Application.Current.Resources["SecondaryButtonStyle"] };
        add.Click += (_, _) =>
        {
            var d = dia.SelectedItem?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(d) || double.IsNaN(a.Value) || double.IsNaN(b.Value) || a.Value <= 0 || b.Value <= 0)
            {
                ShowError("Choose diameter and both values.");
                return;
            }
            var aStr = FormatNum(a.Value);
            var bStr = FormatNum(b.Value);
            var token = $"{d}:{aStr}:{bStr}";
            var display = def.Kind switch
            {
                ExtraKind.SpanFrac => $"Ø{d} · {aStr} nos · {bStr}×span",
                ExtraKind.Mesh => $"Ø{d} · L{aStr} @ {bStr}",
                _ => $"Ø{d} · {aStr} nos · {bStr} mm"
            };
            list.Add(new ExtraLine { Token = token, Display = display });
            a.Value = double.NaN; b.Value = double.NaN;
            ErrorBar.IsOpen = false;
        };
        rem.Click += (_, _) => { if (lv.SelectedItem is ExtraLine line) list.Remove(line); };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        btns.Children.Add(add); btns.Children.Add(rem);
        stack.Children.Add(lv); stack.Children.Add(inputGrid); stack.Children.Add(btns);
        box.Content = stack;
        _extras.Add((def, list, lv));
        return box;
    }

    private void FillLevelCombo(ComboBox cb, string? preferredId)
    {
        cb.Items.Clear();
        LevelOption? preferred = null;
        foreach (var lv in ProjectStore.Current.Levels)
        {
            var opt = new LevelOption
            {
                Id = lv.Id,
                Display = string.IsNullOrWhiteSpace(lv.Name) ? lv.Id : $"{lv.Id} · {lv.Name}"
            };
            cb.Items.Add(opt);
            if (opt.Id == preferredId) preferred = opt;
        }
        if (preferred != null) cb.SelectedItem = preferred;
        else if (cb.Items.Count > 0) cb.SelectedIndex = 0;
    }

    private void RefreshLevelCombo()
    {
        if (!_editors.TryGetValue("level", out var ed) || ed is not ComboBox cb) return;
        FillLevelCombo(cb, GetEditorValue("level"));
        ApplyLevelHeight();
        UpdateVisibility();
    }

    private void ApplyLevelHeight()
    {
        if (_spec.Kind != "columns") return;
        if (!_editors.TryGetValue("height", out var hed)) return;
        var id = GetEditorValue("level");
        if (string.IsNullOrEmpty(id)) id = "Lvl0";
        var h = ProjectStore.Current.ColumnHeightFor(id);
        if (h <= 0) return;
        if (hed is NumberBox nb) nb.Value = h;
        else if (hed is TextBox tb) tb.Text = h.ToString("0", CultureInfo.InvariantCulture);
    }

    private void UpdateVisibility()
    {
        foreach (var f in _spec.Fields)
        {
            if (!_fieldHosts.TryGetValue(f.Key, out var host)) continue;
            if (string.IsNullOrEmpty(f.ShowWhenKey) || f.ShowWhenValues is null)
            {
                host.Visibility = Visibility.Visible;
                continue;
            }
            var gate = GetEditorValue(f.ShowWhenKey);
            host.Visibility = f.ShowWhenValues.Contains(gate) ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_spec.Kind == "columns")
            ApplyColumnTypeUi();
    }

    /// <summary>Filter tie options and geometry labels by Circular / Square / Rectangular.</summary>
    private void ApplyColumnTypeUi()
    {
        if (_spec.Kind != "columns") return;
        var type = GetEditorValue("column_type");
        if (string.IsNullOrEmpty(type)) type = "Rectangular";

        if (_editors.TryGetValue("width", out var wEd))
        {
            var header = type switch
            {
                "Circular" => "Diameter Ø (mm)",
                "Square" => "Side b (mm)",
                _ => "Breadth b (mm)"
            };
            if (wEd is NumberBox nb) nb.Header = header;
            else if (wEd is TextBox tb) tb.Header = header;
        }

        if (_editors.TryGetValue("depth", out var dEd) && _fieldHosts.TryGetValue("depth", out var dHost))
        {
            if (type is "Circular" or "Square")
            {
                dHost.Visibility = Visibility.Collapsed;
                // Keep depth synced for engine (square / circular Ø)
                SetEditorValue("depth", GetEditorValue("width"));
            }
            else
            {
                dHost.Visibility = Visibility.Visible;
                if (dEd is NumberBox nb) nb.Header = "Overall depth D (mm)";
                else if (dEd is TextBox tb) tb.Header = "Overall depth D (mm)";
            }
        }

        // Sync square side when width changes
        if (type == "Square" && _editors.TryGetValue("width", out var sideEd))
        {
            if (sideEd is NumberBox nbw)
            {
                nbw.ValueChanged -= SquareSide_ValueChanged;
                nbw.ValueChanged += SquareSide_ValueChanged;
            }
            else if (sideEd is TextBox tbw)
            {
                tbw.TextChanged -= SquareSide_TextChanged;
                tbw.TextChanged += SquareSide_TextChanged;
            }
        }

        if (_editors.TryGetValue("tie_type", out var tieEd) && tieEd is ComboBox tieCb)
        {
            var allowed = ColumnLayout.TiesForColumnType(type);
            var cur = tieCb.SelectedItem?.ToString() ?? "";
            tieCb.SelectionChanged -= TieType_SelectionChanged;
            tieCb.Items.Clear();
            foreach (var o in allowed) tieCb.Items.Add(o);
            tieCb.SelectedItem = allowed.Contains(cur) ? cur : allowed[0];
            tieCb.SelectionChanged += TieType_SelectionChanged;
        }

        RefreshDiagram();
    }

    private void SquareSide_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (GetEditorValue("column_type") != "Square") return;
        SetEditorValue("depth", GetEditorValue("width"));
        RefreshDiagram();
    }

    private void SquareSide_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GetEditorValue("column_type") != "Square") return;
        SetEditorValue("depth", GetEditorValue("width"));
        RefreshDiagram();
    }

    private void TieType_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshDiagram();

    private string GetEditorValue(string key)
    {
        if (_barLists.TryGetValue(key, out var barList))
            return string.Join(", ", barList.Select(x => x.Token));
        if (!_editors.TryGetValue(key, out var ed)) return "";
        return ed switch
        {
            ComboBox cb when cb.SelectedItem is LevelOption lo => lo.Id,
            ComboBox cb => cb.SelectedItem?.ToString() ?? "",
            TextBox tb => tb.Text ?? "",
            NumberBox nb when double.IsNaN(nb.Value) => "",
            NumberBox nb => FormatNum(nb.Value),
            _ => ""
        };
    }

    private void SetEditorValue(string key, string value)
    {
        if (_barLists.TryGetValue(key, out var barList))
        {
            barList.Clear();
            foreach (var (dia, nos) in ParseBarGroups(value))
                barList.Add(new ExtraLine { Token = $"{dia}:{nos}", Display = $"Ø{dia} · {nos} nos" });
            return;
        }
        if (!_editors.TryGetValue(key, out var ed)) return;
        switch (ed)
        {
            case ComboBox cb when key == "level":
                FillLevelCombo(cb, value);
                break;
            case ComboBox cb:
                if (cb.Items.Contains(value)) cb.SelectedItem = value;
                else if (string.IsNullOrEmpty(value) && cb.Items.Contains("")) cb.SelectedItem = "";
                else if (cb.Items.Count > 0) cb.SelectedIndex = 0;
                break;
            case NumberBox nb:
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    nb.Value = v;
                else
                    nb.Value = double.NaN;
                break;
            case TextBox tb:
                tb.Text = value;
                break;
        }
    }

    private Dictionary<string, string> ReadForm()
    {
        var row = new Dictionary<string, string>();
        foreach (var f in _spec.Fields)
        {
            if (f.Kind == FieldKind.Section) continue;
            if (_fieldHosts.TryGetValue(f.Key, out var host) && host.Visibility == Visibility.Collapsed)
                continue;
            row[f.Key] = GetEditorValue(f.Key);
        }
        if (!string.IsNullOrEmpty(_spec.TypeKey))
            row[_spec.TypeKey] = GetEditorValue(_spec.TypeKey);
        foreach (var (def, list, _) in _extras)
            row[def.StoreKey] = string.Join(", ", list.Select(x => x.Token));
        return row;
    }

    private void LoadForm(Dictionary<string, string> row)
    {
        foreach (var f in _spec.Fields)
        {
            if (f.Kind == FieldKind.Section) continue;
            row.TryGetValue(f.Key, out var v);
            SetEditorValue(f.Key, v ?? f.Default);
        }
        foreach (var (def, list, _) in _extras)
        {
            list.Clear();
            if (!row.TryGetValue(def.StoreKey, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            foreach (Match m in Regex.Matches(raw, @"(\d+)\s*:\s*([^:,\s]+)\s*:\s*([^,\s]+)"))
            {
                var d = m.Groups[1].Value;
                var a = m.Groups[2].Value;
                var b = m.Groups[3].Value;
                var display = def.Kind switch
                {
                    ExtraKind.SpanFrac => $"Ø{d} · {a} nos · {b}×span",
                    ExtraKind.Mesh => $"Ø{d} · L{a} @ {b}",
                    _ => $"Ø{d} · {a} nos · {b} mm"
                };
                list.Add(new ExtraLine { Token = $"{d}:{a}:{b}", Display = display });
            }
        }
        UpdateVisibility();
        // Don't overwrite height from level when loading an existing row
        RefreshDiagram();
    }

    private void ResetFormDefaults()
    {
        foreach (var f in _spec.Fields)
        {
            if (f.Kind == FieldKind.Section) continue;
            SetEditorValue(f.Key, f.Default);
        }
        foreach (var (_, list, _) in _extras) list.Clear();
        UpdateVisibility();
        ApplyLevelHeight();
        RefreshDiagram();
    }

    private static string FormatNum(double v) =>
        Math.Abs(v - Math.Round(v)) < 1e-9
            ? ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.###", CultureInfo.InvariantCulture);

    // ——— Record navigation (Access) ———

    private void NewRecord()
    {
        _editIndex = -1;
        _sheetEditRow = -1;
        ResetFormDefaults();
        UpdateRecordLabel();
        HighlightSheetRow(-1);
        ErrorBar.IsOpen = false;
    }

    private void LoadRecord(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        _editIndex = index;
        _sheetEditRow = index;
        LoadForm(_rows[index]);
        UpdateRecordLabel();
        HighlightSheetRow(index);
    }

    private void UpdateRecordLabel()
    {
        _suppressRecordCombo = true;
        RecordCombo.Items.Clear();
        RecordCombo.Items.Add(new ComboBoxItem
        {
            Content = "New record",
            Tag = -1
        });
        for (int i = 0; i < _rows.Count; i++)
        {
            string mark = _rows[i].TryGetValue("mark", out var m) && !string.IsNullOrWhiteSpace(m)
                ? m
                : $"Row {i + 1}";
            string level = _rows[i].TryGetValue("level", out var lv) ? lv : "";
            string label = string.IsNullOrWhiteSpace(level)
                ? $"{i + 1}. {mark}"
                : $"{i + 1}. {mark} · {level}";
            RecordCombo.Items.Add(new ComboBoxItem
            {
                Content = label,
                Tag = i
            });
        }

        int select = _editIndex < 0 ? 0 : Math.Min(_editIndex + 1, RecordCombo.Items.Count - 1);
        if (RecordCombo.Items.Count > 0)
            RecordCombo.SelectedIndex = select;
        _suppressRecordCombo = false;
    }

    private void RecordCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRecordCombo) return;
        if (RecordCombo.SelectedItem is not ComboBoxItem item || item.Tag is not int index) return;
        if (index < 0)
        {
            if (_editIndex >= 0) NewRecord();
            return;
        }
        if (index != _editIndex)
            LoadRecord(index);
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (_spec.IsComputedFromRcc) return;
        AddSheetRow();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        // Shuttering is auto from RCC and finishes are proposed — no manual duplicate there.
        if (_spec.IsComputedFromRcc || _spec.IsFinishReconcile) return;
        DuplicateSheetRow();
    }

    /// <summary>
    /// Clone the selected row (all common fields — geometry, bars, cover, grades…),
    /// give the copy a fresh unique mark, and insert it right below the original.
    /// Saves re-typing every field for near-identical members (retaining walls, footings, etc.).
    /// </summary>
    private void DuplicateSheetRow()
    {
        if (_editIndex < 0 || _editIndex >= _rows.Count)
        {
            ShowError("Select a row to duplicate.");
            return;
        }
        var clone = new Dictionary<string, string>(_rows[_editIndex], StringComparer.OrdinalIgnoreCase);
        // New mark so the copy doesn't collide with the source; keeps prefix (RW, C, RB…) via prototype.
        clone["mark"] = MemberSheetHelper.SuggestNextMark(_spec.Kind, _rows, clone);
        int insertAt = _editIndex + 1;
        _rows.Insert(insertAt, clone);
        _editIndex = insertAt;
        _sheetEditRow = insertAt;
        if (IsRccKind(_spec.Kind))
            ShutteringCalculator.SyncStore(ProjectStore.Current);
        _sheetEditBusy = true;
        ProjectStore.Current.Notify();
        _sheetEditBusy = false;
        RefreshSheet();
        LoadRecord(insertAt);
        FocusSheetCell(insertAt, 0);
        ShowSuccess($"Duplicated to {clone["mark"]}.");
    }

    private void AddSheetRow()
    {
        var row = BuildDefaultRow();
        _rows.Add(row);
        _editIndex = _rows.Count - 1;
        _sheetEditRow = _editIndex;
        _sheetEditBusy = true;
        ProjectStore.Current.Notify();
        _sheetEditBusy = false;
        RefreshSheet();
        LoadRecord(_editIndex);
        FocusSheetCell(_editIndex, 0);
    }

    private Dictionary<string, string> BuildDefaultRow()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _spec.Fields)
        {
            if (f.Kind == FieldKind.Section) continue;
            row[f.Key] = f.Default;
        }
        row["level"] = _sheetFloorId;
        if (_spec.Kind == "beams")
            row["beam_type"] = _sheetFloorId.Equals("Lvl0", StringComparison.OrdinalIgnoreCase) ? "PB" : "RB";
        if (!row.ContainsKey("nos")) row["nos"] = "1";
        MemberSheetHelper.StampDefaults(_spec.Kind, row);
        row["mark"] = MemberSheetHelper.SuggestNextMark(_spec.Kind, _rows, row);
        return row;
    }

    private string SuggestNextMark() =>
        MemberSheetHelper.SuggestNextMark(_spec.Kind, _rows);

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_editIndex >= 0) LoadRecord(_editIndex);
        else NewRecord();
        ShowSuccess("Edits discarded.");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_spec.IsComputedFromRcc)
        {
            if (_editIndex < 0 || _editIndex >= _rows.Count)
            {
                AppNotify.Warning("Select a row", "Pick a shuttering row, set Include, then Save include.");
                return;
            }
            string include = GetEditorValue("include");
            if (string.IsNullOrWhiteSpace(include)) include = "Yes";
            _rows[_editIndex]["include"] = include;
            ProjectStore.Current.Notify();
            RefreshSheet();
            LoadRecord(_editIndex);
            AppNotify.Success("Include updated", $"{(_rows[_editIndex].TryGetValue("mark", out var mk) ? mk : "")} · {include}");
            return;
        }
        var row = ReadForm();
        if (!row.TryGetValue("mark", out var mark) || string.IsNullOrWhiteSpace(mark))
        {
            ShowError("Mark is required.");
            return;
        }
        if (_editIndex >= 0 && _editIndex < _rows.Count)
        {
            _rows[_editIndex] = row;
            ShowSuccess($"Saved {mark} (record {_editIndex + 1}).");
        }
        else
        {
            _rows.Add(row);
            _editIndex = _rows.Count - 1;
            ShowSuccess($"Added {mark}.");
        }
        if (IsRccKind(_spec.Kind))
            ShutteringCalculator.SyncStore(ProjectStore.Current);
        ProjectStore.Current.Notify();
        RefreshSheet();
        LoadRecord(_editIndex);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_spec.IsComputedFromRcc)
        {
            AppNotify.Warning("Shuttering is automatic", "Delete the source concrete member instead.");
            return;
        }
        if (_spec.IsFinishReconcile)
        {
            AppNotify.Warning("Finishes are proposed", "Set Include to No, or Refresh after changing walls/RCC.");
            return;
        }
        if (_editIndex < 0 || _editIndex >= _rows.Count)
        {
            ShowError("Select a datasheet row to delete.");
            return;
        }
        var mark = _rows[_editIndex].TryGetValue("mark", out var m) ? m : "record";
        _rows.RemoveAt(_editIndex);
        if (_sheetEditRow == _editIndex) _sheetEditRow = -1;
        else if (_sheetEditRow > _editIndex) _sheetEditRow--;
        if (IsRccKind(_spec.Kind))
            ShutteringCalculator.SyncStore(ProjectStore.Current);
        ProjectStore.Current.Notify();
        if (_rows.Count == 0) NewRecord();
        else LoadRecord(Math.Min(_editIndex, _rows.Count - 1));
        RefreshSheet();
        ShowSuccess($"Deleted {mark}.");
    }

    private static bool IsRccKind(string kind) => kind is
        "columns" or "beams" or "slabs" or "footings" or "walls" or "stairs"
        or "pedestals" or "lintels";

    private void InitFloorCombo()
    {
        bool show = MemberSheetHelper.UsesFloorScope(_spec.Kind) && !_spec.IsComputedFromRcc;
        FloorCombo.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            ClearHeightText.Visibility = Visibility.Collapsed;
            return;
        }
        ProjectStore.Current.EnsureDefaultLevels();
        _suppressFloorCombo = true;
        FloorCombo.Items.Clear();
        foreach (var lv in ProjectStore.Current.Levels)
        {
            FloorCombo.Items.Add(new ComboBoxItem
            {
                Content = string.IsNullOrWhiteSpace(lv.Name) ? lv.Id : $"{lv.Id} · {lv.Name}",
                Tag = lv.Id
            });
        }
        _sheetFloorId = ProjectStore.Current.Levels.FirstOrDefault()?.Id ?? "Lvl0";
        for (int i = 0; i < FloorCombo.Items.Count; i++)
        {
            if (FloorCombo.Items[i] is ComboBoxItem { Tag: string id } &&
                id.Equals(_sheetFloorId, StringComparison.OrdinalIgnoreCase))
            {
                FloorCombo.SelectedIndex = i;
                break;
            }
        }
        if (FloorCombo.SelectedIndex < 0 && FloorCombo.Items.Count > 0)
            FloorCombo.SelectedIndex = 0;
        _suppressFloorCombo = false;
        UpdateClearHeightCaption();
    }

    private void FloorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFloorCombo) return;
        if (FloorCombo.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _sheetFloorId = id;
        UpdateClearHeightCaption();
        RefreshSheet();
        var visible = FilteredStoreIndices().ToList();
        if (visible.Count > 0)
            LoadRecord(visible[0]);
        else if (!_spec.IsComputedFromRcc)
            AddSheetRow();
        else
            NewRecord();
    }

    private void UpdateClearHeightCaption()
    {
        if (_spec.Kind == "columns" && FloorCombo.Visibility == Visibility.Visible)
        {
            double h = ProjectStore.Current.ColumnHeightFor(_sheetFloorId);
            ClearHeightText.Text = h > 0
                ? $"Clear height {h:0} mm (from Levels)"
                : "Clear height — set storey on Levels page";
            ClearHeightText.Visibility = Visibility.Visible;
        }
        else
            ClearHeightText.Visibility = Visibility.Collapsed;
    }

    private IEnumerable<int> FilteredStoreIndices()
    {
        if (!MemberSheetHelper.UsesFloorScope(_spec.Kind) || _spec.IsComputedFromRcc || _spec.IsFinishReconcile)
        {
            for (int i = 0; i < _rows.Count; i++) yield return i;
            yield break;
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].TryGetValue("level", out var lv);
            if (string.IsNullOrWhiteSpace(lv)) lv = "Lvl0";
            if (lv.Equals(_sheetFloorId, StringComparison.OrdinalIgnoreCase))
                yield return i;
        }
    }

    // ——— Excel-like datasheet ———

    private void BuildSheetHeader()
    {
        BuildSheetHeaderCore(
            tab: "entry",
            header: SheetHeader,
            out _sheetColumns,
            out _sheetFieldByKey,
            includeMarkAlways: false);
    }

    private void BuildDeductSheetHeader()
    {
        // Opening lines: wall (repeatable) + kind + one opening type + Nos
        _deductSheetColumns = new[] { "wall_mark", "opening_kind", "nos", "opening_l", "opening_h" };
        _deductFieldByKey = new Dictionary<string, FieldDef>(StringComparer.OrdinalIgnoreCase)
        {
            ["wall_mark"] = ElementSpecs.Combo("wall_mark", "Wall mark", WallMarkOptions(), ""),
            ["opening_kind"] = ElementSpecs.Combo("opening_kind", "Kind", new[] { "Door", "Window", "Other" }, "Other"),
            ["nos"] = ElementSpecs.Text("nos", "Nos", "1"),
            ["opening_l"] = ElementSpecs.Text("opening_l", "Width (mm)", "900"),
            ["opening_h"] = ElementSpecs.Text("opening_h", "Height (mm)", "2100"),
        };
        var header = DeductSheetHeader;
        header.Children.Clear();
        header.ColumnDefinitions.Clear();
        header.Background = SheetHeaderBg;
        double totalW = 0;
        for (int i = 0; i < _deductSheetColumns.Length; i++)
        {
            string key = _deductSheetColumns[i];
            double w = key == "wall_mark" ? 140 : SheetColMinWidth;
            totalW += w;
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w), MinWidth = w });
            string label = _deductFieldByKey.TryGetValue(key, out var fd) ? fd.Label : key;
            var cell = new Border
            {
                BorderBrush = SheetGridBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Background = SheetHeaderBg,
                Padding = new Thickness(8, 8, 8, 8),
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = SheetHeaderFg
                }
            };
            Grid.SetColumn(cell, i);
            header.Children.Add(cell);
        }
    }

    private string[] WallMarkOptions()
    {
        var marks = ProjectStore.Current.MasonryWalls
            .Select(w => w.TryGetValue("mark", out var m) ? m.Trim() : "")
            .Where(m => m.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return marks.Length > 0 ? marks : new[] { "MW1" };
    }

    private void BuildSheetHeaderCore(
        string tab,
        Grid header,
        out string[] columns,
        out Dictionary<string, FieldDef> fieldByKey,
        bool includeMarkAlways)
    {
        var keys = new List<string>();
        fieldByKey = new Dictionary<string, FieldDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _spec.Fields)
        {
            if (f.Kind == FieldKind.Section) continue;
            if (MemberSheetHelper.IsSheetHiddenKey(_spec.Kind, f.Key)) continue;
            string st = string.IsNullOrEmpty(f.SheetTab) ? "entry" : f.SheetTab;
            if (tab == "entry" && st == "deductions") continue;
            if (tab == "deductions" && st != "deductions" && !(includeMarkAlways && f.Key.Equals("mark", StringComparison.OrdinalIgnoreCase)))
                continue;
            if (keys.Contains(f.Key, StringComparer.OrdinalIgnoreCase)) continue;
            keys.Add(f.Key);
            fieldByKey[f.Key] = f;
        }
        if (tab == "deductions" && includeMarkAlways && !keys.Contains("mark", StringComparer.OrdinalIgnoreCase))
        {
            keys.Insert(0, "mark");
            var markFd = _spec.Fields.FirstOrDefault(f => f.Key.Equals("mark", StringComparison.OrdinalIgnoreCase));
            if (markFd is not null) fieldByKey["mark"] = markFd;
        }
        if (keys.Count == 0)
            keys.AddRange(_spec.InputKeys.Length > 0 ? _spec.InputKeys : new[] { "mark" });

        // Finish reconcile: only useful columns
        if (_spec.IsFinishReconcile && tab == "entry")
        {
            keys = new List<string> { "mark", "member_type", "source_mark", "include", "area_m2",
                "faces", "sides_exposed", "plaster_sides", "plaster_soffit", "plaster_ceiling", "notes" };
            fieldByKey = _spec.Fields
                .Where(f => f.Kind != FieldKind.Section)
                .GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        columns = keys.ToArray();
        header.Children.Clear();
        header.ColumnDefinitions.Clear();
        header.Background = SheetHeaderBg;
        header.Padding = new Thickness(0);
        double totalW = 0;
        for (int i = 0; i < columns.Length; i++)
        {
            double w = SheetColumnWidth(columns[i]);
            totalW += w;
            header.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(w),
                MinWidth = w
            });
            string headerText = fieldByKey.TryGetValue(columns[i], out var fd)
                ? fd.Label
                : PrettyHeader(columns[i]);
            var cell = new Border
            {
                BorderBrush = SheetGridBrush,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Background = SheetHeaderBg,
                Padding = new Thickness(8, 8, 8, 8),
                Child = new TextBlock
                {
                    Text = headerText,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = SheetHeaderFg
                }
            };
            Grid.SetColumn(cell, i);
            header.Children.Add(cell);
        }
        if (header.Parent is FrameworkElement fe && fe.Parent is FrameworkElement wide)
            wide.MinWidth = Math.Max(400, totalW);
        if (ReferenceEquals(header, SheetHeader))
            ApplySheetWideSize(totalW);
    }

    private double SheetColumnWidth(string key)
    {
        if (_sheetFieldByKey.TryGetValue(key, out var f) &&
            (f.Kind is FieldKind.Combo or FieldKind.Dia))
            return SheetComboColWidth;
        return SheetColMinWidth;
    }

    private double SheetTotalWidth()
    {
        double total = 0;
        foreach (var key in _sheetColumns)
            total += SheetColumnWidth(key);
        return Math.Max(total, SheetColMinWidth);
    }

    private void ApplySheetWideSize(double? contentWidth = null)
    {
        if (SheetWide is null || SheetHScroll is null) return;
        double w = contentWidth ?? SheetTotalWidth();
        if (SheetHScroll.ActualWidth > 0)
            w = Math.Max(w, SheetHScroll.ActualWidth);
        SheetWide.Width = w;
        if (SheetHScroll.ActualHeight > 0)
            SheetWide.Height = SheetHScroll.ActualHeight;
    }

    // Below about 700 px the section sketch gives its column back to the sheet.
    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SketchColumn.Width = e.NewSize.Width < 700 ? new GridLength(0) : new GridLength(240);
    }

    private void SheetHScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        => ApplySheetWideSize();

    private static string PrettyHeader(string key) => key switch
    {
        "mark" => "Mark",
        "level" => "Storey",
        "width" => "b",
        "depth" => "D",
        "height" => "ℓ",
        "span" => "L",
        "span_x" => "ℓx",
        "span_y" => "ℓy",
        "thickness" => "D",
        "column_type" => "Type",
        "tie_type" => "Ties",
        "bars" => "Long.φ",
        "top_bars" => "Top",
        "hanger_bars" => "Hang",
        "bottom_bars" => "Bot",
        "footing_type" => "Type",
        "slab_type" => "Type",
        "length_l" => "L",
        "width_b" => "B",
        "wall_length" => "Len",
        "stem_h" => "Hs",
        "stem_t" => "ts",
        "n_risers" => "nR",
        "going" => "Going",
        "riser" => "Riser",
        "waist_t" => "Waist",
        "flight_width" => "Width",
        "cover" => "Cover",
        "concrete_grade" => "fck",
        "steel_grade" => "fy",
        "include" => "Include",
        "member_type" => "Member",
        "area_m2" => "Area m²",
        "nos" => "Nos",
        "beam_type" => "Type",
        "opening" => "Opening",
        "bearing" => "Bearing",
        _ => key
    };

    private void RefreshSheet()
    {
        SheetRowsHost.Children.Clear();
        _sheetRowMap.Clear();
        bool readOnlyRow = _spec.IsComputedFromRcc;
        double totalW = SheetTotalWidth();
        var indices = FilteredStoreIndices().ToList();

        foreach (int r in indices)
        {
            _sheetRowMap.Add(r);
            var data = _rows[r];
            bool active = r == _sheetEditRow || r == _editIndex;
            var rowBg = active ? SheetActiveRowBg : SheetCellBg;
            var g = new Grid
            {
                Width = totalW,
                MinHeight = 34,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = rowBg,
                Tag = r
            };

            for (int c = 0; c < _sheetColumns.Length; c++)
            {
                string key = _sheetColumns[c];
                double colW = SheetColumnWidth(key);
                g.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(colW),
                    MinWidth = colW
                });
                data.TryGetValue(key, out var value);
                value ??= "";
                _sheetFieldByKey.TryGetValue(key, out var field);
                bool cellReadOnly = readOnlyRow && !key.Equals("include", StringComparison.OrdinalIgnoreCase);
                if (_spec.IsFinishReconcile)
                {
                    cellReadOnly = key is "mark" or "member_type" or "source_mark" or "area_m2" or "notes";
                }
                Brush cellBg = cellReadOnly ? SheetReadonlyBg : rowBg;

                FrameworkElement content;
                if (!cellReadOnly && field is not null && IsSheetDropdown(field))
                {
                    var cb = new ComboBox
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Margin = new Thickness(1),
                        Padding = new Thickness(4, 2, 4, 2),
                        Background = SheetCellBg,
                        Foreground = SheetCellFg,
                        BorderThickness = new Thickness(1),
                        BorderBrush = SheetGridBrush,
                        Tag = (r, key),
                        IsEnabled = true,
                        IsTabStop = true
                    };
                    _sheetComboFillBusy = true;
                    FillSheetDropdown(cb, field, value);
                    _sheetComboFillBusy = false;
                    cb.SelectionChanged += SheetCombo_SelectionChanged;
                    cb.GotFocus += SheetEditor_GotFocusActivate;
                    WireSheetKeyboard(cb);
                    content = cb;
                    AutomationProperties.SetName(cb, $"{field?.Label ?? key}, row {r + 1}");
                }
                else
                {
                    var tb = new TextBox
                    {
                        Text = value,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        Margin = new Thickness(0),
                        Padding = new Thickness(6, 4, 6, 4),
                        BorderThickness = new Thickness(0),
                        Background = cellBg,
                        Foreground = SheetCellFg,
                        IsReadOnly = cellReadOnly,
                        IsHitTestVisible = !cellReadOnly,
                        Tag = (r, key)
                    };
                    if (!cellReadOnly)
                    {
                        tb.LostFocus += SheetCell_LostFocus;
                        tb.GotFocus += SheetEditor_GotFocusActivate;
                        WireSheetKeyboard(tb);
                    }
                    content = tb;
                    AutomationProperties.SetName(tb, $"{field?.Label ?? key}, row {r + 1}");
                }

                var border = new Border
                {
                    BorderBrush = SheetGridBrush,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = cellBg,
                    Child = content
                };
                Grid.SetColumn(border, c);
                g.Children.Add(border);
            }

            g.PointerPressed += SheetRow_PointerPressed;
            SheetRowsHost.Children.Add(g);
        }

        ApplySheetWideSize(totalW);
        CountText.Text = MemberSheetHelper.UsesFloorScope(_spec.Kind) && !_spec.IsComputedFromRcc && !_spec.IsFinishReconcile
            ? $"{indices.Count} on floor · {_rows.Count} total"
            : $"{_rows.Count} row(s)";
        if (_spec.Kind == "masonry")
            RefreshDeductSheet();
        UpdateRecordLabel();
    }

    private void SheetRow_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: int storeIndex }) return;
        if (storeIndex < 0 || storeIndex >= _rows.Count) return;
        _sheetEditRow = storeIndex;
        LoadRecord(storeIndex);
    }

    private void RefreshDeductSheet()
    {
        if (DeductSheetRowsHost is null || _openingRows is null || _deductSheetColumns.Length == 0) return;
        // Refresh wall mark options
        if (_deductFieldByKey.TryGetValue("wall_mark", out var wmField))
        {
            _deductFieldByKey["wall_mark"] = ElementSpecs.Combo("wall_mark", "Wall mark", WallMarkOptions(),
                wmField.Default);
        }
        DeductSheetRowsHost.Children.Clear();
        double totalW = 0;
        foreach (var key in _deductSheetColumns)
            totalW += key == "wall_mark" ? 140 : SheetColMinWidth;
        totalW = Math.Max(totalW, SheetColMinWidth);

        for (int r = 0; r < _openingRows.Count; r++)
        {
            var data = _openingRows[r];
            // Floor filter: show openings whose wall is on current floor (or opening level matches)
            if (MemberSheetHelper.UsesFloorScope("masonry"))
            {
                string oLevel = data.TryGetValue("level", out var ol) ? ol : "";
                string wallMark = data.TryGetValue("wall_mark", out var wm) ? wm : "";
                var wall = ProjectStore.Current.MasonryWalls.FirstOrDefault(w =>
                    w.TryGetValue("mark", out var m) && m.Equals(wallMark, StringComparison.OrdinalIgnoreCase));
                if (wall is not null && wall.TryGetValue("level", out var wl) && !string.IsNullOrWhiteSpace(wl))
                    oLevel = wl;
                if (!string.IsNullOrWhiteSpace(oLevel)
                    && !oLevel.Equals(_sheetFloorId, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            bool active = r == _deductEditRow;
            var rowBg = active ? SheetActiveRowBg : SheetCellBg;
            var g = new Grid
            {
                Width = totalW,
                MinHeight = 34,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = rowBg,
                Tag = r
            };
            for (int c = 0; c < _deductSheetColumns.Length; c++)
            {
                string key = _deductSheetColumns[c];
                double colW = key == "wall_mark" ? 140 : SheetColMinWidth;
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colW), MinWidth = colW });
                data.TryGetValue(key, out var value);
                value ??= "";
                _deductFieldByKey.TryGetValue(key, out var field);
                FrameworkElement content;
                if (field is not null && IsSheetDropdown(field))
                {
                    var cb = new ComboBox
                    {
                        Margin = new Thickness(1),
                        Padding = new Thickness(4, 2, 4, 2),
                        Background = SheetCellBg,
                        Foreground = SheetCellFg,
                        BorderThickness = new Thickness(1),
                        BorderBrush = SheetGridBrush,
                        Tag = ("opening", r, key),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    _sheetComboFillBusy = true;
                    FillSheetDropdown(cb, field, value);
                    _sheetComboFillBusy = false;
                    cb.SelectionChanged += OpeningCombo_SelectionChanged;
                    content = cb;
                    AutomationProperties.SetName(cb, $"{field?.Label ?? key}, row {r + 1}");
                }
                else
                {
                    var tb = new TextBox
                    {
                        Text = value,
                        Margin = new Thickness(0),
                        Padding = new Thickness(6, 4, 6, 4),
                        BorderThickness = new Thickness(0),
                        Background = SheetCellBg,
                        Foreground = SheetCellFg,
                        Tag = ("opening", r, key),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    tb.LostFocus += OpeningText_LostFocus;
                    content = tb;
                    AutomationProperties.SetName(tb, $"{field?.Label ?? key}, row {r + 1}");
                }
                var border = new Border
                {
                    BorderBrush = SheetGridBrush,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = rowBg,
                    Child = content
                };
                Grid.SetColumn(border, c);
                g.Children.Add(border);
            }
            g.PointerPressed += OpeningRow_PointerPressed;
            DeductSheetRowsHost.Children.Add(g);
        }
    }

    private void OpeningRow_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int idx })
            _deductEditRow = idx;
    }

    private void OpeningCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sheetComboFillBusy) return;
        if (sender is not ComboBox { Tag: ValueTuple<string, int, string> tag } cb) return;
        if (tag.Item1 != "opening") return;
        CommitOpeningCell(tag.Item2, tag.Item3, cb.SelectedItem?.ToString() ?? "");
    }

    private void OpeningText_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { Tag: ValueTuple<string, int, string> tag } tb) return;
        if (tag.Item1 != "opening") return;
        CommitOpeningCell(tag.Item2, tag.Item3, tb.Text ?? "");
    }

    private void CommitOpeningCell(int rowIndex, string key, string value)
    {
        if (_openingRows is null || rowIndex < 0 || rowIndex >= _openingRows.Count) return;
        var data = _openingRows[rowIndex];
        data.TryGetValue(key, out var prev);
        if (string.Equals(prev ?? "", value, StringComparison.Ordinal)) return;
        data[key] = value;
        if (key.Equals("wall_mark", StringComparison.OrdinalIgnoreCase))
        {
            var wall = ProjectStore.Current.MasonryWalls.FirstOrDefault(w =>
                w.TryGetValue("mark", out var m) && m.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (wall is not null && wall.TryGetValue("level", out var lv))
                data["level"] = lv;
        }
        _sheetEditBusy = true;
        ProjectStore.Current.Notify();
        _sheetEditBusy = false;
    }

    private void AddOpening_Click(object sender, RoutedEventArgs e)
    {
        if (_openingRows is null) return;
        string wall = WallMarkOptions().FirstOrDefault() ?? "MW1";
        var wallRow = ProjectStore.Current.MasonryWalls.FirstOrDefault(w =>
            w.TryGetValue("mark", out var m) && m.Equals(wall, StringComparison.OrdinalIgnoreCase));
        string level = wallRow is not null && wallRow.TryGetValue("level", out var lv) ? lv : _sheetFloorId;
        _openingRows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["wall_mark"] = wall,
            ["level"] = level,
            ["opening_kind"] = "Other",
            ["nos"] = "1",
            ["opening_l"] = "900",
            ["opening_h"] = "2100"
        });
        _deductEditRow = _openingRows.Count - 1;
        ProjectStore.Current.Notify();
        RefreshDeductSheet();
    }

    private void DeleteOpening_Click(object sender, RoutedEventArgs e)
    {
        if (_openingRows is null || _deductEditRow < 0 || _deductEditRow >= _openingRows.Count)
        {
            AppNotify.Warning("Select a line", "Click an opening row, then Delete line.");
            return;
        }
        _openingRows.RemoveAt(_deductEditRow);
        _deductEditRow = Math.Min(_deductEditRow, _openingRows.Count - 1);
        ProjectStore.Current.Notify();
        RefreshDeductSheet();
    }

    private void HighlightSheetRow(int storeIndex)
    {
        if (SheetRowsHost is null) return;
        foreach (var child in SheetRowsHost.Children)
        {
            if (child is not Grid g) continue;
            int idx = g.Tag is int si ? si : -1;
            var bg = idx == storeIndex ? SheetActiveRowBg : SheetCellBg;
            g.Background = bg;
            foreach (var c in g.Children)
            {
                if (c is Border b && b.Background != SheetReadonlyBg)
                    b.Background = bg;
            }
        }
    }

    private void SheetEditor_GotFocusActivate(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ValueTuple<int, string> tag }) return;
        _sheetEditRow = tag.Item1;
        if (_editIndex != tag.Item1)
            LoadRecord(tag.Item1);
        else
            HighlightSheetRow(tag.Item1);

        if (sender is TextBox { IsReadOnly: false } tb)
            tb.SelectAll();
        EnsureSheetCellVisible(tag.Item1, ColumnIndex(tag.Item2));
    }

    private static bool IsSheetDropdown(FieldDef field) =>
        field.Kind is FieldKind.Combo or FieldKind.Dia;

    private void FillSheetDropdown(ComboBox cb, FieldDef field, string value)
    {
        cb.Items.Clear();
        if (field.Key.Equals("level", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var lv in ProjectStore.Current.Levels)
                cb.Items.Add(lv.Id);
        }
        else if (field.Key.Equals("wall_mark", StringComparison.OrdinalIgnoreCase))
        {
            cb.Items.Add("");
            foreach (var m in WallMarkOptions())
                if (!string.IsNullOrWhiteSpace(m)) cb.Items.Add(m);
        }
        else if (field.Kind == FieldKind.Dia)
        {
            if (field.OptionalDia) cb.Items.Add("");
            foreach (var d in ProjectStore.Current.Diameters)
                cb.Items.Add(d.ToString());
        }
        else
        {
            foreach (var opt in field.Options ?? Array.Empty<string>())
                cb.Items.Add(opt);
        }

        if (!string.IsNullOrEmpty(value) && cb.Items.Contains(value))
            cb.SelectedItem = value;
        else if (string.IsNullOrEmpty(value) && field.OptionalDia && cb.Items.Contains(""))
            cb.SelectedItem = "";
        else if (cb.Items.Count > 0)
            cb.SelectedIndex = 0;
    }

    private void WireSheetKeyboard(FrameworkElement editor)
    {
        editor.PreviewKeyDown -= SheetCell_PreviewKeyDown;
        editor.PreviewKeyDown += SheetCell_PreviewKeyDown;
    }

    private void SheetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sheetComboFillBusy) return;
        if (sender is not ComboBox cb || cb.Tag is not ValueTuple<int, string> tag) return;
        if (cb.SelectedItem is null) return;
        CommitSheetCell(tag.Item1, tag.Item2, cb.SelectedItem.ToString() ?? "");
    }

    private void SheetCell_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb || tb.Tag is not ValueTuple<int, string> tag) return;
        CommitSheetCell(tag.Item1, tag.Item2, tb.Text ?? "");
    }

    private static bool IsShiftDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down)
        == CoreVirtualKeyStates.Down;

    private void SheetCell_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ValueTuple<int, string> tag) return;
        if (sender is ComboBox { IsDropDownOpen: true }) return;

        // Never intercept normal typing keys
        if (e.Key is not (VirtualKey.Tab or VirtualKey.Enter or VirtualKey.Up or VirtualKey.Down
            or VirtualKey.Left or VirtualKey.Right))
            return;

        int row = tag.Item1;
        int col = ColumnIndex(tag.Item2);
        if (col < 0) return;

        int dRow = 0, dCol = 0;
        bool wrap = false;
        bool shift = IsShiftDown();

        switch (e.Key)
        {
            case VirtualKey.Tab:
                CancelPendingEnterNav();
                CommitFromEditor(fe);
                dCol = shift ? -1 : 1;
                wrap = true;
                break;
            case VirtualKey.Enter:
                CommitFromEditor(fe);
                e.Handled = true;
                HandleEnterNavigation(row, col, shift);
                return;
            case VirtualKey.Up:
                CancelPendingEnterNav();
                CommitFromEditor(fe);
                dRow = -1;
                break;
            case VirtualKey.Down:
                CancelPendingEnterNav();
                CommitFromEditor(fe);
                dRow = 1;
                break;
            case VirtualKey.Left:
                if (sender is TextBox tbLeft)
                {
                    if (tbLeft.SelectionStart > 0 || tbLeft.SelectionLength > 0) return;
                }
                CancelPendingEnterNav();
                CommitFromEditor(fe);
                dCol = -1;
                break;
            case VirtualKey.Right:
                if (sender is TextBox tbRight)
                {
                    int len = tbRight.Text?.Length ?? 0;
                    if (tbRight.SelectionStart + tbRight.SelectionLength < len) return;
                }
                CancelPendingEnterNav();
                CommitFromEditor(fe);
                dCol = 1;
                break;
            default:
                return;
        }

        e.Handled = true;
        NavigateSheet(row, col, dRow, dCol, wrap);
    }

    private void HandleEnterNavigation(int row, int col, bool shift)
    {
        if (_enterNavTimer is { IsRunning: true })
        {
            CancelPendingEnterNav();
            NavigateSheet(row, col, shift ? -1 : 1, 0, wrap: false);
            return;
        }

        _enterPendingRow = row;
        _enterPendingCol = col;
        _enterPendingShift = shift;
        if (_enterNavTimer is null)
        {
            _enterNavTimer = DispatcherQueue.CreateTimer();
            _enterNavTimer.IsRepeating = false;
            _enterNavTimer.Tick += EnterNavTimer_Tick;
        }
        _enterNavTimer.Interval = TimeSpan.FromMilliseconds(280);
        _enterNavTimer.Start();
    }

    private void EnterNavTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        CancelPendingEnterNav();
        NavigateSheet(_enterPendingRow, _enterPendingCol, 0, _enterPendingShift ? -1 : 1, wrap: true);
    }

    private void CancelPendingEnterNav()
    {
        _enterNavTimer?.Stop();
    }

    private void CommitFromEditor(FrameworkElement editor)
    {
        if (editor is TextBox tb && tb.Tag is ValueTuple<int, string> tTag)
            CommitSheetCell(tTag.Item1, tTag.Item2, tb.Text ?? "");
        else if (editor is ComboBox cb && cb.Tag is ValueTuple<int, string> cTag && cb.SelectedItem is not null)
            CommitSheetCell(cTag.Item1, cTag.Item2, cb.SelectedItem.ToString() ?? "");
    }

    private int ColumnIndex(string key)
    {
        for (int i = 0; i < _sheetColumns.Length; i++)
        {
            if (_sheetColumns[i].Equals(key, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private bool IsSheetCellEditable(int col)
    {
        if (col < 0 || col >= _sheetColumns.Length) return false;
        string key = _sheetColumns[col];
        if (_spec.IsComputedFromRcc)
            return key.Equals("include", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private void NavigateSheet(int row, int col, int dRow, int dCol, bool wrap)
    {
        int cols = _sheetColumns.Length;
        if (cols == 0 || _sheetRowMap.Count == 0) return;
        int vis = _sheetRowMap.IndexOf(row);
        if (vis < 0) vis = 0;

        if (wrap && dCol != 0 && dRow == 0)
        {
            int newVis = vis;
            int newCol = col;
            int guard = cols * _sheetRowMap.Count + 2;
            for (int i = 0; i < guard; i++)
            {
                newCol += dCol;
                if (newCol >= cols) { newCol = 0; newVis++; }
                else if (newCol < 0) { newCol = cols - 1; newVis--; }
                if (newVis < 0 || newVis >= _sheetRowMap.Count) return;
                if (IsSheetCellEditable(newCol))
                {
                    FocusSheetCell(_sheetRowMap[newVis], newCol);
                    return;
                }
            }
            return;
        }

        int targetVis = vis + dRow;
        int targetCol = col + dCol;
        if (targetVis < 0 || targetVis >= _sheetRowMap.Count) return;
        if (targetCol < 0 || targetCol >= cols) return;

        if (!IsSheetCellEditable(targetCol) && dCol != 0)
        {
            NavigateSheet(_sheetRowMap[targetVis], targetCol, 0, dCol, wrap: true);
            return;
        }

        FocusSheetCell(_sheetRowMap[targetVis], targetCol);
    }

    private void FocusSheetCell(int storeRow, int col, int attempt = 0)
    {
        if (storeRow < 0 || storeRow >= _rows.Count || SheetRowsHost is null) return;
        if (col < 0 || col >= _sheetColumns.Length) return;

        if (!IsSheetCellEditable(col))
        {
            col = FirstEditableColumn(col);
            if (col < 0) return;
        }

        int visual = _sheetRowMap.IndexOf(storeRow);
        if (visual < 0)
        {
            if (attempt < 5)
                DispatcherQueue.TryEnqueue(() => FocusSheetCell(storeRow, col, attempt + 1));
            return;
        }

        _sheetEditRow = storeRow;
        if (_editIndex != storeRow)
            LoadRecord(storeRow);
        else
            HighlightSheetRow(storeRow);

        if (visual >= SheetRowsHost.Children.Count)
        {
            if (attempt < 5)
                DispatcherQueue.TryEnqueue(() => FocusSheetCell(storeRow, col, attempt + 1));
            return;
        }

        if (SheetRowsHost.Children[visual] is not Grid g || col >= g.Children.Count)
            return;

        var editor = ResolveSheetEditor(g.Children[col]);
        if (editor is null || editor is TextBox { IsReadOnly: true })
            return;

        editor.Focus(FocusState.Programmatic);
        if (editor is TextBox tb)
            tb.SelectAll();
        EnsureSheetCellVisible(visual, col);
    }

    private int FirstEditableColumn(int preferred)
    {
        if (preferred >= 0 && preferred < _sheetColumns.Length && IsSheetCellEditable(preferred))
            return preferred;
        for (int c = 0; c < _sheetColumns.Length; c++)
        {
            if (IsSheetCellEditable(c)) return c;
        }
        return -1;
    }

    private static Control? ResolveSheetEditor(DependencyObject cell) =>
        cell switch
        {
            Control c => c,
            Border { Child: Control c } => c,
            _ => null
        };

    private void EnsureSheetCellVisible(int row, int col)
    {
        if (SheetHScroll is null || col < 0 || col >= _sheetColumns.Length) return;
        double x = 0;
        for (int i = 0; i < col; i++)
            x += SheetColumnWidth(_sheetColumns[i]);
        double w = SheetColumnWidth(_sheetColumns[col]);
        double viewLeft = SheetHScroll.HorizontalOffset;
        double viewRight = viewLeft + SheetHScroll.ViewportWidth;
        if (x < viewLeft)
            SheetHScroll.ChangeView(x, null, null, true);
        else if (x + w > viewRight)
            SheetHScroll.ChangeView(Math.Max(0, x + w - SheetHScroll.ViewportWidth), null, null, true);

        if (SheetVScroll is not null && row >= 0 && row < SheetRowsHost.Children.Count
            && SheetRowsHost.Children[row] is FrameworkElement fe)
        {
            var transform = fe.TransformToVisual(SheetRowsHost);
            var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            double top = pt.Y;
            double bottom = top + fe.ActualHeight;
            double vTop = SheetVScroll.VerticalOffset;
            double vBottom = vTop + SheetVScroll.ViewportHeight;
            if (top < vTop)
                SheetVScroll.ChangeView(null, top, null, true);
            else if (bottom > vBottom)
                SheetVScroll.ChangeView(null, Math.Max(0, bottom - SheetVScroll.ViewportHeight), null, true);
        }
    }

    private void CommitSheetCell(int rowIndex, string key, string value)
    {
        if (rowIndex < 0 || rowIndex >= _rows.Count) return;
        var data = _rows[rowIndex];
        data.TryGetValue(key, out var prev);
        if (string.Equals(prev ?? "", value, StringComparison.Ordinal)) return;
        data[key] = value;

        if (_spec.Kind == "masonry" && key.Equals("wall_build", StringComparison.OrdinalIgnoreCase))
            MasonryWallBuild.Apply(data, value);

        if (_spec.IsFinishReconcile && key is "faces" or "sides_exposed" or "plaster_sides"
            or "plaster_soffit" or "plaster_ceiling")
            FinishSurfacesCalculator.RecalcArea(data);

        _sheetEditBusy = true;
        ProjectStore.Current.Notify();
        _sheetEditBusy = false;
        if (_editIndex == rowIndex)
        {
            SetEditorValue(key, value);
            if (_spec.Kind == "masonry" && key.Equals("wall_build", StringComparison.OrdinalIgnoreCase))
            {
                SetEditorValue("unit_type", data.GetValueOrDefault("unit_type", ""));
                SetEditorValue("thickness", data.GetValueOrDefault("thickness", ""));
                SetEditorValue("block_size", data.GetValueOrDefault("block_size", ""));
            }
            if (_spec.IsFinishReconcile)
                SetEditorValue("area_m2", data.GetValueOrDefault("area_m2", ""));
            RefreshDiagram();
        }
        if (_spec.IsFinishReconcile)
            RefreshSheet();
        if (_spec.Kind == "masonry")
            RefreshDeductSheet();
    }

    private void ShowError(string message)
    {
        ErrorBar.Severity = InfoBarSeverity.Error;
        ErrorBar.Title = "Error";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void ShowSuccess(string message)
    {
        ErrorBar.Severity = InfoBarSeverity.Success;
        ErrorBar.Title = "Done";
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0 && !_spec.IsFinishReconcile)
        {
            ShowError("Add at least one row in the sheet before generating.");
            return;
        }
        try
        {
            foreach (var r in _rows)
                MemberSheetHelper.StampDefaults(_spec.Kind, r);

            IEnumerable<Dictionary<string, string>> genRows = _rows;
            string genKind = _spec.Kind;
            if (_spec.IsFinishReconcile)
            {
                genRows = ProjectStore.Current.Plaster;
                genKind = "plaster";
                if (!genRows.Any())
                {
                    ShowError("Finalize finishes first (Reconcile → Finalize), or add manual plaster rows.");
                    return;
                }
            }

            var expanded = MemberSheetHelper.ExpandForGenerate(genKind, genRows);
            string engineKind = MemberSheetHelper.EngineKind(genKind);

            GenResult res;
            if (_spec.IsCivilBoq)
                res = CivilBoqCalculator.Generate(genKind, expanded);
            else
                res = EngineClient.Generate(engineKind, ProjectStore.Current.SettingsJson(), expanded);

            if (!res.Ok)
            {
                ShowError(res.Error ?? "Generate failed.");
                return;
            }
            _last = res;
            _bbsTable.SetTable(res.Bbs.Headers, res.Bbs.Rows);
            _summaryTable.SetTable(res.Summary.Headers, res.Summary.Rows);
            _checksTable.SetTable(res.Checks.Headers, res.Checks.Rows);
            if (_spec.IsCivilBoq)
                ProjectStore.Current.LastCivilSummary = res.Summary;
            else
            {
                ProjectStore.Current.LastSummary = res.Summary;
                ProjectStore.Current.LastBbs = res.Bbs;
            }
            ProjectStore.Current.Notify();
            MainPivot.SelectedIndex = 1;
            var n = res.Bbs.Rows?.Count ?? 0;
            int members = expanded.Count;
            ShowSuccess(_spec.IsCivilBoq
                ? $"Take-off: {n} quantity line{(n == 1 ? "" : "s")}."
                : $"Generated {n} bar line{(n == 1 ? "" : "s")} from {members} member{(members == 1 ? "" : "s")} (Nos expanded).");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void ExportBbs_Click(object sender, RoutedEventArgs e)
    {
        if (_last is null) { ShowError("Generate first, then export."); return; }
        await ExportAsync(_last.Bbs.Headers, _last.Bbs.Rows, $"{_spec.Kind}_bbs");
    }

    private async void ExportSum_Click(object sender, RoutedEventArgs e)
    {
        if (_last is null) { ShowError("Generate first, then export."); return; }
        await ExportAsync(_last.Summary.Headers, _last.Summary.Rows, $"{_spec.Kind}_summary");
    }

    private async Task ExportAsync(List<string> headers, List<List<string>> rows, string name)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
        picker.SuggestedFileName = name;
        picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        var body = rows.Select(r => (IList<string>)r).ToList();
        if (!EngineClient.ExportCsv(file.Path, headers, body, out var err))
            ShowError(err ?? "Export failed");
    }
}
