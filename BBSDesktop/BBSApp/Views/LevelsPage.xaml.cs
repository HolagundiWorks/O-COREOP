// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.Globalization;
using BBSApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BBSApp.Views;

public sealed partial class LevelsPage : Page
{
    readonly ObservableCollection<LevelStoryRow> _rows = new();
    LevelStoryRow? _selected;
    bool _syncingStructure;

    public LevelsPage()
    {
        InitializeComponent();
        ProjectStore.Current.EnsureDefaultLevels();
        StoryList.ItemsSource = _rows;
        ReloadRows();
    }

    void ReloadRows()
    {
        var selectedIndex = (StoryList.SelectedItem as LevelStoryRow)?.ModelIndex ?? -1;
        _rows.Clear();
        var levels = ProjectStore.Current.Levels;
        for (var i = levels.Count - 1; i >= 0; i--)
            _rows.Add(new LevelStoryRow(levels[i], i, OnStructureChanged));
        RefreshElevations();
        if (selectedIndex >= 0)
        {
            var display = RowDisplayIndex(selectedIndex);
            if (display >= 0 && display < _rows.Count)
                StoryList.SelectedIndex = display;
        }
        else if (_rows.Count > 0 && StoryList.SelectedIndex < 0)
            StoryList.SelectedIndex = 0;
        UpdateStructurePanel();
    }

    void OnStructureChanged()
    {
        RefreshElevations();
        ProjectStore.Current.Notify();
        ProjectStore.Current.IsDirty = true;
    }

    void RefreshElevations()
    {
        foreach (var row in _rows)
            row.NotifyElevation();
    }

    void StoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = StoryList.SelectedItem as LevelStoryRow;
        UpdateStructurePanel();
        DeleteStoryBtn.IsEnabled = _selected is not null && !_selected.IsPlinth;
        InsertBelowBtn.IsEnabled = _selected is not null;
    }

    void UpdateStructurePanel()
    {
        if (_selected is null)
        {
            StructurePanel.Visibility = Visibility.Collapsed;
            return;
        }
        StructurePanel.Visibility = Visibility.Visible;
        _syncingStructure = true;
        SlabBox.Value = _selected.SlabThicknessMm;
        BeamBox.Value = _selected.BeamDepthMm;
        ClearHeightText.Text = _selected.ColumnClearMm.ToString("0", CultureInfo.InvariantCulture);
        _syncingStructure = false;
    }

    void Structure_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncingStructure || _selected is null || double.IsNaN(args.NewValue))
            return;
        if (sender == SlabBox)
            _selected.SlabThicknessMm = args.NewValue;
        else if (sender == BeamBox)
            _selected.BeamDepthMm = args.NewValue;
        ClearHeightText.Text = _selected.ColumnClearMm.ToString("0", CultureInfo.InvariantCulture);
        ProjectStore.Current.Notify();
        ProjectStore.Current.IsDirty = true;
    }

    void StoryNumber_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue))
            return;
        ProjectStore.Current.IsDirty = true;
    }

    void StoryField_LostFocus(object sender, RoutedEventArgs e) =>
        ProjectStore.Current.IsDirty = true;

    LevelStoryRow? SelectedRow() => StoryList.SelectedItem as LevelStoryRow;

    void InsertAbove_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedRow();
        var levels = ProjectStore.Current.Levels;
        var insertAt = row is null ? levels.Count : row.ModelIndex + 1;
        var template = row?.Level ?? levels[Math.Max(0, levels.Count - 1)];
        levels.Insert(insertAt, NewLevel(template, insertAt));
        ProjectStore.Current.RenumberLevels();
        FinishEdit("Storey inserted above.");
        StoryList.SelectedIndex = RowDisplayIndex(insertAt);
    }

    void InsertBelow_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedRow();
        if (row is null)
            return;
        if (row.IsPlinth)
        {
            ShowInfo("Plinth (Lvl0) is the lowest storey; use Insert Above to add floors.", InfoBarSeverity.Warning);
            return;
        }
        var insertAt = row.ModelIndex;
        ProjectStore.Current.Levels.Insert(insertAt, NewLevel(row.Level, insertAt));
        ProjectStore.Current.RenumberLevels();
        FinishEdit("Storey inserted below.");
        StoryList.SelectedIndex = RowDisplayIndex(insertAt);
    }

    static LevelDef NewLevel(LevelDef template, int insertAt) =>
        new()
        {
            Id = "Lvl" + insertAt,
            Name = insertAt == 0 ? "Plinth" : $"Level {insertAt}",
            HeightMm = template.HeightMm > 0 ? template.HeightMm : 3000,
            SlabThicknessMm = template.SlabThicknessMm,
            BeamDepthMm = template.BeamDepthMm,
            ShowMarker = true
        };

    int RowDisplayIndex(int modelIndex)
    {
        var levels = ProjectStore.Current.Levels;
        return levels.Count - 1 - modelIndex;
    }

    void DeleteStory_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedRow();
        if (row is null)
            return;
        if (row.IsPlinth)
        {
            ShowInfo("Lvl0 (plinth) cannot be deleted.", InfoBarSeverity.Warning);
            return;
        }
        if (ProjectStore.Current.Levels.Count <= 1)
        {
            ShowInfo("Keep at least one storey.", InfoBarSeverity.Warning);
            return;
        }
        ProjectStore.Current.Levels.RemoveAt(row.ModelIndex);
        ProjectStore.Current.RenumberLevels();
        FinishEdit("Storey deleted.");
    }

    void FinishEdit(string message)
    {
        ReloadRows();
        ShowInfo(message, InfoBarSeverity.Success);
    }

    void ApplyColumns_Click(object sender, RoutedEventArgs e)
    {
        int n = 0;
        foreach (var col in ProjectStore.Current.Columns)
        {
            if (!col.TryGetValue("level", out var lv) || string.IsNullOrWhiteSpace(lv)) lv = "Lvl0";
            var h = ProjectStore.Current.ColumnHeightFor(lv);
            if (h <= 0) continue;
            col["height"] = h.ToString("0", CultureInfo.InvariantCulture);
            n++;
        }
        ProjectStore.Current.Notify();
        ShowInfo($"Updated height on {n} column(s).", InfoBarSeverity.Success);
    }

    void ShowInfo(string message, InfoBarSeverity severity)
    {
        Info.Message = message;
        Info.Severity = severity;
        Info.IsOpen = true;
    }
}
