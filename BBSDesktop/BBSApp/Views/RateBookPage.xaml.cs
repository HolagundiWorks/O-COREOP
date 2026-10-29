// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using BBSApp.Controls;
using BBSApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BBSApp.Views;

public sealed partial class RateBookPage : Page
{
    private readonly List<RateItem> _items = new();
    private readonly ResultTable _table = new();
    private int _selectedIndex = -1;
    private bool _loading;
    private bool _loadingItem;

    public RateBookPage()
    {
        InitializeComponent();
        _table.SetAutomationName("Rate book items");
        TableHost.Child = _table;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RateBookStore.Current.EnsureLoaded();
        RefreshVersionCombo();
    }

    private void RefreshVersionCombo()
    {
        _loading = true;
        var store = RateBookStore.Current;
        VersionCombo.Items.Clear();
        foreach (var v in store.Versions.OrderBy(x => x.Name))
            VersionCombo.Items.Add(new ComboBoxItem { Content = v.Name, Tag = v.Id });
        var active = store.ActiveOrFirst();
        if (active is not null)
        {
            foreach (ComboBoxItem item in VersionCombo.Items)
            {
                if (string.Equals(item.Tag as string, active.Id, StringComparison.OrdinalIgnoreCase))
                {
                    VersionCombo.SelectedItem = item;
                    break;
                }
            }
            LoadVersion(active);
        }
        _loading = false;
    }

    private void VersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (VersionCombo.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        var ver = RateBookStore.Current.Find(id);
        if (ver is null) return;
        RateBookStore.Current.ActiveVersionId = id;
        LoadVersion(ver);
    }

    private void LoadVersion(RateBookVersion ver)
    {
        NotesBox.Text = ver.Notes;
        _items.Clear();
        foreach (var it in ver.Items.OrderBy(i => i.Category).ThenBy(i => i.Code))
            _items.Add(Clone(it));
        RefreshTable();
        RefreshItemCombo();
        _selectedIndex = _items.Count > 0 ? 0 : -1;
        if (ItemCombo.Items.Count > 0) ItemCombo.SelectedIndex = 0;
        LoadEditor();
        Info.Title = "Rate book";
        Info.Message = $"{ver.Name} · {_items.Count} items · {RateBookStore.Current.LibraryPath}";
        Info.Severity = InfoBarSeverity.Informational;
        Info.IsOpen = true;
    }

    private void RefreshTable()
    {
        _table.SetTable(
            new[] { "Code", "DSR item no.", "Category", "Description", "Unit", "Rate" },
            _items.Select(i => (IReadOnlyList<string>)new[]
            {
                i.Code, string.IsNullOrWhiteSpace(i.DsrItemNo) ? "—" : i.DsrItemNo, i.Category, i.Description, i.Unit,
                i.Rate.ToString("0.##", CultureInfo.InvariantCulture)
            }).ToList());
    }

    private void RefreshItemCombo()
    {
        _loadingItem = true;
        ItemCombo.Items.Clear();
        for (int i = 0; i < _items.Count; i++)
            ItemCombo.Items.Add(new ComboBoxItem { Content = $"{_items[i].Code} · {_items[i].Category}", Tag = i });
        _loadingItem = false;
    }

    private void ItemCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingItem) return;
        if (ItemCombo.SelectedItem is ComboBoxItem { Tag: int idx })
        {
            _selectedIndex = idx;
            LoadEditor();
        }
    }

    private void LoadEditor()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
        {
            EditDescBox.Text = "";
            EditDsrBox.Text = "";
            EditRateBox.Value = 0;
            return;
        }
        var it = _items[_selectedIndex];
        EditDescBox.Text = it.Description;
        EditDsrBox.Text = it.DsrItemNo;
        EditRateBox.Value = it.Rate;
    }

    private void ApplyRow_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _items.Count) return;
        var it = _items[_selectedIndex];
        it.Description = EditDescBox.Text?.Trim() ?? it.Description;
        it.DsrItemNo = EditDsrBox.Text?.Trim() ?? "";
        it.Rate = double.IsNaN(EditRateBox.Value) ? 0 : EditRateBox.Value;
        RefreshTable();
        Info.Title = "Updated";
        Info.Message = $"{it.Code} = {it.Rate:0.##} (Save to write library).";
        Info.Severity = InfoBarSeverity.Informational;
        Info.IsOpen = true;
    }

    private async void NewVersion_Click(object sender, RoutedEventArgs e)
    {
        var store = RateBookStore.Current;
        store.EnsureLoaded();
        var src = SelectedVersion() ?? store.ActiveOrFirst();
        var box = new TextBox { PlaceholderText = "e.g. v2 — Mar 2026 rates", Text = "" };
        var dlg = new ContentDialog
        {
            Title = "New rate book version",
            Content = box,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        string name = string.IsNullOrWhiteSpace(box.Text)
            ? $"v{store.Versions.Count + 1}"
            : box.Text.Trim();
        var neu = store.CreateVersion(name, src?.Id, $"Cloned from {src?.Name ?? "seed"}");
        RefreshVersionCombo();
        Info.Title = "Created";
        Info.Message = $"Version “{neu.Name}” ready — edit rates and Save.";
        Info.Severity = InfoBarSeverity.Success;
        Info.IsOpen = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
        {
            _items[_selectedIndex].Description = EditDescBox.Text?.Trim() ?? _items[_selectedIndex].Description;
            _items[_selectedIndex].DsrItemNo = EditDsrBox.Text?.Trim() ?? "";
            _items[_selectedIndex].Rate = double.IsNaN(EditRateBox.Value) ? 0 : EditRateBox.Value;
        }

        var ver = SelectedVersion();
        if (ver is null) return;
        ver.Notes = NotesBox.Text?.Trim() ?? "";
        ver.Items = _items.Select(Clone).ToList();
        RateBookStore.Current.UpdateVersion(ver);
        RateBookStore.Current.ActiveVersionId = ver.Id;
        RateBookStore.Current.Save();
        RefreshTable();
        Info.Title = "Saved";
        Info.Message = $"Saved “{ver.Name}” ({ver.Items.Count} items).";
        Info.Severity = InfoBarSeverity.Success;
        Info.IsOpen = true;
    }

    private RateBookVersion? SelectedVersion()
    {
        if (VersionCombo.SelectedItem is ComboBoxItem { Tag: string id })
            return RateBookStore.Current.Find(id);
        return null;
    }

    private static RateItem Clone(RateItem i) => new()
    {
        Code = i.Code,
        Category = i.Category,
        Description = i.Description,
        Unit = i.Unit,
        Rate = i.Rate,
        DsrItemNo = i.DsrItemNo
    };
}
