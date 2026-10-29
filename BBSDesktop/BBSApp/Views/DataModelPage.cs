// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.IO;
using BBSApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Office.Shared;
using Windows.Foundation;
using Windows.UI;
using MeasureKind = Office.Shared.Measure;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace BBSApp.Views;

/// <summary>
/// Data model (O-Dynamo): the work items and the rules that derive one item's quantity from another, drawn as a graph.
/// Each component has input parameters on its left and output parameters on its right, coloured by what they carry: yellow
/// linear, blue area, red volume. A connection joins an output to an input of the same colour, so it says which of a
/// component's measures feeds the next (masonry's length feeds skirting, its area both faces feeds plaster, its volume prices
/// the bricks). Which main component may feed which is fixed by <see cref="FlowRules"/>; RCC cannot be joined to masonry.
/// Right-click the canvas to add a component. Edits go straight into the project's link rules.
/// </summary>
public sealed class DataModelPage : Page
{
    const double NodeW = 214, HeaderH = 30, RowH = 22, ColGap = 120, RowGap = 24, Pad = 28, Port = 6;

    // Layout survives leaving the page; it is not saved with the project.
    static readonly Dictionary<string, Point> Positions = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> Shown = new(StringComparer.OrdinalIgnoreCase);
    static bool _shownSeeded;

