// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.IO;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using BBSApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace BBSApp.Views;

/// <summary>
/// Data model (O-Dynamo): the work items and the rules that derive one item's quantity from another, drawn as a graph.
/// An item is a node, a rule is an arrow ("target = factor x measure of source"). A ribbon of icons adds items and
/// tools; dragging from an item's right-hand dot to another item makes a rule; a rule is edited on the right. Edits go
/// straight into the project's link rules (the same rules the Item links page lists).
/// </summary>
public sealed class DataModelPage : Page
{
    const double NodeW = 196, NodeH = 54, ColGap = 110, RowGap = 22, Pad = 28, Port = 6;

    // Layout survives leaving the page; it is not saved with the project.
    static readonly Dictionary<string, Point> Positions = new();
    static readonly HashSet<string> Shown = new(StringComparer.OrdinalIgnoreCase);
    static bool _shownSeeded;

    readonly Canvas _surface = new() { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    readonly StackPanel _inspector = new() { Spacing = 10, Padding = new Thickness(14), IsHitTestVisible = true };
    readonly TextBlock _status = new() { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    readonly Dictionary<string, Button> _itemButtons = new(StringComparer.OrdinalIgnoreCase);

    TextBlock _title = null!;
    TextBox _name = null!;
    ComboBox _basis = null!;
    NumberBox _factor = null!;
    NumberBox _rateOverride = null!;
    ToggleSwitch _enabled = null!;
    ToggleSwitch _perItem = null!;
    TextBlock _formula = null!;
    StackPanel _editor = null!;

    LinkRule? _selected;
    bool _loading;
    string? _dragNode;
    (double X, double Y) _dragOffset;
    string? _linkFrom;
    Line? _rubber;
    uint _pointer;

    LinkRuleBook Book => ProjectStore.Current.LinkRules;

    public DataModelPage()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Book.SeedIfEmpty();
        if (!_shownSeeded)
        {
            foreach (var r in Book.Rules) { Shown.Add(r.SourceTrade); Shown.Add(r.TargetTrade); }
            _shownSeeded = true;
        }

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var ribbon = BuildRibbon();
        Grid.SetRow(ribbon, 0);
        root.Children.Add(ribbon);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = _surface,
        };
        _surface.PointerPressed += OnSurfacePressed;
        _surface.PointerMoved += OnSurfaceMoved;
        _surface.PointerReleased += OnSurfaceReleased;
        _surface.PointerCanceled += (_, _) => EndGesture();
        _surface.PointerCaptureLost += (_, _) => EndGesture();
        body.Children.Add(scroll);

        var side = new Border
        {
            Background = Res("HcwSoftBrush"),
            BorderBrush = Res("HcwEdgeBrush"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = new ScrollViewer { Content = BuildInspector() },
        };
        Grid.SetColumn(side, 1);
        body.Children.Add(side);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        _status.Margin = new Thickness(16, 6, 16, 8);
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);

        Content = root;
        Loaded += (_, _) => { Arrange(onlyMissing: true); Redraw(); Say("Click an item in the ribbon to put it on the canvas. Drag from an item's right-hand dot to another item to link them."); };
    }

    // ——— Ribbon ———

    static Brush Res(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    Border BuildRibbon()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Padding = new Thickness(10, 6, 10, 6) };

        row.Children.Add(Group("Items", ItemButtons()));
        row.Children.Add(Divider());
        row.Children.Add(Group("Links", new UIElement[]
        {
            RibbonButton("Link", "", "Drag from an item's right-hand dot to another item", null),
            RibbonButton("Delete link", "", "Delete the selected link (Delete key)", DeleteLink),
            RibbonButton("Arrange", "", "Lay the items out again by what feeds what", () => { Positions.Clear(); Arrange(false); Redraw(); }),
        }));
        row.Children.Add(Divider());
        row.Children.Add(Group("Project", new UIElement[]
        {
            RibbonButton("Defaults", "", "Replace the project's links with the standard library", ResetDefaults),
            RibbonButton("Export ERD", "", "Write the logical ERD (aqc-core.dbml and .sql) for dbdiagram.io", ExportSchema),
        }));

