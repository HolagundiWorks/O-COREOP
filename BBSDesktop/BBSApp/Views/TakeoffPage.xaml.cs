// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using BBSApp.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using WinRT.Interop;

namespace BBSApp.Views;

public sealed class TakeoffListRow : INotifyPropertyChanged
{
    public TakeoffItem Item { get; }
    public TakeoffListRow(TakeoffItem item) => Item = item;
    public string DisplayTitle => Item.Mark;
    public string DisplaySubtitle
    {
        get
        {
            var profile = ElementPickProfile.Find(Item.Category);
            string mode = profile?.PickMode.ToString() ?? Item.Tool;
            if (Item.Tool.Equals("Point", StringComparison.OrdinalIgnoreCase))
                return $"{Item.Category} · {Item.Level} · point";
            if (Item.Tool is "Rectangle" or "Area")
                return $"{Item.Category} · {Item.Level} · {Item.LengthMm:0.#} mm (area)";
            return $"{Item.Category} · {Item.Level} · {Item.LengthMm:0.#} mm · {mode}";
        }
    }
    public string DisplayStatus => Item.Committed ? "Committed" : "Pending";
    public void Refresh() => OnPropertyChanged(string.Empty);
    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed partial class TakeoffPage : Page
{
    private PdfDocument? _pdf;
    private TakeoffTool _util = TakeoffTool.Draw;
    private readonly List<TakeoffPoint> _draft = new();
    private ElementPickProfile _profile = ElementPickProfile.Default;
    private string _level = "Lvl0";
    private readonly ObservableCollection<TakeoffListRow> _rows = new();
    private TakeoffItem? _selected;
    private bool _snapEnabled = true;
    private bool _orthoEnabled = true;
    private const double SnapRadiusPx = 14;
    private bool _canvasFullscreen;
    private bool _windowFullscreen;
    private AppWindowPresenterKind? _prevPresenter;
    private ToggleButton? _toolDrawBtn;
    private ToggleButton? _toolScaleBtn;
    private ToggleButton? _snapBtn;
    private ToggleButton? _orthoBtn;
    private bool _layerManagerExpanded = true;
    private bool _rightPanelVisible = true;
    private readonly List<(string Id, string Name, string[] Aliases, Windows.UI.Color Color)> _layers = new();
    private readonly Dictionary<string, ToggleButton> _layerEyeButtons = new(StringComparer.OrdinalIgnoreCase);

    public TakeoffPage()
    {
        InitializeComponent();
        ProjectStore.Current.EnsureDefaultLevels();
        foreach (var lv in ProjectStore.Current.Levels)
            LevelCombo.Items.Add(new ComboBoxItem { Content = $"{lv.Id} — {lv.Name}", Tag = lv.Id });
        if (LevelCombo.Items.Count > 0) LevelCombo.SelectedIndex = 0;

        BuildElementToolbar();
        BuildUtilToolbar();
        BuildLayerManager();
        ItemList.ItemsSource = _rows;
        CanvasHost.PointerPressedOnPage += OnCanvasPressed;
        CanvasHost.PointerMovedOnPage += OnCanvasMoved;
        CanvasHost.DoubleTapped += OnCanvasDoubleTapped;
        KeyDown += OnPageKeyDown;
        IsTabStop = true;
        Unloaded += (_, _) =>
        {
            if (_windowFullscreen) SetWindowFullscreen(false);
            if (_canvasFullscreen) ExitFullscreen();
        };
        ReloadList();
        UpdateModeStatus();
        UpdateScaleLabel();
        _ = TryReloadPdfAsync();
    }

    private TakeoffState State => ProjectStore.Current.Takeoff;
    private string Category => _profile.Category;

    private void BuildElementToolbar()
    {
        ElementToolPanel.Children.Clear();
        bool first = true;
        foreach (var p in ElementPickProfile.All)
        {
            string tip = $"{p.Label} · {p.ModeHint}";
            var btn = CreateIconToggle(
                tag: p.Category,
                tooltip: tip,
                iconUri: IconUriForCategory(p.Category),
                isOn: first);
            btn.Click += ElementTool_Click;
            ElementToolPanel.Children.Add(btn);
            if (first)
            {
                _profile = p;
                first = false;
            }
        }
    }

    private void BuildUtilToolbar()
    {
        UtilToolPanel.Children.Clear();
        _toolDrawBtn = CreateIconToggle("Draw", "Draw (element mode)", fontGlyph: "\uE7C3", isOn: true);
        _toolScaleBtn = CreateIconToggle("Scale", "Scale picker — snap known dim, enter mm", fontGlyph: "\uE8B2", isOn: false);
        var pan = CreateIconToggle("Pan", "Pan drawing", fontGlyph: "\uE7C2", isOn: false);
        var sel = CreateIconToggle("Select", "Select takeoff item", fontGlyph: "\uE8B0", isOn: false);
        var open = CreateIconToggle("Opening", "Opening deduct rectangle", fontGlyph: "\uE739", isOn: false);
        _snapBtn = CreateIconToggle("Snap", "Snap to endpoints / midpoints", fontGlyph: "\uE71B", isOn: true);
        _orthoBtn = CreateIconToggle("Ortho", "Ortho — constrain H/V from last point", fontGlyph: "\uE8AB", isOn: true);

        foreach (var b in new[] { _toolDrawBtn, _toolScaleBtn, pan, sel, open })
        {
            b.Click += UtilTool_Click;
            UtilToolPanel.Children.Add(b);
        }
        _snapBtn.Click += SnapTool_Click;
        _orthoBtn.Click += OrthoTool_Click;
        UtilToolPanel.Children.Add(_snapBtn);
        UtilToolPanel.Children.Add(_orthoBtn);
    }

    private static string? IconUriForCategory(string category) => category.ToLowerInvariant() switch
    {
        "columns" or "column" => "ms-appx:///Assets/Icons/column.svg",
        "beams" or "beam" => "ms-appx:///Assets/Icons/beam.svg",
        "pedestals" or "pedestal" => "ms-appx:///Assets/Icons/pedestal.svg",
        "lintels" or "lintel" => "ms-appx:///Assets/Icons/lintel.svg",
        "slabs" or "slab" => "ms-appx:///Assets/Icons/slab.svg",
        "footings" or "footing" => "ms-appx:///Assets/Icons/footing.svg",
        "walls" or "wall" => "ms-appx:///Assets/Icons/wall.svg",
        "stairs" or "stair" => "ms-appx:///Assets/Icons/stair.svg",
        "masonry" => "ms-appx:///Assets/Icons/masonry.svg",
        "plaster" => "ms-appx:///Assets/Icons/plaster.svg",
        "pcc" => "ms-appx:///Assets/Icons/pcc.svg",
        "earthwork" => "ms-appx:///Assets/Icons/earthwork.svg",
        "ssm" => "ms-appx:///Assets/Icons/ssm.svg",
        "shuttering" => "ms-appx:///Assets/Icons/shuttering.svg",
        "flooring" => "ms-appx:///Assets/Icons/flooring.svg",
        "painting" => "ms-appx:///Assets/Icons/painting.svg",
        "waterproofing" => "ms-appx:///Assets/Icons/waterproofing.svg",
        "dpc" => "ms-appx:///Assets/Icons/dpc.svg",
        "coping" => "ms-appx:///Assets/Icons/coping.svg",
        "screed" => "ms-appx:///Assets/Icons/screed.svg",
        "vdf" => "ms-appx:///Assets/Icons/vdf.svg",
        "skirting" => "ms-appx:///Assets/Icons/skirting.svg",
        "parapet" => "ms-appx:///Assets/Icons/parapet.svg",
        "plinth_protection" or "plinth" => "ms-appx:///Assets/Icons/plinth.svg",
        _ => null
    };

    private static string LetterForCategory(string category) => category.ToLowerInvariant() switch
    {
        "columns" or "column" => "C",
        "beams" or "beam" => "B",
        "pedestals" or "pedestal" => "P",
        "lintels" or "lintel" => "L",
        "slabs" or "slab" => "S",
        "footings" or "footing" => "F",
        "walls" or "wall" => "W",
        "stairs" or "stair" => "ST",
        "masonry" => "MW",
        "plaster" => "PL",
        "pcc" => "PC",
        "earthwork" => "EW",
        "ssm" => "SS",
        "shuttering" => "SH",
        "flooring" => "FL",
        "painting" => "PT",
        "waterproofing" => "WP",
        "dpc" => "DP",
        "coping" => "CP",
        "screed" => "SC",
        "vdf" => "VD",
        "skirting" => "SK",
        "parapet" => "PR",
        "plinth_protection" or "plinth" => "PP",
        _ => category.Length >= 2 ? category[..2].ToUpperInvariant() : category.ToUpperInvariant()
    };

    private static FrameworkElement LetterBadge(string letters, double size = 22)
    {
        string text = letters.Length > 2 ? letters[..2] : letters;
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(3),
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                FontSize = text.Length > 1 ? size * 0.42 : size * 0.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            }
        };
    }

    private ToggleButton CreateIconToggle(string tag, string tooltip, string? iconUri = null, string? fontGlyph = null, bool isOn = false)
    {
        FrameworkElement glyph;
        if (!string.IsNullOrEmpty(iconUri))
        {
            glyph = new Image
            {
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                Source = new SvgImageSource(new Uri(iconUri))
            };
        }
        else if (!string.IsNullOrEmpty(fontGlyph))
        {
            glyph = new FontIcon
            {
                Glyph = fontGlyph,
                FontSize = 16,
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"]
            };
        }
        else
        {
            string letters = LetterForCategory(tag);
            if (letters.Length > 2) letters = letters[..2];
            glyph = LetterBadge(letters);
        }

        var btn = new ToggleButton
        {
            Tag = tag,
            Width = 36,
            Height = 36,
            Padding = new Thickness(4),
            Margin = new Thickness(0),
            IsChecked = isOn,
            Content = glyph,
            CornerRadius = new CornerRadius(2)
        };
        ToolTipService.SetToolTip(btn, tooltip);
        AutomationProperties.SetName(btn, tooltip);
        return btn;
    }

    private void ElementTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn || btn.Tag is not string cat) return;
        foreach (var child in ElementToolPanel.Children)
        {
            if (child is ToggleButton t)
                t.IsChecked = ReferenceEquals(t, btn);
        }
        btn.IsChecked = true;
        _profile = ElementPickProfile.Find(cat) ?? ElementPickProfile.Default;
        _draft.Clear();
        CanvasHost.DraftPoints = _draft;
        SyncDraftMode();
        CanvasHost.RedrawDraft();
        UpdateFinishButton();
        SelectUtilTool("Draw");
        UpdateModeStatus();
    }

    private void UtilTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn || btn.Tag is not string tag) return;
        SelectUtilTool(tag);
    }

    private void SelectUtilTool(string tag)
    {
        foreach (var child in UtilToolPanel.Children)
        {
            if (child is ToggleButton t && t.Tag is string s && s is not ("Snap" or "Ortho"))
                t.IsChecked = s.Equals(tag, StringComparison.OrdinalIgnoreCase);
        }
        _util = Enum.TryParse(tag, out TakeoffTool tool) ? tool : TakeoffTool.Draw;
        _draft.Clear();
        CanvasHost.DraftPoints = _draft;
        SyncDraftMode();
        CanvasHost.RedrawDraft();
        UpdateFinishButton();
        HostScroll.HorizontalScrollMode = _util == TakeoffTool.Pan ? ScrollMode.Enabled : ScrollMode.Disabled;
        HostScroll.VerticalScrollMode = _util == TakeoffTool.Pan ? ScrollMode.Enabled : ScrollMode.Disabled;
        if (_util == TakeoffTool.Scale)
            ActivateScalePicker();
        else if (_util == TakeoffTool.Draw)
            UpdateModeStatus();
    }

    private void SnapTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton t)
            _snapEnabled = t.IsChecked == true;
    }

    private void OrthoTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton t)
            _orthoEnabled = t.IsChecked == true;
    }

    private void ReloadList()
    {
        _rows.Clear();
        foreach (var it in State.Items)
        {
            if (!IsItemVisibleInList(it)) continue;
            _rows.Add(new TakeoffListRow(it));
        }
        CanvasHost.Redraw(State.Items);
    }

    private bool IsItemVisibleInList(TakeoffItem it)
    {
        string cat = it.Category.ToLowerInvariant();
        if (cat is "column") cat = "columns";
        if (cat is "beam") cat = "beams";
        if (cat is "slab") cat = "slabs";
        if (cat is "footing") cat = "footings";
        return CanvasHost.IsCategoryVisible(cat);
    }

    private void UpdateModeStatus()
    {
        var mark = TakeoffState.NextMark(ProjectStore.Current, Category, _level);
        string mode = $"{_profile.Label} · {_profile.ModeHint}";
        ModeStatus.Text = mode;
        if (FsModeStatus is not null) FsModeStatus.Text = mode;
        AutoCodePreview.Text = $"Next: {mark}";
    }

    private static void Notify(
        string title,
        string message = "",
        InfoBarSeverity severity = InfoBarSeverity.Informational,
        int durationMs = 3800) =>
        AppNotify.Show(title, message, severity, durationMs);

    private void UpdateScaleLabel()
    {
        string text = State.MmPerPx > 0
            ? $"Scale OK · {State.MmPerPx:0.####} mm/px"
            : "Scale NOT set — use Set scale";
        ScaleLabel.Text = text;
        if (FsScaleLabel is not null) FsScaleLabel.Text = text;
        PageLabel.Text = _pdf is null ? "No PDF" : $"Page {State.Page + 1} / {_pdf.PageCount}";

        // Once scale is set, collapse zoom/page into View… overflow for more canvas space.
        bool compact = State.MmPerPx > 0;
        if (ViewExtrasPanel is not null)
            ViewExtrasPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (ViewMoreBtn is not null)
            ViewMoreBtn.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (_canvasFullscreen)
            ExitFullscreen();
        else
            EnterFullscreen(alsoWindow: false);
    }

    private void WindowFullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (_canvasFullscreen && _windowFullscreen)
            ExitFullscreen();
        else
            EnterFullscreen(alsoWindow: true);
    }

    private void EnterFullscreen(bool alsoWindow)
    {
        _canvasFullscreen = true;
        RightCol.Width = new GridLength(0);
        RightRailCol.Width = new GridLength(0);
        RightPanel.Visibility = Visibility.Collapsed;
        RightRail.Visibility = Visibility.Collapsed;
        TopBar.Visibility = Visibility.Collapsed;
        FullscreenChrome.Visibility = Visibility.Visible;
        RootGrid.Padding = new Thickness(0);
        RootGrid.ColumnSpacing = 0;
        RootGrid.RowSpacing = 0;
        CanvasBorder.BorderThickness = new Thickness(0);
        Grid.SetColumn(CanvasBorder, 0);
        Grid.SetColumnSpan(CanvasBorder, 3);
        Grid.SetColumn(ElementBar, 0);
        Grid.SetColumnSpan(ElementBar, 3);
        FullscreenBtn.Content = "Exit fullscreen";
        UpdateModeStatus();
        UpdateScaleLabel();
        Focus(FocusState.Programmatic);

        if (alsoWindow)
            SetWindowFullscreen(true);
    }

    private void ExitFullscreen()
    {
        _canvasFullscreen = false;
        RootGrid.Padding = new Thickness(16, 12, 16, 12);
        RootGrid.ColumnSpacing = 12;
        RootGrid.RowSpacing = 10;
        CanvasBorder.BorderThickness = new Thickness(1);
        Grid.SetColumn(CanvasBorder, 0);
        Grid.SetColumnSpan(CanvasBorder, 1);
        Grid.SetColumn(ElementBar, 0);
        Grid.SetColumnSpan(ElementBar, 3);
        TopBar.Visibility = Visibility.Visible;
        FullscreenChrome.Visibility = Visibility.Collapsed;
        FullscreenBtn.Content = "Fullscreen";
        // restore right panel state
        if (_rightPanelVisible)
        {
            RightCol.Width = new GridLength(280);
            RightRailCol.Width = new GridLength(0);
            RightPanel.Visibility = Visibility.Visible;
            RightRail.Visibility = Visibility.Collapsed;
        }
        else
        {
            RightCol.Width = new GridLength(0);
            RightRailCol.Width = new GridLength(36);
            RightPanel.Visibility = Visibility.Collapsed;
            RightRail.Visibility = Visibility.Visible;
        }
        if (_windowFullscreen)
            SetWindowFullscreen(false);
        UpdateModeStatus();
    }

    private void SetWindowFullscreen(bool on)
    {
        var win = App.MainWindow;
        if (win?.AppWindow is null) return;
        if (on)
        {
            _prevPresenter = win.AppWindow.Presenter.Kind;
            win.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _windowFullscreen = true;
        }
        else
        {
            win.AppWindow.SetPresenter(_prevPresenter ?? AppWindowPresenterKind.Default);
            _windowFullscreen = false;
            _prevPresenter = null;
        }
    }

    private void ScaleTool_Click(object sender, RoutedEventArgs e) =>
        SelectUtilTool("Scale");

    private void ActivateScalePicker()
    {
        _util = TakeoffTool.Scale;
        _draft.Clear();
        CanvasHost.DraftPoints = _draft;
        if (_snapBtn is not null) _snapBtn.IsChecked = true;
        _snapEnabled = true;
        SyncDraftMode();
        CanvasHost.RedrawDraft();
        ModeStatus.Text = "Scale picker · snap known dim";
        Notify("Set drawing scale",
            "Snap two points across a wall thickness, door, column, or any known size. Then enter the real dimension in mm.",
            durationMs: 5000);
    }

    private void SyncDraftMode()
    {
        if (_util == TakeoffTool.Opening)
            CanvasHost.DraftMode = "Opening";
        else if (_util == TakeoffTool.Scale)
            CanvasHost.DraftMode = "Line";
        else if (_profile.PickMode == TakeoffPickMode.Area)
            CanvasHost.DraftMode = "Area";
        else
            CanvasHost.DraftMode = "Line";
    }

    private void UpdateFinishButton()
    {
        bool show = _util == TakeoffTool.Draw
                    && _profile.PickMode == TakeoffPickMode.Area
                    && _draft.Count >= 3;
        FinishShapeBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildLayerManager()
    {
        _layers.Clear();
        _layerEyeButtons.Clear();
        LayerListPanel.Children.Clear();

        void Add(string id, string name, Windows.UI.Color color, params string[] aliases)
            => _layers.Add((id, name, aliases.Length > 0 ? aliases : new[] { id }, color));

        Add("columns", "0-RCC-Columns", Windows.UI.Color.FromArgb(255, 220, 80, 80));
        Add("beams", "0-RCC-Beams", Windows.UI.Color.FromArgb(255, 200, 120, 40));
        Add("pedestals", "0-RCC-Pedestals", Windows.UI.Color.FromArgb(255, 180, 100, 80));
        Add("lintels", "0-RCC-Lintels", Windows.UI.Color.FromArgb(255, 200, 160, 60));
        Add("slabs", "0-RCC-Slabs", Windows.UI.Color.FromArgb(255, 80, 140, 220));
        Add("footings", "0-RCC-Footings", Windows.UI.Color.FromArgb(255, 140, 90, 60));
        Add("masonry", "A-Wall-Masonry", Windows.UI.Color.FromArgb(255, 180, 60, 60));
        Add("plaster", "A-Finish-Plaster", Windows.UI.Color.FromArgb(255, 100, 160, 100));
        Add("pcc", "C-Concrete-PCC", Windows.UI.Color.FromArgb(255, 120, 120, 120));
        Add("earthwork", "C-Earthwork", Windows.UI.Color.FromArgb(255, 160, 120, 60));
        Add("ssm", "A-Wall-SSM", Windows.UI.Color.FromArgb(255, 100, 100, 140));
        Add("flooring", "A-Finish-Floor", Windows.UI.Color.FromArgb(255, 160, 80, 160));
        Add("painting", "A-Finish-Paint", Windows.UI.Color.FromArgb(255, 80, 80, 200));
        Add("waterproofing", "A-Finish-WP", Windows.UI.Color.FromArgb(255, 40, 120, 180));
        Add("dpc", "A-Wall-DPC", Windows.UI.Color.FromArgb(255, 80, 80, 80));
        Add("coping", "A-Wall-Coping", Windows.UI.Color.FromArgb(255, 140, 140, 100));
        Add("screed", "A-Finish-Screed", Windows.UI.Color.FromArgb(255, 150, 150, 150));
        Add("vdf", "A-Finish-VDF", Windows.UI.Color.FromArgb(255, 90, 90, 110));
        Add("skirting", "A-Finish-Skirting", Windows.UI.Color.FromArgb(255, 170, 100, 140));
        Add("parapet", "A-Wall-Parapet", Windows.UI.Color.FromArgb(255, 160, 70, 70));
        Add("plinth_protection", "A-Plinth-Prot", Windows.UI.Color.FromArgb(255, 130, 110, 80));
        Add("scale", "Z-Scale-Ref", Windows.UI.Color.FromArgb(255, 0, 200, 200));

        foreach (var layer in _layers)
        {
            var row = new Grid { Padding = new Thickness(4, 2, 4, 2), MinHeight = 28 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var eye = new ToggleButton
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                IsChecked = true,
                Tag = layer.Id,
                Content = new FontIcon { Glyph = "\uE7B3", FontSize = 12 },
                CornerRadius = new CornerRadius(2)
            };
            ToolTipService.SetToolTip(eye, $"Toggle {layer.Name}");
            AutomationProperties.SetName(eye, $"Show layer {layer.Name}");
            eye.Click += LayerEye_Click;
            _layerEyeButtons[layer.Id] = eye;
            Grid.SetColumn(eye, 0);
            row.Children.Add(eye);

            var swatch = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(layer.Color),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(swatch, 1);
            row.Children.Add(swatch);

            var name = new TextBlock
            {
                Text = layer.Name,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTipService.SetToolTip(name, layer.Id);
            Grid.SetColumn(name, 2);
            row.Children.Add(name);

            LayerListPanel.Children.Add(row);
        }
    }

    private void LayerEye_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton btn || btn.Tag is not string id) return;
        bool on = btn.IsChecked == true;
        btn.Content = new FontIcon { Glyph = on ? "\uE7B3" : "\uED1A", FontSize = 12 };
        ApplyLayerVisibility(id, on);
        ReloadList();
    }

    private void ApplyLayerVisibility(string id, bool on)
    {
        CanvasHost.SetCategoryVisible(id, on);
        // Keep RCC master sync for list filter when columns toggled
        if (id is "columns" or "beams" or "slabs" or "footings")
        {
            // individual RCC layers — no longer force-link all via one RCC checkbox
        }
    }

    private void LayersAllOn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var kv in _layerEyeButtons)
        {
            kv.Value.IsChecked = true;
            kv.Value.Content = new FontIcon { Glyph = "\uE7B3", FontSize = 12 };
            ApplyLayerVisibility(kv.Key, true);
        }
        ReloadList();
    }

    private void LayersAllOff_Click(object sender, RoutedEventArgs e)
    {
        foreach (var kv in _layerEyeButtons)
        {
            kv.Value.IsChecked = false;
            kv.Value.Content = new FontIcon { Glyph = "\uED1A", FontSize = 12 };
            ApplyLayerVisibility(kv.Key, false);
        }
        ReloadList();
    }

    private void LayerManagerCollapse_Click(object sender, RoutedEventArgs e)
    {
        _layerManagerExpanded = !_layerManagerExpanded;
        LayerManagerBody.Visibility = _layerManagerExpanded ? Visibility.Visible : Visibility.Collapsed;
        LayerChevron.Glyph = _layerManagerExpanded ? "\uE70D" : "\uE76C"; // chevron down / right
    }

    private void ToggleRightPanel_Click(object sender, RoutedEventArgs e)
    {
        _rightPanelVisible = !_rightPanelVisible;
        if (_rightPanelVisible)
        {
            RightCol.Width = new GridLength(280);
            RightRailCol.Width = new GridLength(0);
            RightPanel.Visibility = Visibility.Visible;
            RightRail.Visibility = Visibility.Collapsed;
        }
        else
        {
            RightCol.Width = new GridLength(0);
            RightRailCol.Width = new GridLength(36);
            RightPanel.Visibility = Visibility.Collapsed;
            RightRail.Visibility = Visibility.Visible;
        }
        // Same icon whether open or closed (matches app ribbon toggle).
        if (ToggleRightIcon is not null)
            ToggleRightIcon.Glyph = UiGlyphs.PanelToggle;
        ToolTipService.SetToolTip(ToggleRightBtn,
            _rightPanelVisible ? "Hide panels" : "Show panels");
    }

    private void Cat_Changed(object sender, RoutedEventArgs e)
    {
        // Kept for compatibility if any XAML still wires it — prefer layer manager
        if (sender is not CheckBox cb || cb.Tag is not string cat) return;
        bool on = cb.IsChecked == true;
        if (_layerEyeButtons.TryGetValue(cat, out var eye))
        {
            eye.IsChecked = on;
            eye.Content = new FontIcon { Glyph = on ? "\uE7B3" : "\uED1A", FontSize = 12 };
        }
        ApplyLayerVisibility(cat, on);
        ReloadList();
    }

    private void Level_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LevelCombo.SelectedItem is ComboBoxItem { Tag: string id })
            _level = id;
        UpdateModeStatus();
    }

    private async void ImportPdf_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow!);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add(".pdf");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        State.PdfPath = MakeProjectRelativePath(file.Path);
        State.Page = 0;
        ProjectStore.Current.Notify();
        await LoadPdfFromPathAsync(file.Path);
    }

    private static string MakeProjectRelativePath(string absolute)
    {
        var proj = ProjectStore.Current.FilePath;
        if (string.IsNullOrWhiteSpace(proj)) return absolute;
        try
        {
            var projDir = System.IO.Path.GetDirectoryName(proj);
            if (string.IsNullOrEmpty(projDir)) return absolute;
            var rel = System.IO.Path.GetRelativePath(projDir, absolute);
            return rel.StartsWith("..", StringComparison.Ordinal) ? absolute : rel;
        }
        catch { return absolute; }
    }

    private static string ResolvePdfPath(string stored)
    {
        if (System.IO.Path.IsPathRooted(stored) && System.IO.File.Exists(stored))
            return stored;
        var proj = ProjectStore.Current.FilePath;
        if (!string.IsNullOrWhiteSpace(proj))
        {
            var projDir = System.IO.Path.GetDirectoryName(proj);
            if (!string.IsNullOrEmpty(projDir))
            {
                var combined = System.IO.Path.GetFullPath(System.IO.Path.Combine(projDir, stored));
                if (System.IO.File.Exists(combined)) return combined;
            }
        }
        return stored;
    }

    private async Task TryReloadPdfAsync()
    {
        if (string.IsNullOrWhiteSpace(State.PdfPath)) return;
        var path = ResolvePdfPath(State.PdfPath);
        if (!System.IO.File.Exists(path))
        {
            Notify("PDF missing", "Re-link the drawing with Import PDF — path no longer exists.", InfoBarSeverity.Warning);
            return;
        }
        await LoadPdfFromPathAsync(path);
    }

    private async Task LoadPdfFromPathAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            _pdf = await PdfDocument.LoadFromFileAsync(file);
            await RenderPageAsync();
            UpdateScaleLabel();
            Notify("PDF loaded", "Set scale first (snap wall thickness or known dim), then pick an element and draw.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            Notify("PDF error", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async Task RenderPageAsync()
    {
        if (_pdf is null) return;
        uint page = (uint)Math.Clamp(State.Page, 0, (int)_pdf.PageCount - 1);
        State.Page = (int)page;
        using var pdfPage = _pdf.GetPage(page);
        var stream = new InMemoryRandomAccessStream();
        var opts = new PdfPageRenderOptions { DestinationWidth = (uint)(pdfPage.Size.Width * 2) };
        await pdfPage.RenderToStreamAsync(stream, opts);
        stream.Seek(0);
        var bmp = new BitmapImage();
        await bmp.SetSourceAsync(stream);
        CanvasHost.PageSource = bmp;
        CanvasHost.Redraw(State.Items);
        UpdateScaleLabel();
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pdf is null || State.Page <= 0) return;
        State.Page--;
        ProjectStore.Current.Notify();
        await RenderPageAsync();
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if (_pdf is null || State.Page + 1 >= _pdf.PageCount) return;
        State.Page++;
        ProjectStore.Current.Notify();
        await RenderPageAsync();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) =>
        HostScroll.ChangeView(null, null, Math.Min(4f, HostScroll.ZoomFactor * 1.25f));

    private void ZoomOut_Click(object sender, RoutedEventArgs e) =>
        HostScroll.ChangeView(null, null, Math.Max(0.25f, HostScroll.ZoomFactor / 1.25f));

    private async void OnCanvasPressed(Point p)
    {
        Focus(FocusState.Programmatic);
        if (_util == TakeoffTool.Pan) return;
        if (_util == TakeoffTool.Select)
        {
            SelectNearest(p);
            return;
        }

        p = ApplySnap(p);

        if (_util == TakeoffTool.Draw && _profile.PickMode == TakeoffPickMode.Point)
        {
            PlacePoint(p);
            return;
        }

        // Area polyline: click near first point closes when >= 3 verts
        if (_util == TakeoffTool.Draw && _profile.PickMode == TakeoffPickMode.Area
            && _draft.Count >= 3
            && DistPx(p, _draft[0]) <= SnapRadiusPx)
        {
            FinishAreaPolyline();
            return;
        }

        _draft.Add(new TakeoffPoint { X = p.X, Y = p.Y });
        CanvasHost.DraftPoints = _draft;
        SyncDraftMode();
        CanvasHost.RedrawDraft();
        UpdateFinishButton();

        if (_util == TakeoffTool.Scale && _draft.Count >= 2)
        {
            await FinishScalePickerAsync();
            return;
        }

        if (_util == TakeoffTool.Opening && _draft.Count >= 2)
        {
            FinishOpening();
            return;
        }

        // Line = exactly two clicks (beams / walls) — never area preview
        if (_util == TakeoffTool.Draw && _profile.PickMode == TakeoffPickMode.Line && _draft.Count >= 2)
            FinishLine();
        // Area = keep collecting until finish
    }

    private void OnCanvasDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (_util == TakeoffTool.Draw && _profile.PickMode == TakeoffPickMode.Area && _draft.Count >= 3)
        {
            // Remove accidental extra point from the second click of the double-tap if present
            if (_draft.Count >= 4)
            {
                // last click often duplicates — drop last if very close to previous
                var a = _draft[^1];
                var b = _draft[^2];
                if (DistPx(new Point(a.X, a.Y), new Point(b.X, b.Y)) < 4)
                    _draft.RemoveAt(_draft.Count - 1);
            }
            FinishAreaPolyline();
            e.Handled = true;
        }
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (ctrl && e.Key == VirtualKey.Z)
        {
            UndoTakeoff();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.F11)
        {
            if (_canvasFullscreen) ExitFullscreen();
            else EnterFullscreen(alsoWindow: false);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Escape)
        {
            if (_draft.Count > 0)
            {
                _draft.Clear();
                CanvasHost.DraftPoints = _draft;
                CanvasHost.SnapHint = null;
                CanvasHost.DraftReadout = null;
                CanvasHost.RedrawDraft();
                UpdateFinishButton();
                e.Handled = true;
                return;
            }
            if (_canvasFullscreen)
            {
                ExitFullscreen();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == VirtualKey.Enter
            && _util == TakeoffTool.Draw
            && _profile.PickMode == TakeoffPickMode.Area
            && _draft.Count >= 3)
        {
            FinishAreaPolyline();
            e.Handled = true;
        }
    }

    private void UndoTakeoff()
    {
        if (_draft.Count > 0)
        {
            _draft.RemoveAt(_draft.Count - 1);
            CanvasHost.DraftPoints = _draft;
            CanvasHost.DraftReadout = null;
            CanvasHost.RedrawDraft();
            UpdateFinishButton();
            AppNotify.Show("Undo", "Removed last vertex.");
            return;
        }

        TakeoffItem? target = _selected;
        if (target is null && ItemList.SelectedItem is TakeoffListRow row)
            target = row.Item;
        if (target is null)
            target = State.Items.LastOrDefault(i => !i.Committed && !i.Category.Equals("scale", StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            AppNotify.Show("Nothing to undo");
            return;
        }
        if (target.Committed)
        {
            AppNotify.Warning("Already committed", "Committed items cannot be undone here — delete from BOQ sheets if needed.");
            return;
        }

        State.Items.Remove(target);
        _selected = null;
        CanvasHost.SelectedItem = null;
        ProjectStore.Current.Notify();
        ReloadList();
        CanvasHost.Redraw(State.Items);
        AppNotify.Show("Undo", $"Removed {target.Mark}.");
    }

    private void FinishShape_Click(object sender, RoutedEventArgs e)
    {
        if (_draft.Count >= 3) FinishAreaPolyline();
    }

    private void OnCanvasMoved(Point p)
    {
        p = ApplySnap(p, previewOnly: true);

        bool rubberLine = _draft.Count >= 1 && (
            _util is TakeoffTool.Scale or TakeoffTool.Opening
            || (_util == TakeoffTool.Draw && _profile.PickMode == TakeoffPickMode.Line));

        bool rubberPoly = _draft.Count >= 1
                          && _util == TakeoffTool.Draw
                          && _profile.PickMode == TakeoffPickMode.Area;

        if (!rubberLine && !rubberPoly)
        {
            CanvasHost.DraftReadout = null;
            CanvasHost.RedrawDraft();
            return;
        }

        var preview = new List<TakeoffPoint>(_draft) { new() { X = p.X, Y = p.Y } };
        CanvasHost.DraftPoints = preview;
        SyncDraftMode();
        UpdateDraftReadout(preview);
        CanvasHost.RedrawDraft();
    }

    private void UpdateDraftReadout(IList<TakeoffPoint> pts)
    {
        CanvasHost.DraftReadout = null;
        CanvasHost.DraftReadoutAt = null;
        if (pts.Count < 2 || State.MmPerPx <= 0) return;
        var a = pts[^2];
        var b = pts[^1];
        double px = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        double mm = px * State.MmPerPx;
        CanvasHost.DraftReadout = $"{mm:0.#} mm";
        CanvasHost.DraftReadoutAt = new TakeoffPoint { X = (a.X + b.X) / 2, Y = (a.Y + b.Y) / 2 };
    }

    private Point ApplySnap(Point p, bool previewOnly = false)
    {
        CanvasHost.SnapHint = null;
        if (!_snapEnabled && !_orthoEnabled) return p;

        double best = SnapRadiusPx;
        TakeoffPoint? hit = null;

        void Consider(double x, double y)
        {
            double d = Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
            if (d < best) { best = d; hit = new TakeoffPoint { X = x, Y = y }; }
        }

        if (_snapEnabled)
        {
            foreach (var it in State.Items)
            {
                foreach (var pt in it.Points)
                    Consider(pt.X, pt.Y);
                // Midpoints of segments
                for (int i = 0; i + 1 < it.Points.Count; i++)
                {
                    var a = it.Points[i];
                    var b = it.Points[i + 1];
                    Consider((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                }
            }
            foreach (var pt in _draft)
                Consider(pt.X, pt.Y);
            for (int i = 0; i + 1 < _draft.Count; i++)
            {
                var a = _draft[i];
                var b = _draft[i + 1];
                Consider((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            }
        }

        // Ortho from last draft point
        if (_orthoEnabled && _draft.Count > 0)
        {
            var last = _draft[^1];
            double dx = Math.Abs(p.X - last.X);
            double dy = Math.Abs(p.Y - last.Y);
            if (dx < SnapRadiusPx * 1.5 && dy >= SnapRadiusPx)
                Consider(last.X, p.Y);
            if (dy < SnapRadiusPx * 1.5 && dx >= SnapRadiusPx)
                Consider(p.X, last.Y);
            // Soft ortho: prefer H/V when clearly closer to an axis
            if (hit is null && (dx < dy * 0.15 || dy < dx * 0.15))
            {
                if (dx < dy) return new Point(last.X, p.Y);
                return new Point(p.X, last.Y);
            }
        }

        if (hit is null) return p;
        CanvasHost.SnapHint = hit;
        _ = previewOnly;
        return new Point(hit.X, hit.Y);
    }

    private static double DistPx(Point a, TakeoffPoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double DistPx(Point a, Point b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private async Task FinishScalePickerAsync()
    {
        double px = Dist(_draft[0], _draft[1]);
        var p0 = _draft[0];
        var p1 = _draft[1];
        _draft.Clear();
        CanvasHost.DraftPoints = _draft;
        CanvasHost.SnapHint = null;
        CanvasHost.RedrawDraft();
        if (px < 1)
        {
            Notify("Too short", "Pick two distinct points across the known dimension.", InfoBarSeverity.Warning);
            return;
        }

        var preset = new ComboBox
        {
            Header = "Common sizes",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = 2
        };
        var presets = new (string label, double mm)[]
        {
            ("Wall 110 mm", 110),
            ("Wall 150 mm", 150),
            ("Wall 230 mm", 230),
            ("Wall 300 mm", 300),
            ("Column 300 mm", 300),
            ("Column 450 mm", 450),
            ("Beam depth 450 mm", 450),
            ("Door 900 mm", 900),
            ("Door 1000 mm", 1000),
            ("Window 1200 mm", 1200),
            ("Grid / custom…", 0),
        };
        foreach (var (label, _) in presets)
            preset.Items.Add(label);

        var box = new NumberBox
        {
            Header = "Actual size (mm)",
            Value = 230,
            Minimum = 1,
            Maximum = 100000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        preset.SelectionChanged += (_, _) =>
        {
            int i = preset.SelectedIndex;
            if (i >= 0 && i < presets.Length && presets[i].mm > 0)
                box.Value = presets[i].mm;
        };

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"Measured {px:0.#} px on drawing. Enter the real-world size of that span (wall thickness, door clear width, column face, etc.). Scale applies to the whole PDF."
        });
        body.Children.Add(preset);
        body.Children.Add(box);

        var dlg = new ContentDialog
        {
            Title = "Set drawing scale",
            Content = body,
            PrimaryButtonText = "Apply scale",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        double mm = double.IsNaN(box.Value) ? 0 : box.Value;
        if (mm <= 0)
        {
            Notify("Enter size in mm", severity: InfoBarSeverity.Warning);
            return;
        }

        State.MmPerPx = mm / px;
        // Keep a faint scale reference line on the takeoff (optional visual)
        var refItem = new TakeoffItem
        {
            Category = "scale",
            Level = _level,
            Mark = $"SCALE-{mm:0}mm",
            Tool = "Line",
            LengthMm = mm,
            MappedField = "length",
            Committed = true
        };
        refItem.Points.Add(new TakeoffPoint { X = p0.X, Y = p0.Y });
        refItem.Points.Add(new TakeoffPoint { X = p1.X, Y = p1.Y });
        refItem.Fields["known_mm"] = mm.ToString(CultureInfo.InvariantCulture);
        // Replace previous scale refs
        for (int i = State.Items.Count - 1; i >= 0; i--)
        {
            if (State.Items[i].Category.Equals("scale", StringComparison.OrdinalIgnoreCase))
                State.Items.RemoveAt(i);
        }
        State.Items.Add(refItem);

        ProjectStore.Current.Notify();
        UpdateScaleLabel();
        ReloadList();
        Notify("Scale set for whole drawing",
            $"{mm:0.###} mm over {px:0.#} px → {State.MmPerPx:0.####} mm/px. All measures use this scale.",
            InfoBarSeverity.Success);
        SelectUtilTool("Draw");
    }

    private bool RequireScale()
    {
        if (State.MmPerPx > 0) return true;
        Notify("Set scale first", "Use Set scale / Scale picker: snap across a wall thickness (or any known dim) and enter mm.", InfoBarSeverity.Warning);
        _draft.Clear();
        CanvasHost.DraftPoints = _draft;
        CanvasHost.RedrawDraft();
        SelectUtilTool("Scale");
        return false;
    }

    private void PlacePoint(Point p)
    {
        // Point placement does not require scale (mark only); height from level defaults.
        var item = new TakeoffItem
        {
            Category = Category,
            Level = _level,
            Mark = TakeoffState.NextMark(ProjectStore.Current, Category, _level),
            Tool = "Point",
            LengthMm = 0,
            MappedField = _profile.PrimaryField,
            Committed = false
        };
        item.Points.Add(new TakeoffPoint { X = p.X, Y = p.Y });
        State.Items.Add(item);
        ProjectStore.Current.Notify();
        ReloadList();
        UpdateModeStatus();
        SelectItem(item);
    }

    private void FinishLine()
    {
        if (!RequireScale()) return;
        double len = Dist(_draft[0], _draft[1]) * State.MmPerPx;
        var item = new TakeoffItem
        {
            Category = Category,
            Level = _level,
            Mark = TakeoffState.NextMark(ProjectStore.Current, Category, _level),
            Tool = "Line",
            LengthMm = Math.Round(len, 1),
            MappedField = _profile.PrimaryField,
            Committed = false
        };
        item.Points.AddRange(_draft);
        item.Fields[_profile.PrimaryField] = item.LengthMm.ToString("0.###", CultureInfo.InvariantCulture);
        State.Items.Add(item);
        _draft.Clear();
        CanvasHost.DraftPoints = _draft;
        CanvasHost.SnapHint = null;
        ProjectStore.Current.Notify();
        ReloadList();
        UpdateModeStatus();
        UpdateFinishButton();
        SelectItem(item);
    }

    private void FinishAreaPolyline()
    {
        if (!RequireScale()) return;
        if (_draft.Count < 3)
        {
            Notify("Need 3+ points", "Add vertices, then Finish / double-click / click near start.", InfoBarSeverity.Warning);
            return;
        }

        double minX = _draft.Min(p => p.X), maxX = _draft.Max(p => p.X);
        double minY = _draft.Min(p => p.Y), maxY = _draft.Max(p => p.Y);
        double w = (maxX - minX) * State.MmPerPx;
        double h = (maxY - minY) * State.MmPerPx;
        double areaM2 = PolygonAreaMm2(_draft) / 1e6;

        var item = new TakeoffItem
        {
            Category = Category,
            Level = _level,
            Mark = TakeoffState.NextMark(ProjectStore.Current, Category, _level),
            Tool = "Area",
            LengthMm = Math.Round(w, 1),
            MappedField = _profile.PrimaryField,
            Committed = false
        };
        item.Points.AddRange(_draft.Select(p => new TakeoffPoint { X = p.X, Y = p.Y }));
        item.Fields[_profile.PrimaryField] = Math.Round(w, 1).ToString(CultureInfo.InvariantCulture);
        item.Fields[_profile.SecondaryField] = Math.Round(h, 1).ToString(CultureInfo.InvariantCulture);
        item.Fields["breadth_mm"] = item.Fields[_profile.SecondaryField];
        item.Fields["area_m2"] = Math.Round(areaM2, 3).ToString(CultureInfo.InvariantCulture);

        State.Items.Add(item);
        _draft.Clear();
        CanvasHost.DraftPoints = _draft;
        CanvasHost.SnapHint = null;
        ProjectStore.Current.Notify();
        ReloadList();
        UpdateModeStatus();
        UpdateFinishButton();
        SelectItem(item);
    }

    /// <summary>Shoelace formula — absolute area in mm² (points in px × mmPerPx).</summary>
    private double PolygonAreaMm2(IReadOnlyList<TakeoffPoint> pts)
    {
        if (pts.Count < 3 || State.MmPerPx <= 0) return 0;
        double sum = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        double areaPx2 = Math.Abs(sum) * 0.5;
        return areaPx2 * State.MmPerPx * State.MmPerPx;
    }

    private void FinishOpening()
    {
        if (!RequireScale()) return;
        var a = _draft[0];
        var b = _draft[1];
        double w = Math.Abs(b.X - a.X) * State.MmPerPx;
        double h = Math.Abs(b.Y - a.Y) * State.MmPerPx;
        var item = new TakeoffItem
        {
            Category = Category,
            Level = _level,
            Mark = TakeoffState.NextMark(ProjectStore.Current, Category, _level) + "-OP",
            Tool = "Opening",
            LengthMm = Math.Round(Math.Max(w, h), 1),
            MappedField = "opening",
            Committed = false
        };
        item.Points.Add(a);
        item.Points.Add(b);
        item.Fields["opening_l"] = Math.Round(w, 1).ToString(CultureInfo.InvariantCulture);
        item.Fields["opening_h"] = Math.Round(h, 1).ToString(CultureInfo.InvariantCulture);
        item.Fields["opening_nos"] = "1";
        State.Items.Add(item);
        _draft.Clear();
        CanvasHost.DraftPoints = _draft;
        ProjectStore.Current.Notify();
        ReloadList();
        UpdateModeStatus();
        SelectItem(item);
        SelectUtilTool("Draw");
        Notify("Opening drawn", "Commit to create Door/Window schedule + masonry wall deduct (nearest wall suggested).", InfoBarSeverity.Informational);
    }

    private void SelectNearest(Point p)
    {
        TakeoffItem? best = null;
        double bestD = 28;
        foreach (var it in State.Items)
        {
            if (!IsItemVisibleInList(it)) continue;
            foreach (var pt in it.Points)
            {
                double d = Math.Sqrt((pt.X - p.X) * (pt.X - p.X) + (pt.Y - p.Y) * (pt.Y - p.Y));
                if (d < bestD) { bestD = d; best = it; }
            }
        }
        if (best is not null) SelectItem(best);
    }

    private void SelectItem(TakeoffItem item)
    {
        _selected = item;
        CanvasHost.SelectedItem = item;
        CanvasHost.Redraw(State.Items);
        var row = _rows.FirstOrDefault(r => r.Item.Id == item.Id);
        if (row is not null) ItemList.SelectedItem = row;
    }

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemList.SelectedItem is TakeoffListRow row)
            SelectItem(row.Item);
    }

    private async void Commit_Click(object sender, RoutedEventArgs e)
    {
        var item = _selected ?? (ItemList.SelectedItem as TakeoffListRow)?.Item;
        if (item is null)
        {
            Notify("Select an item", severity: InfoBarSeverity.Warning);
            return;
        }
        if (item.Category.Equals("scale", StringComparison.OrdinalIgnoreCase))
        {
            Notify("Scale reference", "This line only records the known dimension used to set drawing scale.");
            return;
        }
        if (item.Committed)
        {
            Notify("Already committed");
            return;
        }

        if (item.Tool.Equals("Opening", StringComparison.OrdinalIgnoreCase))
        {
            await CommitOpeningAsync(item);
            return;
        }

        var coll = TakeoffState.CollectionFor(ProjectStore.Current, item.Category);
        if (coll is null)
        {
            Notify("Unknown category", severity: InfoBarSeverity.Error);
            return;
        }

        double breadth = 0;
        if (item.Fields.TryGetValue("breadth_mm", out var bm)
            && double.TryParse(bm, NumberStyles.Float, CultureInfo.InvariantCulture, out var b1))
            breadth = b1;
        else
        {
            var profile = ElementPickProfile.Find(item.Category);
            if (profile is not null
                && item.Fields.TryGetValue(profile.SecondaryField, out var sf)
                && double.TryParse(sf, NumberStyles.Float, CultureInfo.InvariantCulture, out var b2))
                breadth = b2;
        }

        var row = TakeoffState.DefaultRow(
            item.Category, item.Mark, item.Level,
            item.LengthMm, breadth, item.MappedField, item.Fields);
        coll.Add(row);
        item.Committed = true;
        ProjectStore.Current.Notify();
        foreach (var r in _rows) r.Refresh();
        CanvasHost.Redraw(State.Items);
        Notify("Committed", $"{item.Mark} → {item.Category} BOQ.", InfoBarSeverity.Success);
        UpdateModeStatus();
    }

    private async System.Threading.Tasks.Task CommitOpeningAsync(TakeoffItem item)
    {
        var store = ProjectStore.Current;
        var suggestKind = OpeningScheduleLinker.SuggestKind(item);
        var suggestWall = OpeningScheduleLinker.SuggestWallMark(store, item);
        var walls = OpeningScheduleLinker.WallMarkChoices(store, item.Level).ToList();
        if (walls.Count == 0) walls.Add(suggestWall);

        var kindBox = new ComboBox { Header = "Create as", HorizontalAlignment = HorizontalAlignment.Stretch };
        kindBox.Items.Add(new ComboBoxItem { Content = "Door (schedule + wall deduct)", Tag = OpeningScheduleLinker.ScheduleKind.Door });
        kindBox.Items.Add(new ComboBoxItem { Content = "Window (schedule + wall deduct)", Tag = OpeningScheduleLinker.ScheduleKind.Window });
        kindBox.Items.Add(new ComboBoxItem { Content = "Deduct only (no door/window)", Tag = OpeningScheduleLinker.ScheduleKind.DeductOnly });
        kindBox.SelectedIndex = suggestKind switch
        {
            OpeningScheduleLinker.ScheduleKind.Door => 0,
            OpeningScheduleLinker.ScheduleKind.Window => 1,
            _ => 2
        };

        var wallBox = new ComboBox { Header = "Wall mark", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var m in walls)
            wallBox.Items.Add(m);
        wallBox.SelectedItem = walls.Contains(suggestWall, StringComparer.OrdinalIgnoreCase)
            ? walls.First(x => x.Equals(suggestWall, StringComparison.OrdinalIgnoreCase))
            : walls[0];

        double w = 0, h = 0;
        item.Fields.TryGetValue("opening_l", out var wl);
        item.Fields.TryGetValue("opening_h", out var hl);
        double.TryParse(wl, NumberStyles.Float, CultureInfo.InvariantCulture, out w);
        double.TryParse(hl, NumberStyles.Float, CultureInfo.InvariantCulture, out h);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Opening {w:0} × {h:0} mm — links to masonry deduct and optional Door/Window schedule.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(kindBox);
        panel.Children.Add(wallBox);

        var dlg = new ContentDialog
        {
            Title = "Commit opening",
            Content = panel,
            PrimaryButtonText = "Commit",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        var kind = OpeningScheduleLinker.ScheduleKind.Window;
        if (kindBox.SelectedItem is ComboBoxItem { Tag: OpeningScheduleLinker.ScheduleKind k })
            kind = k;
        string wallMark = wallBox.SelectedItem?.ToString() ?? suggestWall;

        try
        {
            var result = OpeningScheduleLinker.Commit(store, item, kind, wallMark);
            store.Notify();
            foreach (var r in _rows) r.Refresh();
            CanvasHost.Redraw(State.Items);
            Notify("Committed", result.Message, InfoBarSeverity.Success);
            UpdateModeStatus();
        }
        catch (Exception ex)
        {
            Notify("Commit failed", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var item = _selected ?? (ItemList.SelectedItem as TakeoffListRow)?.Item;
        if (item is null) return;
        State.Items.Remove(item);
        _selected = null;
        CanvasHost.SelectedItem = null;
        ProjectStore.Current.Notify();
        ReloadList();
        UpdateModeStatus();
    }

    private static double Dist(TakeoffPoint a, TakeoffPoint b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