    readonly Canvas _surface = new() { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    readonly TextBlock _status = new() { FontSize = 12, Opacity = 0.85, TextWrapping = TextWrapping.Wrap };

    TextBlock _title = null!;
    TextBox _name = null!;
    ComboBox _basis = null!;
    NumberBox _factor = null!;
    NumberBox _rateOverride = null!;
    ToggleSwitch _enabled = null!;
    ToggleSwitch _perItem = null!;
    TextBlock _formula = null!;
    TextBlock _warning = null!;
    StackPanel _editor = null!;
    StackPanel _component = null!;
    ComponentDef? _selComp;

    LinkRule? _selected;
    bool _loading;
    string? _dragNode;
    (double X, double Y) _dragOffset;
    (string Key, Office.Shared.Port Port)? _linkFrom;
    Line? _rubber;

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
        body.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = _surface,
        });
        _surface.PointerPressed += (_, e) => { if (e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed) Select(null); };
        _surface.PointerMoved += OnSurfaceMoved;
        _surface.PointerReleased += OnSurfaceReleased;
        _surface.PointerCanceled += (_, _) => EndGesture();
        _surface.PointerCaptureLost += (_, _) => EndGesture();
        _surface.RightTapped += OnSurfaceRightTapped;

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
        Loaded += (_, _) =>
        {
            Arrange(onlyMissing: true);
            Redraw();
            Say("Right-click the canvas to add a component. Drag from a coloured dot on a component's right to a dot of the same colour on another.");
        };
    }

    // ——— Ribbon ———

    static Brush Res(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : new SolidColorBrush(Microsoft.UI.Colors.Gray);

    static string GroupGlyph(string group) => group switch
    {
        ComponentCatalog.Rcc => "",
        ComponentCatalog.Formwork => "",
        ComponentCatalog.Masonry => "",
        ComponentCatalog.Earthwork => "",
        ComponentCatalog.Finishes => "",
        ComponentCatalog.Protection => "",
        _ => "",
    };

    Border BuildRibbon()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Padding = new Thickness(10, 6, 10, 6) };

        var groups = new List<UIElement>();
        foreach (var g in ComponentCatalog.Groups)
        {
            var b = RibbonButton(g.Display, GroupGlyph(g.Key), $"{g.Display}: {g.Role} Click for its sub components.", null);
            b.Flyout = BuildGroupMenu(g);
            groups.Add(b);
        }
        row.Children.Add(Group("Add component", groups));
        row.Children.Add(Divider());
        row.Children.Add(Group("Links", new UIElement[]
        {
            RibbonButton("Delete link", "", "Delete the selected link (Delete key)", DeleteLink),
            RibbonButton("Arrange", "", "Lay the components out again by what feeds what", () => { Positions.Clear(); Arrange(false); Redraw(); }),
        }));
        row.Children.Add(Divider());
        row.Children.Add(Group("Project", new UIElement[]
        {
            RibbonButton("Rules", "", "The colours and what may be connected to what", ShowRules),
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

    /// <summary>Text-only menu of a main component's sub components, built each time it opens so it shows what is already on the canvas.</summary>
    MenuFlyout BuildGroupMenu(ComponentGroup g)
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var c in ComponentCatalog.InGroup(g.Key))
            {
                var item = new MenuFlyoutItem { Text = c.Display, IsEnabled = !Shown.Contains(c.Key) };
                ToolTipService.SetToolTip(item, c.Note);
                item.Click += (_, _) => AddComponent(c, null);
                menu.Items.Add(item);
            }
        };
        return menu;
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

    Button RibbonButton(string label, string glyph, string tip, Action? click)
    {
        var content = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 20, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") });
        content.Children.Add(new TextBlock { Text = label, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 84, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        var b = new Button
        {
            Content = content,
            Width = 90,
            Padding = new Thickness(4, 6, 4, 6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
        };
        if (Application.Current.Resources.TryGetValue("HcwGhostButton", out var style) && style is Style s) b.Style = s;
        b.Height = 58;
        b.MinHeight = 58;
        ToolTipService.SetToolTip(b, tip);
        AutomationProperties.SetName(b, label);
        if (click is not null) b.Click += (_, _) => click();
        return b;
    }

    void AddComponent(ComponentDef c, Point? at)
    {
        Shown.Add(c.Key);
        if (at is { } p) Positions[c.Key] = new Point(Math.Max(0, p.X), Math.Max(0, p.Y));
        else Arrange(onlyMissing: true);
        Redraw();
        Say($"{c.Display} added. {c.Note}");
    }

    void RemoveComponent(ComponentDef c)
    {
        if (Book.Rules.Any(r => Same(r.SourceTrade, c.Key) || Same(r.TargetTrade, c.Key)))
        {
            Say($"{c.Display} has links; delete them first to take it off the canvas.");
            return;
        }
        Shown.Remove(c.Key);
        Redraw();
    }

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ——— Colours and geometry ———

    static SolidColorBrush Rgb(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));

    static SolidColorBrush MeasureBrush(MeasureKind m) => m switch
    {
        MeasureKind.Length => Rgb(240, 180, 40),
        MeasureKind.Area => Rgb(70, 140, 245),
        MeasureKind.Volume => Rgb(229, 72, 77),
        _ => Rgb(140, 140, 140),
    };

    static IEnumerable<ComponentDef> VisibleComponents() => ComponentCatalog.All.Where(c => Shown.Contains(c.Key));

    static double NodeH(ComponentDef c) => HeaderH + Math.Max(1, Math.Max(c.Inputs.Count, c.Outputs.Count)) * RowH + 8;

    Point InPortAt(ComponentDef c, int i) => new(Positions[c.Key].X, Positions[c.Key].Y + HeaderH + i * RowH + RowH / 2);
    Point OutPortAt(ComponentDef c, int i) => new(Positions[c.Key].X + NodeW, Positions[c.Key].Y + HeaderH + i * RowH + RowH / 2);

    void Arrange(bool onlyMissing)
    {
        var visible = VisibleComponents().ToList();
        var depth = visible.ToDictionary(c => c.Key, _ => 0, StringComparer.OrdinalIgnoreCase);
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
        foreach (var column in visible.GroupBy(c => depth[c.Key]).OrderBy(g => g.Key))
        {
            var y = Pad;
            foreach (var c in column.OrderBy(c => c.Display))
            {
                if (!(onlyMissing && Positions.ContainsKey(c.Key)))
                    Positions[c.Key] = new Point(Pad + column.Key * (NodeW + ColGap), y);
                y += NodeH(c) + RowGap;
            }
        }
    }

    // ——— Drawing ———

    void Redraw()
    {
        _surface.Children.Clear();
        double maxX = 0, maxY = 0;
        foreach (var c in VisibleComponents())
        {
            if (!Positions.ContainsKey(c.Key)) Arrange(true);
            var p = Positions[c.Key];
            maxX = Math.Max(maxX, p.X + NodeW + Pad);
            maxY = Math.Max(maxY, p.Y + NodeH(c) + Pad);
        }
        _surface.Width = Math.Max(maxX + 160, 900);
        _surface.Height = Math.Max(maxY + 120, 520);

        DrawBuiltInFormwork();
        foreach (var r in Book.Rules) DrawEdge(r);
        foreach (var c in VisibleComponents()) DrawNode(c);
    }

    static PathGeometry Curve(Point a, Point b)
    {
        var dx = Math.Max(40, Math.Abs(b.X - a.X) / 2);
        var fig = new PathFigure { StartPoint = a };
        fig.Segments.Add(new BezierSegment { Point1 = new Point(a.X + dx, a.Y), Point2 = new Point(b.X - dx, b.Y), Point3 = b });
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
    }

    /// <summary>Formwork is rebuilt from RCC by the estimate itself; show that as a faint fixed link when both ends are on the canvas.</summary>
    void DrawBuiltInFormwork()
    {
        var shuttering = ComponentCatalog.ByKey("Shuttering");
        if (shuttering is null || !Shown.Contains(shuttering.Key)) return;
        var into = shuttering.Inputs.ToList().FindIndex(p => p.Measure == MeasureKind.Area);
        foreach (var rcc in VisibleComponents().Where(c => c.GroupKey == ComponentCatalog.Rcc))
        {
            var from = rcc.Outputs.ToList().FindIndex(p => p.Measure == MeasureKind.Area);
            _surface.Children.Add(new Path
            {
                Data = Curve(OutPortAt(rcc, from), InPortAt(shuttering, into)),
                Stroke = Rgb(120, 120, 120),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 1, 3 },
                IsHitTestVisible = false,
            });
        }
    }

    void DrawEdge(LinkRule r)
    {
        var src = ComponentCatalog.ByKey(r.SourceTrade);
        var dst = ComponentCatalog.ByKey(r.TargetTrade);
        if (src is null || dst is null || !Shown.Contains(src.Key) || !Shown.Contains(dst.Key)) return;
        var outPort = ComponentCatalog.OutputFor(src, LinkBasisInfo.Label(r.Basis));
        var inPort = outPort is null ? null : ComponentCatalog.InputFor(dst, outPort.Measure);
        var a = outPort is null ? new Point(Positions[src.Key].X + NodeW, Positions[src.Key].Y + HeaderH / 2) : OutPortAt(src, src.Outputs.ToList().IndexOf(outPort));
        var b = inPort is null ? new Point(Positions[dst.Key].X, Positions[dst.Key].Y + HeaderH / 2) : InPortAt(dst, dst.Inputs.ToList().IndexOf(inPort));

        var breaks = outPort is null || FlowRules.Check(src, outPort, dst) is { Ok: false };
        var selected = ReferenceEquals(r, _selected);
        var colour = r.Enabled ? MeasureBrush(outPort?.Measure ?? MeasureKind.Area) : Rgb(130, 130, 130);
        var stroke = breaks ? Rgb(255, 90, 90) : colour;

        var hit = new Path { Data = Curve(a, b), Stroke = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), StrokeThickness = 14 };
        hit.PointerPressed += (_, e) => { Select(r); e.Handled = true; };
        var line = new Path { Data = Curve(a, b), Stroke = stroke, StrokeThickness = selected ? 4 : 2, IsHitTestVisible = false };
        if (!r.Enabled || breaks) line.StrokeDashArray = new DoubleCollection { 4, 3 };
        _surface.Children.Add(line);
        _surface.Children.Add(hit);
        _surface.Children.Add(new Microsoft.UI.Xaml.Shapes.Polygon
        {
            Points = new PointCollection { b, new Point(b.X - 9, b.Y - 5), new Point(b.X - 9, b.Y + 5) },
            Fill = stroke,
            IsHitTestVisible = false,
        });

        var label = new TextBlock
        {
            Text = (breaks ? "⚠ " : "") + $"×{r.Factor.ToString("0.###", CultureInfo.InvariantCulture)}",
            FontSize = 11,
            Foreground = stroke,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, (a.X + b.X) / 2 - 10);
        Canvas.SetTop(label, (a.Y + b.Y) / 2 - 18);
        _surface.Children.Add(label);
    }

    static string Materials(ComponentDef c)
    {
        var names = SchemaExport.ItemMaterialEdges().Where(e => Same(e.Trade, c.Key)).Select(e => e.MaterialName).Distinct().ToList();
        return names.Count == 0 ? "" : "\nMaterials: " + string.Join(", ", names);
    }

    void DrawNode(ComponentDef c)
    {
        var p = Positions[c.Key];
        var node = new Border
        {
            Width = NodeW,
            Height = NodeH(c),
            CornerRadius = new CornerRadius(4),
            Background = Res("HcwWhiteBrush"),
            BorderBrush = ReferenceEquals(_selComp, c) ? Rgb(255, 170, 60) : Res("HcwEdgeBrush"),
            BorderThickness = new Thickness(ReferenceEquals(_selComp, c) ? 2 : 1),
        };
        ToolTipService.SetToolTip(node, $"{c.Display} ({ComponentCatalog.Group(c.GroupKey)?.Display})\n{c.Note}{Materials(c)}");
        node.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed) return;
            var at = e.GetCurrentPoint(_surface).Position;
            _dragNode = c.Key;
            _dragOffset = (p.X - at.X, p.Y - at.Y);
            ShowComponent(c);
            _surface.CapturePointer(e.Pointer);
            e.Handled = true;
        };
        Canvas.SetLeft(node, p.X);
        Canvas.SetTop(node, p.Y);
        _surface.Children.Add(node);

        var icon = MainWindow.NavIcon(c.IconKey);
        icon.Width = 16;
        icon.Height = 16;
        icon.IsHitTestVisible = false;
        Canvas.SetLeft(icon, p.X + 10);
        Canvas.SetTop(icon, p.Y + 7);
        _surface.Children.Add(icon);
        AddText(c.Display, p.X + 32, p.Y + 6, 13, true, NodeW - 44, 1);

        for (var i = 0; i < c.Inputs.Count; i++)
        {
            var at = InPortAt(c, i);
            var dot = new Ellipse { Width = Port * 2, Height = Port * 2, Fill = Res("HcwWhiteBrush"), Stroke = MeasureBrush(c.Inputs[i].Measure), StrokeThickness = 3, IsHitTestVisible = false };
            Canvas.SetLeft(dot, at.X - Port);
            Canvas.SetTop(dot, at.Y - Port);
            _surface.Children.Add(dot);
            AddText(c.Inputs[i].Name, at.X + 12, at.Y - 8, 11, false, NodeW / 2 - 16, 0.8);
        }
        for (var j = 0; j < c.Outputs.Count; j++)
        {
            var at = OutPortAt(c, j);
            var port = c.Outputs[j];
            var dot = new Ellipse { Width = Port * 2, Height = Port * 2, Fill = MeasureBrush(port.Measure), Stroke = Res("HcwWhiteBrush"), StrokeThickness = 2 };
            ToolTipService.SetToolTip(dot, $"{port.Name} ({MeasureInfo.Colour(port.Measure)}, {MeasureInfo.Unit(port.Measure)}): drag to a {MeasureInfo.Colour(port.Measure)} input");
            var key = c.Key;
            var index = j;
            dot.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed) return;
                _linkFrom = (key, port);
                var from = OutPortAt(c, index);
                _rubber = new Line { X1 = from.X, Y1 = from.Y, X2 = from.X, Y2 = from.Y, Stroke = MeasureBrush(port.Measure), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false };
                _surface.Children.Add(_rubber);
                _surface.CapturePointer(e.Pointer);
                e.Handled = true;
            };
            Canvas.SetLeft(dot, at.X - Port);
            Canvas.SetTop(dot, at.Y - Port);
            _surface.Children.Add(dot);
            var t = AddText(port.Name, at.X - 12 - 80, at.Y - 8, 11, false, 80, 0.8);
            t.TextAlignment = TextAlignment.Right;
        }
    }

    TextBlock AddText(string text, double x, double y, double size, bool bold, double width, double opacity)
    {
        var t = new TextBlock { Text = text, FontSize = size, IsHitTestVisible = false, Opacity = opacity, TextTrimming = TextTrimming.CharacterEllipsis };
        if (width > 0) t.Width = width;
        if (bold) t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        _surface.Children.Add(t);
        return t;
    }

    // ——— Pointer gestures ———

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

    ComponentDef? ComponentAt(Point at) =>
        VisibleComponents().FirstOrDefault(c => new Rect(Positions[c.Key], new Size(NodeW, NodeH(c))).Contains(at));

    void OnSurfaceReleased(object sender, PointerRoutedEventArgs e)
    {
        var at = e.GetCurrentPoint(_surface).Position;
        var from = _linkFrom;
        EndGesture();
        _surface.ReleasePointerCaptures();
        if (from is null) return;

        var src = ComponentCatalog.ByKey(from.Value.Key)!;
        var target = ComponentAt(at);
        if (target is null || Same(target.Key, src.Key)) { Redraw(); return; }

        var port = from.Value.Port;
        var (ok, reason) = FlowRules.Check(src, port, target);
        if (!ok) { Say(reason); Redraw(); return; }
        if (WouldLoop(src.Key, target.Key)) { Say("That link would make an item depend on itself."); Redraw(); return; }
        if (Book.Rules.Any(r => Same(r.SourceTrade, src.Key) && Same(r.TargetTrade, target.Key)))
        {
            Say($"{src.Display} already feeds {target.Display}; click the link to change which measure it uses.");
            Redraw();
            return;
        }

        var rule = new LinkRule
        {
            Name = $"{target.Display} from {src.Display}",
            SourceTrade = src.Key,
            TargetTrade = target.Key,
            Basis = LinkBasisInfo.Parse(port.Basis),
            Factor = DefaultFactor(src.Key, target.Key, port.Basis),
            TargetUnit = target.Unit,
        };
        Book.Rules.Add(rule);
        ProjectStore.Current.Notify();
        Select(rule);
        Say($"{src.Display} {port.Name.ToLowerInvariant()} now feeds {target.Display}. Set the factor on the right.");
    }

    /// <summary>The usual factor: a wall has two faces, so plaster and paint take twice its area.</summary>
    static double DefaultFactor(string source, string target, string basis) =>
        Same(source, "Masonry") && Same(basis, "Area") && (Same(target, "Plaster") || Same(target, "Painting")) ? 2.0 : 1.0;

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

    // ——— Right-click: add a component ———

    void OnSurfaceRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var at = e.GetPosition(_surface);
        var menu = new MenuFlyout();

        var hit = ComponentAt(at);
        if (hit is not null)
        {
            var remove = new MenuFlyoutItem { Text = $"Remove {hit.Display} from the canvas" };
            remove.Click += (_, _) => RemoveComponent(hit);
            menu.Items.Add(remove);
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        var add = new MenuFlyoutSubItem { Text = "Add component" };
        foreach (var g in ComponentCatalog.Groups)
        {
            var sub = new MenuFlyoutSubItem { Text = g.Display };
            foreach (var c in ComponentCatalog.InGroup(g.Key))
            {
                var item = new MenuFlyoutItem { Text = c.Display, IsEnabled = !Shown.Contains(c.Key) };
                ToolTipService.SetToolTip(item, c.Note);
                item.Click += (_, _) => AddComponent(c, at);
                sub.Items.Add(item);
            }
            add.Items.Add(sub);
        }
        menu.Items.Add(add);
        menu.ShowAt(_surface, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = at });
        e.Handled = true;
    }

    // ——— Inspector ———

    UIElement BuildInspector()
    {
        _title = new TextBlock { Text = "No link selected", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        _name = new TextBox { Header = "Name" };
        _basis = new ComboBox { Header = "Measure taken from the source", HorizontalAlignment = HorizontalAlignment.Stretch };
        _factor = new NumberBox { Header = "Factor", Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, SmallChange = 0.5, LargeChange = 1 };
        _rateOverride = new NumberBox { Header = "Rate for the derived lines (₹, 0 = from the rate book)", Minimum = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        _enabled = new ToggleSwitch { Header = "On", OnContent = "On", OffContent = "Off" };
        _perItem = new ToggleSwitch { Header = "One line per source item", OnContent = "Yes", OffContent = "One total" };
        _formula = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
        _warning = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Rgb(255, 120, 120) };

        _name.TextChanged += (_, _) => Apply();
        _basis.SelectionChanged += (_, _) => Apply();
        _factor.ValueChanged += (_, _) => Apply();
        _rateOverride.ValueChanged += (_, _) => Apply();
        _enabled.Toggled += (_, _) => Apply();
        _perItem.Toggled += (_, _) => Apply();

        var delete = new Button { Content = "Delete link" };
        delete.Click += (_, _) => DeleteLink();

        _editor = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        foreach (var c in new UIElement[] { _name, _basis, _factor, _rateOverride, _enabled, _perItem, _formula, _warning, delete }) _editor.Children.Add(c);

        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(14) };
        panel.Children.Add(_title);
        panel.Children.Add(Legend());
        _component = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        panel.Children.Add(_component);
        panel.Children.Add(_editor);
        return panel;
    }

    static StackPanel Legend()
    {
        var l = new StackPanel { Spacing = 3 };
        foreach (var m in new[] { MeasureKind.Length, MeasureKind.Area, MeasureKind.Volume })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = MeasureBrush(m), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = $"{MeasureInfo.Colour(m)}: {m.ToString().ToLowerInvariant()} ({MeasureInfo.Unit(m)})", FontSize = 11, Opacity = 0.8 });
            l.Children.Add(row);
        }
        return l;
    }

    // ——— Component panel: how the item is specified and measured in a DSR-style schedule ———

    static TextBlock Heading(string text) => new() { Text = text, FontSize = 11, Opacity = 0.6, Margin = new Thickness(0, 6, 0, 0) };
    static TextBlock Body(string text) => new() { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };

    void ShowComponent(ComponentDef c)
    {
        _selected = null;
        _editor.Visibility = Visibility.Collapsed;
        _selComp = c;
        _title.Text = c.Display;
        _component.Children.Clear();
        _component.Visibility = Visibility.Visible;

        var group = ComponentCatalog.Group(c.GroupKey)?.Display ?? c.GroupKey;
        var dsr = DsrCatalog.For(c.Key);
        _component.Children.Add(Body($"{group}. {c.Note}"));

        if (dsr is not null)
        {
            _component.Children.Add(Heading("DSR sub-head"));
            _component.Children.Add(Body(DsrCatalog.SubHead(dsr.SubHeadKey)?.Name ?? dsr.SubHeadKey));

            _component.Children.Add(Heading("Rate-book item"));
            _component.Children.Add(Body(RateItemText(c)));

            _component.Children.Add(Heading("Item wording"));
            _component.Children.Add(Body(dsr.ItemWording));

            _component.Children.Add(Heading("Measured in " + dsr.MeasuredIn));
            _component.Children.Add(Body(dsr.MeasurementRule));

            if (dsr.Materials.Count > 0)
            {
                _component.Children.Add(Heading("Materials and their standards"));
                foreach (var m in dsr.Materials)
                    _component.Children.Add(Body(m.Material + (m.Standard.Length > 0 ? " — " + m.Standard : " — as the specification clause says") + (m.Note.Length > 0 ? ". " + m.Note : "")));
            }
        }

        _component.Children.Add(Heading("Parameters"));
        _component.Children.Add(Body("In: " + (c.Inputs.Count == 0 ? "none (measured)" : string.Join(", ", c.Inputs.Select(p => $"{p.Name} ({MeasureInfo.Colour(p.Measure)})")))));
        _component.Children.Add(Body("Out: " + string.Join(", ", c.Outputs.Select(p => $"{p.Name} ({MeasureInfo.Colour(p.Measure)})"))));
        Redraw();
    }

    /// <summary>The rate-book item the estimate prices this component on (the user's DSR import supplies the code, wording and rate).</summary>
    static string RateItemText(ComponentDef c)
    {
        if (c.GroupKey == ComponentCatalog.Rcc) return "RCC is priced from the concrete, formwork and steel items of its own pages.";
        RateBookStore.Current.EnsureLoaded();
        var version = RateBookStore.Current.ActiveOrFirst();
        var code = EstimateCalculator.CodeForElement(c.Key, c.Unit);
        if (version is null) return $"{code}: no rate book is loaded.";
        var index = RateBookStore.Current.IndexByCode(version);
        return index.TryGetValue(code, out var ri)
            ? $"{code}: {ri.Description} — ₹{ri.Rate.ToString("0.##", CultureInfo.InvariantCulture)} per {ri.Unit} (“{version.Name}”)."
            : $"{code}: not in “{version.Name}”. Add it to the rate book, or import the DSR item and give it this code.";
    }

    void Select(LinkRule? rule)
    {
        _selected = rule;
        if (rule is not null || _selComp is not null) { _selComp = null; _component.Visibility = Visibility.Collapsed; }
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
            _basis.Items.Clear();
            var src = ComponentCatalog.ByKey(rule.SourceTrade);
            var dst = ComponentCatalog.ByKey(rule.TargetTrade);
            if (src is not null && dst is not null)
                foreach (var o in src.Outputs.Where(o => ComponentCatalog.InputFor(dst, o.Measure) is not null))
                    _basis.Items.Add(o.Name);
            var label = LinkBasisInfo.Label(rule.Basis);
            if (!_basis.Items.Contains(label)) _basis.Items.Add(label);
            _basis.SelectedItem = label;
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
        _warning.Text = "";
        if (_selected is null) { _formula.Text = ""; return; }
        _formula.Text = $"{LinkTradeRegistry.Display(_selected.TargetTrade)} = {_selected.Factor.ToString("0.###", CultureInfo.InvariantCulture)} × "
                      + $"{LinkBasisInfo.Label(_selected.Basis).ToLowerInvariant()} of {LinkTradeRegistry.Display(_selected.SourceTrade)}";
        var src = ComponentCatalog.ByKey(_selected.SourceTrade);
        var dst = ComponentCatalog.ByKey(_selected.TargetTrade);
        var port = src is null ? null : ComponentCatalog.OutputFor(src, LinkBasisInfo.Label(_selected.Basis));
        if (src is null || dst is null || port is null)
            _warning.Text = "This link uses a measure the source does not offer.";
        else if (FlowRules.Check(src, port, dst) is { Ok: false } bad)
            _warning.Text = "⚠ " + bad.Reason + " Delete this link, or keep it only if you know the estimate needs it.";
    }

    void Apply()
    {
        if (_loading || _selected is null) return;
        _selected.Name = _name.Text;
        if (_basis.SelectedItem is string b) _selected.Basis = LinkBasisInfo.Parse(b);
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

    async void ShowRules()
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var lines = new List<string>
        {
            "Colours: yellow is linear (m), blue is area (m²), red is volume (m³). An output connects only to an input of the same colour.",
            "",
            "Which main component may feed which:",
        };
        foreach (var from in ComponentCatalog.Groups)
        {
            var feeds = ComponentCatalog.Groups.Where(to => FlowRules.GroupMayFeed(from.Key, to.Key)).Select(to => to.Display).ToList();
            lines.Add($"  {from.Display}: " + (feeds.Count == 0 ? "feeds nothing" : string.Join(", ", feeds)));
        }
        lines.Add("");
        lines.Add("Inside a main component only these chains exist: Earthwork to PCC bed; Plastering to Painting; Flooring to Screed and to Skirting.");
        lines.Add("RCC cannot be joined to masonry; formwork follows the RCC without a link. Nothing feeds RCC.");
        text.Text = string.Join("\n", lines);
        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Rules of the data model",
            Content = new ScrollViewer { Content = text, MaxHeight = 420 },
            CloseButtonText = "Close",
        }.ShowAsync();
    }

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
