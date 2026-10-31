// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using BBSApp.Services;

namespace BBSApp.Views;

/// <summary>One row in the story grid (displayed highest elevation first).</summary>
public sealed class LevelStoryRow : INotifyPropertyChanged
{
    readonly Action _structureChanged;

    public LevelStoryRow(LevelDef level, int modelIndex, Action structureChanged)
    {
        Level = level;
        ModelIndex = modelIndex;
        _structureChanged = structureChanged;
        level.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(LevelDef.HeightMm):
                    OnPropertyChanged(nameof(HeightToNextMm));
                    OnPropertyChanged(nameof(HeightToNextText));
                    OnPropertyChanged(nameof(ElevationText));
                    _structureChanged();
                    break;
                case nameof(LevelDef.Name):
                    OnPropertyChanged(nameof(Name));
                    break;
                case nameof(LevelDef.ShowMarker):
                    OnPropertyChanged(nameof(ShowMarker));
                    break;
                case nameof(LevelDef.SlabThicknessMm):
                case nameof(LevelDef.BeamDepthMm):
                    OnPropertyChanged(nameof(ColumnClearMm));
                    break;
            }
        };
    }

    public LevelDef Level { get; }
    public int ModelIndex { get; }

    /// <summary>Storey number (0 = plinth / ground in the model list).</summary>
    public int StoryNo => ModelIndex;

    public bool IsPlinth => Level.Id == "Lvl0";

    public string Name
    {
        get => Level.Name;
        set => Level.Name = value;
    }

    public double ElevationMm
    {
        get => LevelStoryMath.ElevationAt(ProjectStore.Current.Levels, ModelIndex);
        set
        {
            if (ModelIndex == 0)
                return;
            var levels = ProjectStore.Current.Levels;
            var prevElev = LevelStoryMath.ElevationAt(levels, ModelIndex - 1);
            levels[ModelIndex - 1].HeightMm = Math.Max(0, value - prevElev);
            _structureChanged();
        }
    }

    public double HeightToNextMm
    {
        get => Level.HeightMm;
        set
        {
            Level.HeightMm = Math.Max(0, value);
            _structureChanged();
        }
    }

    public bool ShowMarker
    {
        get => Level.ShowMarker;
        set => Level.ShowMarker = value;
    }

    public double SlabThicknessMm
    {
        get => Level.SlabThicknessMm;
        set => Level.SlabThicknessMm = value;
    }

    public double BeamDepthMm
    {
        get => Level.BeamDepthMm;
        set => Level.BeamDepthMm = value;
    }

    public double ColumnClearMm => Level.ColumnHeightMm;

    public string ElevationText
    {
        get => ElevationMm.ToString("0", CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var elev))
                ElevationMm = elev;
        }
    }

    public string HeightToNextText
    {
        get => HeightToNextMm.ToString("0", CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ht))
                HeightToNextMm = ht;
        }
    }

    public void NotifyElevation()
    {
        OnPropertyChanged(nameof(ElevationMm));
        OnPropertyChanged(nameof(ElevationText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class LevelStoryMath
{
    public static double ElevationAt(IReadOnlyList<LevelDef> levels, int index)
    {
        double e = 0;
        for (var i = 0; i < index && i < levels.Count; i++)
            e += levels[i].HeightMm;
        return e;
    }
}