        return new Border
        {
            Background = Res("HcwSoftBrush"),
            BorderBrush = Res("HcwEdgeBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Enabled,
                VerticalScrollMode = ScrollMode.Disabled,
                Content = row,
            },
        };
    }

    IEnumerable<UIElement> ItemButtons()
    {
        foreach (var t in LinkTradeRegistry.All)
        {
            var key = t.Key;
            var b = RibbonButton(t.Display, null, $"{t.Display} ({t.Unit}): put it on the canvas, or take it off when it has no links", () => ToggleItem(key), MainWindow.NavIcon(key));
            _itemButtons[key] = b;
            yield return b;
        }
        RefreshItemButtons();
    }

    static StackPanel Group(string caption, IEnumerable<UIElement> buttons)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var b in buttons) strip.Children.Add(b);
        var g = new StackPanel { Spacing = 2 };
        g.Children.Add(strip);
        g.Children.Add(new TextBlock { Text = caption, FontSize = 10, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center });
        return g;
    }

    static Border Divider() => new() { Width = 1, Margin = new Thickness(8, 4, 8, 4), Background = Res("HcwEdgeBrush") };

    Button RibbonButton(string label, string? glyph, string tip, Action? click, UIElement? icon = null)
    {
        UIElement glyphOrIcon = icon ?? new FontIcon { Glyph = glyph ?? "", FontSize = 20, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") };
        if (icon is FrameworkElement fe) { fe.Width = 22; fe.Height = 22; }
        var content = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new Border { Height = 24, HorizontalAlignment = HorizontalAlignment.Center, Child = glyphOrIcon });
        content.Children.Add(new TextBlock { Text = label, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 74, HorizontalAlignment = HorizontalAlignment.Center });
        var b = new Button
        {
            Content = content,
            Width = 80,
            Padding = new Thickness(4, 6, 4, 6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
        };
        if (Application.Current.Resources.TryGetValue("HcwGhostButton", out var style) && style is Style s) b.Style = s;
        ToolTipService.SetToolTip(b, tip);
        AutomationProperties.SetName(b, label);
        if (click is not null) b.Click += (_, _) => click();
        else b.IsHitTestVisible = false; // "Link" is a hint for the drag, not a command
        if (click is null) b.Opacity = 0.7;
        return b;
    }

    void RefreshItemButtons()
    {
        foreach (var (key, b) in _itemButtons)
            b.Background = Shown.Contains(key) ? Res("HcwEdgeBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    void ToggleItem(string key)
    {
        if (Shown.Contains(key))
        {
            if (Book.Rules.Any(r => Same(r.SourceTrade, key) || Same(r.TargetTrade, key)))
            {
                Say($"{LinkTradeRegistry.Display(key)} has links; delete them first to take it off the canvas.");
                return;
            }
            Shown.Remove(key);
        }
        else
        {
            Shown.Add(key);
            Arrange(onlyMissing: true);
        }
        RefreshItemButtons();
        Redraw();
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ——— Layout ———

    void Arrange(bool onlyMissing)
    {
        var visible = LinkTradeRegistry.All.Where(t => Shown.Contains(t.Key)).ToList();
        var depth = visible.ToDictionary(t => t.Key, _ => 0, StringComparer.OrdinalIgnoreCase);
        for (var pass = 0; pass < visible.Count; pass++)
        {
            var changed = false;
            foreach (var r in Book.Rules)
            {
                if (!depth.ContainsKey(r.SourceTrade) || !depth.ContainsKey(r.TargetTrade)) continue;
                var want = depth[r.SourceTrade] + 1;
                if (want > depth[r.TargetTrade] && want <= visible.Count) { depth[r.TargetTrade] = want; changed = true; }
            }
            if (!changed) break;
        }
        foreach (var column in visible.GroupBy(t => depth[t.Key]).OrderBy(g => g.Key))
        {
            var row = 0;
            foreach (var t in column.OrderBy(t => t.Display))
            {
                if (onlyMissing && Positions.ContainsKey(t.Key)) { row++; continue; }
                Positions[t.Key] = new Point(Pad + column.Key * (NodeW + ColGap), Pad + row++ * (NodeH + RowGap));
            }
        }
    }

    // ——— Drawing ———

    Point OutPort(string k) => new(Positions[k].X + NodeW, Positions[k].Y + NodeH / 2);
    Point InPort(string k) => new(Positions[k].X, Positions[k].Y + NodeH / 2);

    void Redraw()
    {
        _surface.Children.Clear();
        double maxX = 0, maxY = 0;
        foreach (var t in LinkTradeRegistry.All.Where(t => Shown.Contains(t.Key)))
        {
            if (!Positions.ContainsKey(t.Key)) Arrange(true);
            var p = Positions[t.Key];
            maxX = Math.Max(maxX, p.X + NodeW + Pad);
            maxY = Math.Max(maxY, p.Y + NodeH + Pad);
        }
        _surface.Width = Math.Max(maxX + 160, 900);
        _surface.Height = Math.Max(maxY + 120, 520);

        foreach (var r in Book.Rules.Where(r => Shown.Contains(r.SourceTrade) && Shown.Contains(r.TargetTrade))) DrawEdge(r);
        foreach (var t in LinkTradeRegistry.All.Where(t => Shown.Contains(t.Key))) DrawNode(t);
        RefreshItemButtons();
    }

    static SolidColorBrush Rgb(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));

    void DrawEdge(LinkRule r)
    {
        var a = OutPort(r.SourceTrade);
        var b = InPort(r.TargetTrade);
        var dx = Math.Max(40, Math.Abs(b.X - a.X) / 2);
        // A geometry can belong to one shape only, so the visible line and its wide click target each get their own.
        PathGeometry Curve()
        {
            var fig = new PathFigure { StartPoint = a };
            fig.Segments.Add(new BezierSegment { Point1 = new Point(a.X + dx, a.Y), Point2 = new Point(b.X - dx, b.Y), Point3 = b });
            var g = new PathGeometry();
            g.Figures.Add(fig);
            return g;
        }
        var selected = ReferenceEquals(r, _selected);
        var stroke = !r.Enabled ? Rgb(130, 130, 130) : selected ? Rgb(255, 170, 60) : Rgb(90, 150, 240);

        var hit = new Path { Data = Curve(), Stroke = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), StrokeThickness = 14 };
        hit.PointerPressed += (_, e) => { Select(r); e.Handled = true; };
        var line = new Path { Data = Curve(), Stroke = stroke, StrokeThickness = selected ? 3 : 2, IsHitTestVisible = false };
        if (!r.Enabled) line.StrokeDashArray = new DoubleCollection { 4, 3 };
        _surface.Children.Add(line);
        _surface.Children.Add(hit);

        var arrow = new Microsoft.UI.Xaml.Shapes.Polygon
        {
            Points = new PointCollection { b, new Point(b.X - 9, b.Y - 5), new Point(b.X - 9, b.Y + 5) },
            Fill = stroke,
            IsHitTestVisible = false,
        };
        _surface.Children.Add(arrow);

        var label = new TextBlock
        {
            Text = $"×{r.Factor.ToString("0.###", CultureInfo.InvariantCulture)} {LinkBasisInfo.Label(r.Basis)}",
            FontSize = 11,
            Foreground = stroke,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, (a.X + b.X) / 2 - 28);
        Canvas.SetTop(label, (a.Y + b.Y) / 2 - 18);
        _surface.Children.Add(label);
    }

    void DrawNode(TradeDef t)
    {
        var p = Positions[t.Key];
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock { Text = t.Display, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = $"{t.Unit}  ·  {RateText(t)}", FontSize = 11, Opacity = 0.65 });
        var icon = MainWindow.NavIcon(t.Key);
        icon.Width = 22;
        icon.Height = 22;
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 10, 0);
        var inner = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        inner.Children.Add(icon);
        inner.Children.Add(text);
        var node = new Border
        {
            Width = NodeW,
            Height = NodeH,
            CornerRadius = new CornerRadius(4),
            Background = Res("HcwWhiteBrush"),
            BorderBrush = Res("HcwEdgeBrush"),
            BorderThickness = new Thickness(1),
            Child = inner,
        };
        node.PointerPressed += (_, e) =>
        {
            _dragNode = t.Key;
            _dragOffset = (p.X - e.GetCurrentPoint(_surface).Position.X, p.Y - e.GetCurrentPoint(_surface).Position.Y);
            _pointer = e.Pointer.PointerId;
            _surface.CapturePointer(e.Pointer);
            e.Handled = true;
        };
        Canvas.SetLeft(node, p.X);
        Canvas.SetTop(node, p.Y);
        _surface.Children.Add(node);

        var port = new Ellipse { Width = Port * 2, Height = Port * 2, Fill = Rgb(90, 150, 240), Stroke = Res("HcwWhiteBrush"), StrokeThickness = 2 };
        ToolTipService.SetToolTip(port, "Drag to another item to link");
        port.PointerPressed += (_, e) =>
        {
            _linkFrom = t.Key;
            var from = OutPort(t.Key);
            _rubber = new Line { X1 = from.X, Y1 = from.Y, X2 = from.X, Y2 = from.Y, Stroke = Rgb(255, 170, 60), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false };
            _surface.Children.Add(_rubber);
            _pointer = e.Pointer.PointerId;
            _surface.CapturePointer(e.Pointer);
            e.Handled = true;
        };
        Canvas.SetLeft(port, p.X + NodeW - Port);
        Canvas.SetTop(port, p.Y + NodeH / 2 - Port);
        _surface.Children.Add(port);

        var inPort = new Ellipse { Width = Port * 2, Height = Port * 2, Fill = Res("HcwWhiteBrush"), Stroke = Rgb(90, 150, 240), StrokeThickness = 2, IsHitTestVisible = false };
        Canvas.SetLeft(inPort, p.X - Port);
        Canvas.SetTop(inPort, p.Y + NodeH / 2 - Port);
        _surface.Children.Add(inPort);
    }

    static string RateText(TradeDef t)
    {
        RateBookStore.Current.EnsureLoaded();
        var version = RateBookStore.Current.ActiveOrFirst();
        if (version is null) return "no rate";
        var index = RateBookStore.Current.IndexByCode(version);
        var code = EstimateCalculator.CodeForElement(t.Key, t.Unit);
        return index.TryGetValue(code, out var ri) && ri.Rate > 0 ? "₹" + ri.Rate.ToString("0.##", CultureInfo.InvariantCulture) : "no rate";
    }

    // ——— Pointer gestures ———

    void OnSurfacePressed(object sender, PointerRoutedEventArgs e) => Select(null);

    void OnSurfaceMoved(object sender, PointerRoutedEventArgs e)
    {
        var at = e.GetCurrentPoint(_surface).Position;
        if (_rubber is not null)
        {
            _rubber.X2 = at.X;
            _rubber.Y2 = at.Y;
        }
        else if (_dragNode is not null)
        {
            Positions[_dragNode] = new Point(Math.Max(0, at.X + _dragOffset.X), Math.Max(0, at.Y + _dragOffset.Y));
            Redraw();
        }
    }

    void OnSurfaceReleased(object sender, PointerRoutedEventArgs e)
    {
        var at = e.GetCurrentPoint(_surface).Position;
        var from = _linkFrom;
        EndGesture();
        _surface.ReleasePointerCaptures();
        if (from is null) return;

        var target = LinkTradeRegistry.All.FirstOrDefault(t => Shown.Contains(t.Key) && new Rect(Positions[t.Key], new Size(NodeW, NodeH)).Contains(at))?.Key;
        if (target is null || Same(target, from)) { Redraw(); return; }
        if (WouldLoop(from, target)) { Say("That link would make an item depend on itself."); Redraw(); return; }
        if (Book.Rules.Any(r => Same(r.SourceTrade, from) && Same(r.TargetTrade, target))) { Say("Those two items are already linked."); Redraw(); return; }

        var src = LinkTradeRegistry.ByKey(from)!;
        var tgt = LinkTradeRegistry.ByKey(target)!;
        var rule = new LinkRule
        {
            Name = $"{tgt.Display} from {src.Display}",
            SourceTrade = src.Key,
            TargetTrade = tgt.Key,
            Basis = src.DefaultBasis,
            Factor = 1,
            TargetUnit = tgt.Unit,
        };
        Book.Rules.Add(rule);
        ProjectStore.Current.Notify();
        Select(rule);
        Say($"Linked {src.Display} to {tgt.Display}. Set the factor on the right.");
    }

    void EndGesture()
    {
        _dragNode = null;
        _linkFrom = null;
        if (_rubber is not null) { _surface.Children.Remove(_rubber); _rubber = null; }
    }

    bool WouldLoop(string source, string target)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(target);
        while (stack.Count > 0)
        {
            var k = stack.Pop();
            if (Same(k, source)) return true;
            if (!seen.Add(k)) continue;
            foreach (var r in Book.Rules.Where(r => Same(r.SourceTrade, k))) stack.Push(r.TargetTrade);
        }
        return false;
    }

    // ——— Inspector ———

    UIElement BuildInspector()
    {
        _title = new TextBlock { Text = "No link selected", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        _name = new TextBox { Header = "Name" };
        _basis = new ComboBox { Header = "Measure of the source", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var b in LinkBasisInfo.All) _basis.Items.Add(LinkBasisInfo.Label(b));
        _factor = new NumberBox { Header = "Factor", Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, SmallChange = 0.5, LargeChange = 1 };
        _rateOverride = new NumberBox { Header = "Rate for the derived lines (₹, 0 = from the rate book)", Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        _enabled = new ToggleSwitch { Header = "On", OnContent = "On", OffContent = "Off" };
        _perItem = new ToggleSwitch { Header = "One line per source item", OnContent = "Yes", OffContent = "One total" };
        _formula = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };

        _name.TextChanged += (_, _) => Apply();
        _basis.SelectionChanged += (_, _) => Apply();
        _factor.ValueChanged += (_, _) => Apply();
        _rateOverride.ValueChanged += (_, _) => Apply();
        _enabled.Toggled += (_, _) => Apply();
        _perItem.Toggled += (_, _) => Apply();

        var delete = new Button { Content = "Delete link" };
        delete.Click += (_, _) => DeleteLink();

        _editor = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        foreach (var c in new UIElement[] { _name, _basis, _factor, _rateOverride, _enabled, _perItem, _formula, delete }) _editor.Children.Add(c);

        _inspector.Children.Add(_title);
        _inspector.Children.Add(_editor);
        return _inspector;
    }

    void Select(LinkRule? rule)
    {
        _selected = rule;
        _loading = true;
        _editor.Visibility = rule is null ? Visibility.Collapsed : Visibility.Visible;
        if (rule is null)
        {
            _title.Text = "No link selected";
        }
        else
        {
            _title.Text = $"{LinkTradeRegistry.Display(rule.TargetTrade)} from {LinkTradeRegistry.Display(rule.SourceTrade)}";
            _name.Text = rule.Name;
            _basis.SelectedItem = LinkBasisInfo.Label(rule.Basis);
            _factor.Value = rule.Factor;
            _rateOverride.Value = rule.RateOverride;
            _enabled.IsOn = rule.Enabled;
            _perItem.IsOn = rule.PerItem;
        }
        _loading = false;
        ShowFormula();
        Redraw();
    }

    void ShowFormula()
    {
        if (_selected is null) { _formula.Text = ""; return; }
        _formula.Text = $"{LinkTradeRegistry.Display(_selected.TargetTrade)} = {_selected.Factor.ToString("0.###", CultureInfo.InvariantCulture)} × "
                      + $"{LinkBasisInfo.Label(_selected.Basis).ToLowerInvariant()} of {LinkTradeRegistry.Display(_selected.SourceTrade)}";
    }

    void Apply()
    {
        if (_loading || _selected is null) return;
        _selected.Name = _name.Text;
        _selected.Basis = LinkBasisInfo.Parse(_basis.SelectedItem as string);
        if (double.IsFinite(_factor.Value) && _factor.Value >= 0) _selected.Factor = _factor.Value;
        _selected.RateOverride = double.IsFinite(_rateOverride.Value) ? Math.Max(0, _rateOverride.Value) : 0;
        _selected.Enabled = _enabled.IsOn;
        _selected.PerItem = _perItem.IsOn;
        ProjectStore.Current.Notify();
        ShowFormula();
        Redraw();
    }

    void DeleteLink()
    {
        if (_selected is null) { Say("Click a link first."); return; }
        Book.Rules.Remove(_selected);
        ProjectStore.Current.Notify();
        Select(null);
        Say("Link deleted.");
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Windows.System.VirtualKey.Delete && _selected is not null && FocusManager.GetFocusedElement(XamlRoot) is not TextBox and not NumberBox)
        {
            DeleteLink();
            e.Handled = true;
        }
    }

    // ——— Project ———

    async void ResetDefaults()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Replace the links with the standard library?",
            Content = "Every link now in this project is replaced by the standard ones.",
            PrimaryButtonText = "Replace",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        Book.ResetToDefaults();
        ProjectStore.Current.Notify();
        Shown.Clear();
        foreach (var r in Book.Rules) { Shown.Add(r.SourceTrade); Shown.Add(r.TargetTrade); }
        Positions.Clear();
        Arrange(false);
        Select(null);
        Say("The standard links are back.");
    }

    void ExportSchema()
    {
        try
        {
            var dir = System.IO.Path.Combine(Branding.AppDataDirectory, "schema");
            var (dbml, _) = SchemaExport.WriteFiles(dir);
            Say($"ERD written to {System.IO.Path.GetDirectoryName(dbml)} (aqc-core.dbml and .sql).");
        }
        catch (Exception ex)
        {
            Say("Export failed: " + ex.Message);
        }
    }

    void Say(string text) => _status.Text = text;
}
