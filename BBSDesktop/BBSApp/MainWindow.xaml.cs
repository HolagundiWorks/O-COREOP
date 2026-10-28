// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using BBSApp.Models;
using BBSApp.Services;
using BBSApp.Services.Ai;
using BBSApp.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace BBSApp;

public sealed partial class MainWindow : Window, IAppCommandBus, IAssistantConfirm
{
    private sealed record NavCmd(string Tag, string Label, object? Icon);

    private sealed record RibbonTab(string Id, string Title, NavCmd[] Commands);

    private readonly List<RibbonTab> _tabs = new();
    // Canonical navigable tags, matched case-sensitively to NavigateTo's switch arms.
    private readonly HashSet<string> _navTags = new(StringComparer.Ordinal);
    private string _activeTab = "project";
    private string _activeTag = "dashboard";
    private DispatcherTimer? _toastTimer;
    private int _toastGeneration;
    private AssistantService? _assistant;
    private bool _assistantBusy;

    public MainWindow()
    {
        InitializeComponent();
        App.MainWindow = this;
        // Opaque Fog canvas — no Mica/Acrylic (HCW pure neu; DESKTOP-WINUI-UX).
        SystemBackdrop = null;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Hcw.Ui.SuiteMotion.RespectSystemPreference("PageContentTransitions", "CardContentTransitions");
        Hcw.Ui.SuiteMotion.ApplyToFrame(ContentFrame);
        ThemeSwitch.Attach(this);
        AutomationProperties.SetName(ThemeSwitch, "Colour theme");
        AutomationProperties.SetLandmarkType(AppNav, AutomationLandmarkType.Navigation);
        AutomationProperties.SetLandmarkType(ContentFrame, AutomationLandmarkType.Main);
        Hcw.Ui.SuiteAppearance.Changed += OnSuiteThemeChanged;
        ApplyBrandingChrome();
        TrySetAppIcon();
        ProjectStore.Current.SeedDefaults();
        BuildRibbonModel();
        Hcw.Ui.SuiteNavigation.Apply(AppNav, Hcw.Ui.SuiteApp.Metra);
        BuildNavigation();
        NavigateTo("dashboard");
        UpdatePersonaBadge();
        SetWindowTitle(Branding.WindowTitle(ProjectStore.Current.Name, ProjectStore.Current.IsDirty));
        StartCompanyPolling();
        _ = StartFromSuiteAsync();
        AppNotify.Raised += OnAppNotify;
        ProjectStore.Current.Changed += OnStoreChanged;
        Closed += (_, _) =>
        {
            Hcw.Ui.SuiteAppearance.Changed -= OnSuiteThemeChanged;
            AppNotify.Raised -= OnAppNotify;
            ProjectStore.Current.Changed -= OnStoreChanged;
        };
        if (Content is FrameworkElement root)
            root.KeyDown += Window_KeyDown;
    }

