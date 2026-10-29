// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace BBSApp.Controls;

/// <summary>
/// App-wide tabular results host — CommunityToolkit <see cref="DataGrid"/> with a stable SetTable API.
/// </summary>
public sealed class ResultTable : UserControl
{
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserReorderColumns = false,
        CanUserResizeColumns = true,
        CanUserSortColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.All,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Extended,
        RowHeight = Hcw.Ui.SuiteScale.PropertyRowHeight,
        ColumnHeaderHeight = Hcw.Ui.SuiteScale.PropertyRowHeight,
        FontSize = Hcw.Ui.SuiteScale.FontBody,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        BorderThickness = new Thickness(0),
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
    };

    private readonly ObservableCollection<TableRow> _rows = new();

    public ResultTable()
    {
        if (Application.Current.Resources.TryGetValue("SubtleFillColorTertiaryBrush", out var alt)
            && alt is Brush ab)
            _grid.AlternatingRowBackground = ab;
        if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var stroke)
            && stroke is Brush sb)
        {
            _grid.HorizontalGridLinesBrush = sb;
            _grid.VerticalGridLinesBrush = sb;
        }

        Content = _grid;
        _grid.ItemsSource = _rows;
        AutomationProperties.SetName(this, "Results table");
        AutomationProperties.SetName(_grid, "Results table");
    }

    public void SetAutomationName(string name)
    {
        AutomationProperties.SetName(this, name);
        AutomationProperties.SetName(_grid, name);
    }

    public void SetTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        _grid.Columns.Clear();
        _rows.Clear();

        int colCount = Math.Min(headers.Count, TableRow.MaxColumns);
        for (int i = 0; i < colCount; i++)
        {
            string header = headers[i] ?? "";
            var col = new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding { Path = new PropertyPath(TableRow.PropertyName(i)) },
                IsReadOnly = true,
                MinWidth = 72,
                CanUserSort = true,
                Width = ColumnWidthFor(header)
            };
            _grid.Columns.Add(col);
        }

        foreach (var row in rows)
            _rows.Add(TableRow.From(row, colCount));
    }

    public void Clear()
    {
        _rows.Clear();
        _grid.Columns.Clear();
    }

    private static DataGridLength ColumnWidthFor(string header)
    {
        if (header.Contains("Description", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Particular", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Notes", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Remark", StringComparison.OrdinalIgnoreCase)
            || header.Contains("of item", StringComparison.OrdinalIgnoreCase))
            return new DataGridLength(2.6, DataGridLengthUnitType.Star);

        if (header.Contains("Sl", StringComparison.OrdinalIgnoreCase)
            || header.Equals("Unit", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Qty", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Quantity", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Rate", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Amount", StringComparison.OrdinalIgnoreCase)
            || header.Contains("Nos", StringComparison.OrdinalIgnoreCase))
            return new DataGridLength(0.9, DataGridLengthUnitType.Star);

        return new DataGridLength(1.2, DataGridLengthUnitType.Star);
    }
}

/// <summary>Fixed-slot row model so DataGrid columns can bind without dynamic types.</summary>
public sealed class TableRow : INotifyPropertyChanged
{
    public const int MaxColumns = 24;

    private readonly string[] _cells = new string[MaxColumns];

    public string V0 { get => Cell(0); set => Set(0, value); }
    public string V1 { get => Cell(1); set => Set(1, value); }
    public string V2 { get => Cell(2); set => Set(2, value); }
    public string V3 { get => Cell(3); set => Set(3, value); }
    public string V4 { get => Cell(4); set => Set(4, value); }
    public string V5 { get => Cell(5); set => Set(5, value); }
    public string V6 { get => Cell(6); set => Set(6, value); }
    public string V7 { get => Cell(7); set => Set(7, value); }
    public string V8 { get => Cell(8); set => Set(8, value); }
    public string V9 { get => Cell(9); set => Set(9, value); }
    public string V10 { get => Cell(10); set => Set(10, value); }
    public string V11 { get => Cell(11); set => Set(11, value); }
    public string V12 { get => Cell(12); set => Set(12, value); }
    public string V13 { get => Cell(13); set => Set(13, value); }
    public string V14 { get => Cell(14); set => Set(14, value); }
    public string V15 { get => Cell(15); set => Set(15, value); }
    public string V16 { get => Cell(16); set => Set(16, value); }
    public string V17 { get => Cell(17); set => Set(17, value); }
    public string V18 { get => Cell(18); set => Set(18, value); }
    public string V19 { get => Cell(19); set => Set(19, value); }
    public string V20 { get => Cell(20); set => Set(20, value); }
    public string V21 { get => Cell(21); set => Set(21, value); }
    public string V22 { get => Cell(22); set => Set(22, value); }
    public string V23 { get => Cell(23); set => Set(23, value); }

    public static string PropertyName(int index) => "V" + index;

    public static TableRow From(IReadOnlyList<string> cells, int colCount)
    {
        var row = new TableRow();
        int n = Math.Min(colCount, Math.Min(cells.Count, MaxColumns));
        for (int i = 0; i < n; i++)
            row._cells[i] = cells[i] ?? "";
        return row;
    }

    private string Cell(int i) => _cells[i] ?? "";

    private void Set(int i, string? value)
    {
        value ??= "";
        if (_cells[i] == value) return;
        _cells[i] = value;
        OnPropertyChanged(PropertyName(i));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