    private void OnSuiteThemeChanged(Microsoft.UI.Xaml.ElementTheme _)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Hcw.Ui.SuiteAppearance.Apply(this);
            if (Content is FrameworkElement root)
                root.RequestedTheme = Hcw.Ui.SuiteAppearance.Current;
            AppNav.RequestedTheme = Hcw.Ui.SuiteAppearance.Current;
            Hcw.Ui.SuiteNavigation.Apply(AppNav, Hcw.Ui.SuiteApp.Metra);
            // Rebuild the ribbon model so ImageIcon sources pick up the active theme dictionary (light/dark SVG).
            BuildRibbonModel();
            BuildNavigation();
            ApplyBrandingChrome();
        });
    }

    // Was 0.8; user asked 1.2× of that → 0.96 of design.
    const double UiScale = 1.0; // the shell follows the system text size and display scale exactly

    void ScaleViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ScaleHost is null || e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        // RenderTransform does not affect layout — enlarge host so 0.8× fills the viewport.
        ScaleHost.Width = e.NewSize.Width / UiScale;
        ScaleHost.Height = e.NewSize.Height / UiScale;
        if (UiScaleTransform is not null)
        {
            UiScaleTransform.ScaleX = UiScale;
            UiScaleTransform.ScaleY = UiScale;
        }
    }

    private void OnStoreChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SetWindowTitle(Branding.WindowTitle(ProjectStore.Current.Name, ProjectStore.Current.IsDirty));
            UpdatePersonaBadge();
        });
    }

    private void Window_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!ctrl) return;
        string? tab = e.Key switch
        {
            VirtualKey.Number1 or VirtualKey.NumberPad1 => "project",
            VirtualKey.Number2 or VirtualKey.NumberPad2 => "rcc",
            VirtualKey.Number3 or VirtualKey.NumberPad3 => "civil",
            VirtualKey.Number4 or VirtualKey.NumberPad4 => "schedule",
            VirtualKey.Number5 or VirtualKey.NumberPad5 => "office",
            VirtualKey.Number6 or VirtualKey.NumberPad6 => "contracts",
            VirtualKey.Number7 or VirtualKey.NumberPad7 => "accounts",
            VirtualKey.Number8 or VirtualKey.NumberPad8 => "stores",
            VirtualKey.Number9 or VirtualKey.NumberPad9 => "outputs",
            _ => null
        };
        if (tab is null) return;
        SelectTab(tab);
        e.Handled = true;
    }

    private void BuildRibbonModel()
    {
        _tabs.Clear();
        _tabs.Add(new RibbonTab("project", "Project", new[]
        {
            Cmd("dashboard", "Dashboard", Glyph("\uE80F")),
            Cmd("levels", "Levels", Glyph("\uF158")),
            Cmd("takeoff", "Take-off (O-Draft)", Glyph("\uE8A5")),
        }));
        _tabs.Add(new RibbonTab("rcc", "RCC steel", new[]
        {
            Cmd("columns", "Columns", NavIcon("columns")),
            Cmd("beams", "Beams", NavIcon("beams")),
            Cmd("pedestals", "Pedestals", NavIcon("pedestals")),
            Cmd("lintels", "Lintels", NavIcon("lintels")),
            Cmd("slabs", "Slabs", NavIcon("slabs")),
            Cmd("footings", "Footings", NavIcon("footings")),
            Cmd("walls", "Retaining walls", NavIcon("walls")),
            Cmd("stairs", "Stairs", NavIcon("stairs")),
        }));
        _tabs.Add(new RibbonTab("civil", "Civil BOQ", new[]
        {
            Cmd("masonry", "Masonry walls", NavIcon("masonry")),
            Cmd("plaster", "Plastering", NavIcon("plaster")),
            Cmd("pcc", "PCC bed", NavIcon("pcc")),
            Cmd("earthwork", "Earthwork", NavIcon("earthwork")),
            Cmd("ssm", "Size stone", NavIcon("ssm")),
            Cmd("shuttering", "Shuttering", NavIcon("shuttering")),
            Cmd("flooring", "Flooring", NavIcon("flooring")),
            Cmd("painting", "Painting", NavIcon("painting")),
            Cmd("waterproofing", "Waterproofing", NavIcon("waterproofing")),
            Cmd("dpc", "DPC", NavIcon("dpc")),
            Cmd("coping", "Coping", NavIcon("coping")),
            Cmd("screed", "Screed", NavIcon("screed")),
            Cmd("vdf", "VDF", NavIcon("vdf")),
            Cmd("skirting", "Skirting", NavIcon("skirting")),
            Cmd("parapet", "Parapet", NavIcon("parapet")),
            Cmd("plinth_protection", "Plinth protection", NavIcon("plinth_protection")),
            Cmd("doors", "Doors", NavIcon("doors")),
            Cmd("windows", "Windows", NavIcon("windows")),
        }));
        // Unique Segoe Fluent glyphs per destination (no shared glyph across unrelated pages).
        _tabs.Add(new RibbonTab("schedule", "Schedule", new[]
        {
            Cmd("schedule_activities", "Activities", Glyph("\uE8FD")), // CalendarDay
            Cmd("schedule_network", "Network", Glyph("\uE774")),       // Globe
            Cmd("schedule_gantt", "Gantt", Glyph("\uE787")),           // CalendarWeek
        }));
        _tabs.Add(new RibbonTab("office", "Office", new[]
        {
            Cmd("correspondence", "Correspondence", Glyph("\uE715")), // Mail
        }));
        _tabs.Add(new RibbonTab("contracts", "Contracts", new[]
        {
            Cmd("contracts_list", "Contracts", Glyph("\uE8C8")),       // PageList
            Cmd("contracts_rates", "Rate schedule", Glyph("\uE8CB")),  // BulletedList2
            Cmd("contracts_terms", "Terms", Glyph("\uE90F")),          // Page
        }));
        _tabs.Add(new RibbonTab("accounts", "Accounts", new[]
        {
            Cmd("accounts_bills", "Running bills", Glyph("\uE8C9")),   // DockLeft (bill strip)
            Cmd("accounts_cash", "Cash book", Glyph("\uE8EF")),        // Money
            Cmd("accounts_ledger", "Ledger", Glyph("\uE8F1")),         // Dictionary
        }));
        _tabs.Add(new RibbonTab("stores", "Stores", new[]
        {
            Cmd("stores_po", "Purchase orders", Glyph("\uE7BF")),      // Shop
            Cmd("stores_grn", "Goods receipt", Glyph("\uE7B8")),       // Package
            Cmd("stores_inventory", "Inventory", Glyph("\uE81E")),     // ViewAll / boxes
            Cmd("stores_masters", "Masters", Glyph("\uE8B7")),         // Repair
        }));
        _tabs.Add(new RibbonTab("hr", "HR & resources", new[]
        {
            Cmd("org_sites", "Sites", Glyph("\uE707")),                // MapPin
            Cmd("org_resources", "Resources", Glyph("\uE8F2")),        // OpenFile
            Cmd("org_employees", "Employees", Glyph("\uE716")),        // Contact
            Cmd("org_payroll", "Payroll", Glyph("\uE8EA")),            // PaymentCard
        }));
        _tabs.Add(new RibbonTab("outputs", "Outputs", new[]
        {
            Cmd("quantities", "Quantities", Glyph("\uE9D2")),          // Switch
            Cmd("po", "Purchase orders", Glyph("\uE719")),             // Shop (distinct from stores PO)
            Cmd("estimate", "Estimate", Glyph("\uE8BC")),              // Work
            Cmd("links", "Item links", Glyph("\uE71B")),               // Link
            Cmd("datamodel", "Data model", Glyph("\uE8AB")),           // MapLayers
            Cmd("ratebook", "Rate book", Glyph("\uE82D")),             // Bookmarks
            Cmd("report", "Report", Glyph("\uE9F9")),                  // ReportDocument
        }));

        _navTags.Clear();
        foreach (var cmd in _tabs.SelectMany(t => t.Commands))
            _navTags.Add(cmd.Tag);
        // Edit menu + sync actions (not duplicated in Outputs strip).
        foreach (var tag in new[] { "settings_project", "settings_engineering", "settings_cost" })
            _navTags.Add(tag);
    }

    private static NavCmd Cmd(string tag, string label, object? icon) => new(tag, label, icon);

    /// <summary>SVG theme icon, or 1–2 letter badge when the asset is missing.</summary>
    private static FrameworkElement NavIcon(string tag)
    {
        string? key = tag.ToLowerInvariant() switch
        {
            "columns" or "column" => "NavIconColumn",
            "beams" or "beam" => "NavIconBeam",
            "pedestals" or "pedestal" => "NavIconPedestal",
            "lintels" or "lintel" => "NavIconLintel",
            "slabs" or "slab" => "NavIconSlab",
            "footings" or "footing" => "NavIconFooting",
            "walls" or "wall" => "NavIconWall",
            "stairs" or "stair" => "NavIconStair",
            "masonry" => "NavIconMasonry",
            "plaster" => "NavIconPlaster",
            "pcc" => "NavIconPcc",
            "earthwork" => "NavIconEarthwork",
            "ssm" => "NavIconSsm",
            "shuttering" => "NavIconShuttering",
            "flooring" => "NavIconFlooring",
            "painting" => "NavIconPainting",
            "waterproofing" => "NavIconWaterproofing",
            "dpc" => "NavIconDpc",
            "coping" => "NavIconCoping",
            "screed" => "NavIconScreed",
            "vdf" => "NavIconVdf",
            "skirting" => "NavIconSkirting",
            "parapet" => "NavIconParapet",
            "plinth_protection" or "plinth" => "NavIconPlinth",
            "doors" or "door" => "NavIconDoor",
            "windows" or "window" => "NavIconWindow",
            _ => null
        };
        if (key is not null && ThemeIcon(key) is { } img)
            return img;
        return LetterIcon(LetterForTag(tag));
    }

    private static string LetterForTag(string tag) => tag.ToLowerInvariant() switch
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
        "doors" or "door" => "DR",
        "windows" or "window" => "WN",
        "estimate" => "ES",
        "links" => "LK",
        "datamodel" => "DM",
        "ratebook" => "RB",
        _ => tag.Length >= 2 ? tag[..2].ToUpperInvariant() : tag.ToUpperInvariant()
    };

    private static FrameworkElement LetterIcon(string letters, double size = 16)
    {
        string text = letters.Length > 2 ? letters[..2] : letters;
        return new Border
        {
            Width = size + 6,
            Height = size + 6,
            CornerRadius = new CornerRadius(2),
            Background = BrushRes("HcwSoftBrush", Color.FromArgb(255, 0xEC, 0xEE, 0xF2)),
            BorderBrush = BrushRes("HcwEdgeBrush", Color.FromArgb(255, 0xD0, 0xD4, 0xDC)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = text.Length > 1 ? size * 0.55 : size * 0.7,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = BrushRes("HcwInkBrush", Color.FromArgb(255, 0x14, 0x15, 0x17)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            }
        };
    }

    private static ImageIcon? ThemeIcon(string key)
    {
        if (!Application.Current.Resources.TryGetValue(key, out var src) || src is null)
            return null;
        return new ImageIcon { Source = (ImageSource)src, Width = SuiteIconPx, Height = SuiteIconPx };
    }

    /// <summary>Suite icon glyph size (10) and tile hit (36) — matches Hcw.Ui / O-Draft.</summary>
    private const double SuiteIconPx = 15;
    private const double SuiteTilePx = 36;

    private bool _navSyncing;

    /// <summary>Builds the left navigation pane from the sections: a heading for each, and an item for each page.</summary>
    private void BuildNavigation()
    {
        AppNav.MenuItems.Clear();
        var first = true;
        foreach (var tab in _tabs)
        {
            if (!first)
                AppNav.MenuItems.Add(new NavigationViewItemSeparator());
            first = false;
            AppNav.MenuItems.Add(new NavigationViewItemHeader { Content = tab.Title, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var cmd in tab.Commands)
            {
                var icon = cmd.Icon switch
                {
                    FontIcon fi => (IconElement)CloneFontIcon(fi, SuiteIconPx),
                    ImageIcon ii => new ImageIcon { Source = ii.Source, Width = SuiteIconPx, Height = SuiteIconPx },
                    _ => new FontIcon { Glyph = "\uE7C3", FontSize = SuiteIconPx, FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"] },
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                AppNav.MenuItems.Add(new NavigationViewItem
                {
                    Content = cmd.Label,
                    Tag = cmd.Tag,
                    MinHeight = SuiteTilePx,
                    FontSize = 13,
                    Icon = icon,
                });
            }
        }
    }

    // In the narrow layout the pane's menu button sits at the top left, so the page line moves clear of it.
    private void AppNav_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        var minimal = sender.DisplayMode == NavigationViewDisplayMode.Minimal;
        PageLine.Margin = new Thickness(minimal ? 56 : 24, 12, 24, 4);
    }

    private void AppNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_navSyncing || args.SelectedItem is not NavigationViewItem { Tag: string tag })
            return;
        Navigate(tag);
        // an action such as Pull JM is not a page: put the highlight back on the page being shown
        RefreshCommandHighlight();
    }

    private void SelectTab(string id)
    {
        _activeTab = id;
        var first = _tabs.FirstOrDefault(t => t.Id == id)?.Commands.FirstOrDefault();
        if (first is not null)
            Navigate(first.Tag);
    }

    private static FontIcon CloneFontIcon(FontIcon src, double size = SuiteIconPx) => new()
    {
        Glyph = src.Glyph,
        FontSize = size,
        FontFamily = src.FontFamily
    };

    private static FrameworkElement CloneFrameworkIcon(FrameworkElement src, double size = 20)
    {
        if (src is ImageIcon ii)
            return new ImageIcon { Source = ii.Source, Width = size, Height = size };
        if (src is Border)
            return LetterIcon(
                src is Border { Child: TextBlock tb } ? tb.Text : "?",
                size);
        return LetterIcon("?", size);
    }

    private static FontIcon Glyph(string g, double size = SuiteIconPx) => new()
    {
        Glyph = g,
        FontSize = size,
        FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"]
    };

    private static double ResourceDouble(string key, double fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is double d)
            return d;
        return fallback;
    }

    // ——— IAppCommandBus: the single external route into navigation ———

    /// <summary>Canonical navigable ribbon tags, in ribbon order.</summary>
    public IReadOnlyList<string> KnownTags =>
        _tabs.SelectMany(t => t.Commands).Select(c => c.Tag).Distinct().ToArray();

    /// <summary>
    /// Navigate to a ribbon command tag. Returns <c>false</c> and does nothing for
    /// an unknown tag, instead of falling through to the dashboard, so callers can
    /// tell whether the request was understood.
    /// </summary>
    public bool Navigate(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var norm = tag.Trim();
        if (!_navTags.Contains(norm)) return false;
        NavigateTo(norm);
        return true;
    }

    private void NavigateTo(string tag)
    {
        _activeTag = tag;
        var owner = _tabs.FirstOrDefault(t => t.Commands.Any(c => c.Tag == tag));
        if (owner is not null)
            _activeTab = owner.Id;
        RefreshCommandHighlight();

        ContentFrame.Content = tag switch
        {
            "dashboard" => new DashboardPage(),
            "levels" => new LevelsPage(),
            "columns" => new ElementPage(ElementSpecs.Columns(), ProjectStore.Current.Columns),
            "beams" => new ElementPage(ElementSpecs.Beams(), ProjectStore.Current.Beams),
            "pedestals" => new ElementPage(ElementSpecs.Pedestals(), ProjectStore.Current.Pedestals),
            "lintels" => new ElementPage(ElementSpecs.Lintels(), ProjectStore.Current.Lintels),
            "slabs" => new ElementPage(ElementSpecs.Slabs(), ProjectStore.Current.Slabs),
            "footings" => new ElementPage(ElementSpecs.Footings(), ProjectStore.Current.Footings),
            "walls" => new ElementPage(ElementSpecs.Walls(), ProjectStore.Current.Walls),
            "stairs" => new ElementPage(ElementSpecs.Stairs(), ProjectStore.Current.Stairs),
            "takeoff" => new TakeoffPage(),
            "masonry" => new ElementPage(ElementSpecs.MasonryWalls(), ProjectStore.Current.MasonryWalls),
            "plaster" => new ElementPage(ElementSpecs.Plaster(), ProjectStore.Current.FinishPropose),
            "pcc" => new ElementPage(ElementSpecs.PccBeds(), ProjectStore.Current.PccBeds),
            "earthwork" => new ElementPage(ElementSpecs.Earthwork(), ProjectStore.Current.Earthwork),
            "ssm" => new ElementPage(ElementSpecs.SizeStone(), ProjectStore.Current.SizeStone),
            "shuttering" => new ElementPage(ElementSpecs.Shuttering(), ProjectStore.Current.Shuttering),
            "flooring" => new ElementPage(ElementSpecs.Flooring(), ProjectStore.Current.Flooring),
            "painting" => new ElementPage(ElementSpecs.Painting(), ProjectStore.Current.Painting),
            "waterproofing" => new ElementPage(ElementSpecs.Waterproofing(), ProjectStore.Current.Waterproofing),
            "dpc" => new ElementPage(ElementSpecs.Dpc(), ProjectStore.Current.Dpc),
            "coping" => new ElementPage(ElementSpecs.Coping(), ProjectStore.Current.Coping),
            "screed" => new ElementPage(ElementSpecs.Screed(), ProjectStore.Current.Screed),
            "vdf" => new ElementPage(ElementSpecs.Vdf(), ProjectStore.Current.Vdf),
            "skirting" => new ElementPage(ElementSpecs.Skirting(), ProjectStore.Current.Skirting),
            "parapet" => new ElementPage(ElementSpecs.Parapet(), ProjectStore.Current.Parapet),
            "plinth_protection" => new ElementPage(ElementSpecs.PlinthProtection(), ProjectStore.Current.PlinthProtection),
            "doors" => new ElementPage(ElementSpecs.Doors(), ProjectStore.Current.Doors),
            "windows" => new ElementPage(ElementSpecs.Windows(), ProjectStore.Current.Windows),
            "schedule" or "schedule_activities" => new SchedulePage(SchedulePage.ScheduleTab.Activities),
            "schedule_network" => new SchedulePage(SchedulePage.ScheduleTab.Network),
            "schedule_gantt" => new SchedulePage(SchedulePage.ScheduleTab.Gantt),
            "office" or "correspondence" => new CorrespondencePage(),
            "contracts" or "contracts_list" => new ContractsPage(ContractsPage.ContractsTab.Contracts),
            "contracts_rates" => new ContractsPage(ContractsPage.ContractsTab.Rates),
            "contracts_terms" => new ContractsPage(ContractsPage.ContractsTab.Terms),
            "accounts" or "accounts_bills" => new AccountsPage(AccountsPage.AccountsTab.Bills),
            "accounts_cash" => new AccountsPage(AccountsPage.AccountsTab.Cash),
            "accounts_ledger" => new AccountsPage(AccountsPage.AccountsTab.Ledger),
            "stores" or "stores_po" => new StoresPage(StoresPage.StoresTab.Orders),
            "stores_grn" => new StoresPage(StoresPage.StoresTab.Grn),
            "stores_inventory" => new StoresPage(StoresPage.StoresTab.Inventory),
            "stores_masters" => new StoresPage(StoresPage.StoresTab.Masters),
            "hr" or "org_sites" => new OrgPage(OrgPage.OrgTab.Sites),
            "org_resources" => new OrgPage(OrgPage.OrgTab.Resources),
            "org_employees" => new OrgPage(OrgPage.OrgTab.Employees),
            "org_payroll" => new OrgPage(OrgPage.OrgTab.Payroll),
            "quantities" => new QuantitiesPage(),
            "po" => new PurchaseOrderPage(),
            "estimate" => new EstimatePage(),
            "links" => new LinkRulesPage(),
            "datamodel" => new DataModelPage(),
            "ratebook" => new RateBookPage(),
            "report" => new ReportPage(),
            "settings" or "settings_project" => new SettingsPage(SettingsPage.SettingsTab.Project),
            "settings_engineering" => new SettingsPage(SettingsPage.SettingsTab.Engineering),
            "settings_cost" => new SettingsPage(SettingsPage.SettingsTab.CostPercent),
            _ => new DashboardPage()
        };

        var label = owner?.Commands.FirstOrDefault(c => c.Tag == tag)?.Label ?? tag;
        if (RibbonPageLabel != null) RibbonPageLabel.Text = label;
    }

    /// <summary>Marks the navigation item of the page being shown, without navigating again.</summary>
    private void RefreshCommandHighlight()
    {
        _navSyncing = true;
        try
        {
            AppNav.SelectedItem = AppNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => string.Equals(i.Tag as string, _activeTag, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _navSyncing = false;
        }
    }

    static Brush BrushRes(string key, Color fallback)
    {
        var themed = Hcw.Ui.SuiteAppearance.TryBrush(key);
        if (themed != null)
            return themed;
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b)
            return b;
        return new SolidColorBrush(fallback);
    }

    private void OnAppNotify(NotifyRequest req)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _toastGeneration++;
            int gen = _toastGeneration;
            ToastBar.Title = req.Title;
            ToastBar.Message = req.Message;
            ToastBar.Severity = req.Severity;
            ToastHost.Visibility = Visibility.Visible;
            ToastBar.IsOpen = true;

            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1200, req.DurationMs)) };
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                if (gen == _toastGeneration)
                    CloseToast();
            };
            _toastTimer.Start();
        });
    }

    private void ToastBar_CloseClick(InfoBar sender, object args)
    {
        _toastGeneration++;
        _toastTimer?.Stop();
        CloseToast();
    }

    private void CloseToast()
    {
        ToastBar.IsOpen = false;
        ToastHost.Visibility = Visibility.Collapsed;
    }

    private void TrySetAppIcon()
    {
        try
        {
            var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(ico))
                AppWindow.SetIcon(ico);
        }
        catch
        {
            // Icon is optional for unpackaged runs.
        }
    }

    private void CalcFab_Click(object sender, RoutedEventArgs e)
    {
        var open = FloatingCalcPanel.Visibility != Visibility.Visible;
        FloatingCalcPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open)
            CalcInput.Focus(FocusState.Programmatic);
    }

    private void CalcClose_Click(object sender, RoutedEventArgs e)
    {
        FloatingCalcPanel.Visibility = Visibility.Collapsed;
        NavCalcBtn?.Focus(FocusState.Programmatic);
    }

    private void CalcInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            string text = (CalcInput.Text ?? "").Trim();
            // Arithmetic stays the instant path; anything else goes to the assistant.
            if (QuickCalc.TryEvaluate(text, out var result))
            {
                CalcResult.Text = result;
                CalcResult.Focus(FocusState.Programmatic);
                CalcResult.SelectAll();
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                _ = AskAssistantAsync(text);
            }
        }
        else if (e.Key == VirtualKey.Escape)
        {
            if (!string.IsNullOrEmpty(CalcInput.Text) || !string.IsNullOrEmpty(CalcResult.Text)
                || AssistantReplyHost.Visibility == Visibility.Visible)
            {
                CalcInput.Text = "";
                CalcResult.Text = "";
                AssistantReply.Text = "";
                AssistantReplyHost.Visibility = Visibility.Collapsed;
            }
            else
            {
                FloatingCalcPanel.Visibility = Visibility.Collapsed;
                NavCalcBtn?.Focus(FocusState.Programmatic);
            }
            e.Handled = true;
        }
    }

    private async Task AskAssistantAsync(string prompt)
    {
        if (_assistantBusy) return;

        _assistant ??= AssistantService.TryCreate(this, this, DispatcherQueue);
        if (_assistant is null)
        {
            if (await PromptForKeyAsync())
                _assistant = AssistantService.TryCreate(this, this, DispatcherQueue);
            if (_assistant is null)
            {
                ShowAssistantReply("Assistant not enabled — no Anthropic API key provided.");
                return;
            }
        }

        _assistantBusy = true;
        AssistantReply.Text = "";
        AssistantReplyHost.Visibility = Visibility.Visible;
        AssistantBusy.IsActive = true;
        AssistantBusy.Visibility = Visibility.Visible;
        try
        {
            string reply = await _assistant.AskAsync(prompt);
            ShowAssistantReply(string.IsNullOrWhiteSpace(reply) ? "(no reply)" : reply);
        }
        catch (Exception ex) when (LooksLikeAuthError(ex))
        {
            // A stored key was rejected — clear it so the next ask re-prompts.
            AssistantSettings.ApiKey = null;
            _assistant = null;
            ShowAssistantReply("That API key was rejected. Ask again to re-enter it.");
        }
        catch (Exception ex)
        {
            ShowAssistantReply($"Assistant error: {ex.Message}");
        }
        finally
        {
            AssistantBusy.IsActive = false;
            AssistantBusy.Visibility = Visibility.Collapsed;
            _assistantBusy = false;
        }
    }

    private void ShowAssistantReply(string text)
    {
        AssistantReplyHost.Visibility = Visibility.Visible;
        AssistantReply.Text = text;
    }

    // IAssistantConfirm — modal gate for any assistant-driven change to project state.
    public async Task<bool> ConfirmAsync(string title, string details)
    {
        try
        {
            var dlg = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            return await dlg.ShowAsync() == ContentDialogResult.Primary;
        }
        catch
        {
            // e.g. another ContentDialog is already open — treat as declined.
            return false;
        }
    }

    /// <summary>Prompt for and store the Anthropic API key. Returns true if a key was saved.</summary>
    private async Task<bool> PromptForKeyAsync()
    {
        var box = new PasswordBox { PlaceholderText = "sk-ant-…" };
        var panel = new StackPanel { Spacing = 8, MinWidth = 320 };
        panel.Children.Add(new TextBlock
        {
            Text = "Paste your Anthropic API key to enable the assistant. "
                 + "It is stored locally under your Windows user profile.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(box);
        try
        {
            var dlg = new ContentDialog
            {
                Title = "Enable assistant",
                Content = panel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return false;
            var key = box.Password?.Trim();
            if (string.IsNullOrWhiteSpace(key)) return false;
            AssistantSettings.ApiKey = key;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Heuristic: does this exception (or an inner one) look like an API-key/auth failure?
    /// Uses only the exception type name and message so it needs no SDK type references.</summary>
    private static bool LooksLikeAuthError(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            string name = e.GetType().Name;
            if (name.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
                return true;
            string m = e.Message ?? "";
            if (m.Contains("api key", StringComparison.OrdinalIgnoreCase)
                || m.Contains("x-api-key", StringComparison.OrdinalIgnoreCase)
                || m.Contains("authentication", StringComparison.OrdinalIgnoreCase)
                || m.Contains("401"))
                return true;
        }
        return false;
    }

    private void CalcInput_TextChanged(object sender, TextChangedEventArgs e) =>
        EvaluateCalc(selectResult: false);

    private void EvaluateCalc(bool selectResult)
    {
        if (QuickCalc.TryEvaluate(CalcInput.Text, out var result))
        {
            CalcResult.Text = result;
            if (selectResult)
            {
                CalcResult.Focus(FocusState.Programmatic);
                CalcResult.SelectAll();
            }
        }
        else if (string.IsNullOrWhiteSpace(CalcInput.Text))
        {
            CalcResult.Text = "";
        }
        else if (!CalcInput.Text.Any(c => char.IsDigit(c)))
        {
            CalcResult.Text = "";
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void NavMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;
        SelectNavTag(tag);
    }

    private void SelectNavTag(string tag)
    {
        Navigate(tag);
    }

    // ——— Persona badge (read-only; declared under File → Project settings) ———

    private void UpdatePersonaBadge()
    {
        if (PersonaBadgeText is null) return;
        // PersonaBadge chrome removed — status line in top ribbon only.
        var p = ProjectStore.Current.Parties.ActiveParty;
        string who = string.IsNullOrWhiteSpace(p.Company) ? p.Role.Display() : p.Company;
        PersonaBadgeText.Text = $"{p.Role.Display()} · {who}";
    }

    void ApplyBrandingChrome()
    {
        // Product name once in the window title; caption shows the open document. No pane logo.
        Title = Branding.AppName;
        if (TitleBarText != null)
            TitleBarText.Text = ProjectStore.Current.Name;
        PaintTitleBar();
    }

    void PaintTitleBar()
    {
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            return;
        var dark = Hcw.Ui.SuiteAppearance.IsDark;
        var bar = AppWindow.TitleBar;
        Windows.UI.Color Bg(byte r, byte g, byte b) => Windows.UI.Color.FromArgb(255, r, g, b);
        var background = dark ? Bg(0x1B, 0x1B, 0x1B) : Bg(0xE4, 0xEA, 0xF2);
        var foreground = dark ? Bg(0xF3, 0xF3, 0xF3) : Bg(0x1A, 0x23, 0x32);
        var hover = dark ? Bg(0x2F, 0x2F, 0x2F) : Bg(0xF1, 0xF5, 0xFA);
        var pressed = dark ? Bg(0x38, 0x38, 0x38) : Bg(0xD8, 0xDE, 0xE8);
        var inactive = dark ? Bg(0x9A, 0x9A, 0x9A) : Bg(0x4A, 0x56, 0x68);
        bar.BackgroundColor = background;
        bar.ForegroundColor = foreground;
        bar.InactiveBackgroundColor = background;
        bar.InactiveForegroundColor = inactive;
        bar.ButtonBackgroundColor = background;
        bar.ButtonForegroundColor = foreground;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = foreground;
        bar.ButtonInactiveBackgroundColor = background;
        bar.ButtonInactiveForegroundColor = inactive;
    }

    private void SetWindowTitle(string text)
    {
        Title = text;
        // Caption = document name only (avoid repeating "O-CoreOp").
        if (TitleBarText != null)
        {
            var name = ProjectStore.Current.Name;
            if (string.IsNullOrWhiteSpace(name)) name = "Untitled Project";
            TitleBarText.Text = ProjectStore.Current.IsDirty ? $"{name} *" : name;
        }
    }

    private void RefreshWindowTitle() =>
        SetWindowTitle(Branding.WindowTitle(ProjectStore.Current.Name, ProjectStore.Current.IsDirty));


    private async void About_Click(object sender, RoutedEventArgs e)
    {
        var logo = new Border
        {
            Width = 72,
            Height = 72,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Microsoft.UI.Colors.White),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Image
            {
                Source = new SvgImageSource(new Uri("ms-appx:///Assets/mark.svg")),
                Stretch = Stretch.Uniform,
                Margin = new Thickness(8)
            }
        };

        var body = new StackPanel { Spacing = 8, MaxWidth = 360 };
        body.Children.Add(logo);
        body.Children.Add(new TextBlock
        {
            Text = Branding.AppName,
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"],
            HorizontalAlignment = HorizontalAlignment.Center
        });
        body.Children.Add(new TextBlock
        {
            Text = Branding.FullName,
            Style = (Style)Application.Current.Resources["BodyStrongStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });
        body.Children.Add(new TextBlock
        {
            Text = Branding.Tagline,
            Style = (Style)Application.Current.Resources["CaptionSecondaryStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });
        body.Children.Add(new TextBlock
        {
            Text = Branding.DevelopedBy,
            Style = (Style)Application.Current.Resources["BodyStrongStyle"],
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });
        body.Children.Add(new TextBlock
        {
            Text = Branding.Copyright,
            Style = (Style)Application.Current.Resources["MetaTextStyle"],
            HorizontalAlignment = HorizontalAlignment.Center
        });
        body.Children.Add(new TextBlock
        {
            Text = $"Licensed under {Branding.LicenseName}",
            Style = (Style)Application.Current.Resources["MetaTextStyle"],
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var dlg = new ContentDialog
        {
            Title = $"About {Branding.AppName}",
            Content = body,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };
        await dlg.ShowAsync();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        ProjectStore.Current.Reset();
        RefreshWindowTitle();
        UpdatePersonaBadge();
        SelectNavTag("dashboard");
    }

    private async void ImportCad_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        CadImportService.Prepared prep;
        try { prep = CadImportService.Prepare(file.Path); }
        catch (Exception ex)
        {
            AppNotify.Error("Import from CAD", ex.Message);
            return;
        }
        await ShowCadImportPreviewAsync(prep, "Import from CAD");
    }

    /// <summary>The preview dialog for "Import from CAD". Nothing is written before Import.</summary>
    internal async Task ShowCadImportPreviewAsync(CadImportService.Prepared prep, string title)
    {
        var plan = prep.Plan;
        var lines = plan.Items
            .Where(i => i.Action != Aqc.CadImport.PlanAction.Unchanged)
            .Select(i => $"{i.Action,-17} {i.Section,-17} {(i.Mark.Length > 0 ? i.Mark : i.Ref)}{(i.Reason.Length > 0 ? "  - " + i.Reason : "")}")
            .ToList();
        var other = plan.OtherDrawingId is null
            ? ""
            : "\nThis project already holds rows from a different drawing; importing adds this drawing's rows alongside them.\n";
        if (plan.Warnings.Count > 0)
            other += "\nCheck before importing:\n - " + string.Join("\n - ", plan.Warnings) + "\n";
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock
                {
                    Text = plan.Summary + "\n" + other + "\n" + string.Join("\n", lines),
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                },
            },
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = plan.HasChanges,
            XamlRoot = Content.XamlRoot,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        CadImportService.Apply(prep);
        RefreshWindowTitle();
        AppNotify.Success("Import from CAD", plan.Summary);
    }

    private async void ExportCad_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedFileName = ProjectStore.Current.Name.Replace(' ', '_') + ".aqcbridge";
        picker.FileTypeChoices.Add("O-CoreOp bridge file", new List<string> { ".json" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            await System.IO.File.WriteAllTextAsync(file.Path, CadImportService.BuildExport());
        }
        catch (Exception ex)
        {
            AppNotify.Error("Export for CAD failed", ex.Message);
            return;
        }
        AppNotify.Success("Export for CAD", $"{file.Name}: project details, floors above the plinth and beam depths. Apply it in the CAD program.");
    }

    private void UndoCadImport_Click(object sender, RoutedEventArgs e)
    {
        if (!CadImportService.CanUndo) { AppNotify.Warning("Import from CAD", "Nothing to undo."); return; }
        CadImportService.Undo();
        RefreshWindowTitle();
        AppNotify.Success("Import from CAD", "Last CAD import undone.");
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".bbsproj");
        picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var root = EngineClient.LoadProject(file.Path, out var err);
        if (root is null)
        {
            AppNotify.Error("Open failed", err ?? "Could not open project.");
            return;
        }
        ProjectStore.Current.LoadFrom(root);
        ProjectStore.Current.FilePath = file.Path;
        ProjectStore.Current.Shared = null;
        RefreshWindowTitle();
        UpdatePersonaBadge();
        SelectNavTag("dashboard");
        AppNotify.Success("Project opened", ProjectStore.Current.Name);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectStore.Current.Shared is not null)
        {
            _ = SaveToCompanyAsync();
            return;
        }
        if (string.IsNullOrEmpty(ProjectStore.Current.FilePath))
        {
            SaveAs_Click(sender, e);
            return;
        }
        if (!EngineClient.SaveProject(ProjectStore.Current.FilePath, ProjectStore.Current.ToJson(), out var err))
            AppNotify.Error("Save failed", err ?? "Could not save.");
        else
        {
            ProjectStore.Current.IsDirty = false;
            RefreshWindowTitle();
            AppNotify.Success("Saved", Path.GetFileName(ProjectStore.Current.FilePath));
        }
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.SuggestedFileName = ProjectStore.Current.Name.Replace(' ', '_');
        picker.FileTypeChoices.Add(Branding.ProjectFileFilter, new List<string> { Branding.ProjectExtension });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        if (!EngineClient.SaveProject(file.Path, ProjectStore.Current.ToJson(), out var err))
        {
            AppNotify.Error("Save failed", err ?? "Could not save.");
            return;
        }
        ProjectStore.Current.FilePath = file.Path;
        ProjectStore.Current.IsDirty = false;
        RefreshWindowTitle();
        AppNotify.Success("Saved", file.Name);
    }
}
