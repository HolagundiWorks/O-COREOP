// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.IO;
using BBSApp.Controls;
using BBSApp.Services;
using BBSApp.Services.Db;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BBSApp.Views;

public sealed partial class LinkRulesPage : Page
{
    private readonly ResultTable _rulesTable = new();
    private readonly ResultTable _previewTable = new();
    private bool _loading;

    private LinkRuleBook Book => ProjectStore.Current.LinkRules;

    public LinkRulesPage()
    {
        InitializeComponent();
        _rulesTable.SetAutomationName("Link rules");
        _previewTable.SetAutomationName("Derivation preview");
        RulesHost.Child = _rulesTable;
        PreviewHost.Child = _previewTable;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Book.SeedIfEmpty();
        PopulateTradeCombos();
        PopulateBasisCombo();
        RefreshRuleCombo(selectIndex: Book.Rules.Count > 0 ? 0 : -1);
        RefreshRulesTable();
        RefreshPreview();
    }

    // ---- population -------------------------------------------------------

    private void PopulateTradeCombos()
    {
        SourceCombo.Items.Clear();
        TargetCombo.Items.Clear();
        foreach (var t in LinkTradeRegistry.Sources)
        {
            SourceCombo.Items.Add(new ComboBoxItem { Content = t.SourceOnly ? "RCC · " + t.Display : t.Display, Tag = t.Key });
            if (!t.SourceOnly) TargetCombo.Items.Add(new ComboBoxItem { Content = t.Display, Tag = t.Key });
        }
    }

    private void PopulateBasisCombo()
    {
        BasisCombo.Items.Clear();
        foreach (var b in LinkBasisInfo.All)
            BasisCombo.Items.Add(new ComboBoxItem { Content = LinkBasisInfo.Label(b), Tag = LinkBasisInfo.Label(b) });
    }

    private void RefreshRuleCombo(int selectIndex)
    {
        _loading = true;
        RuleCombo.Items.Clear();
        for (int i = 0; i < Book.Rules.Count; i++)
        {
            var r = Book.Rules[i];
            string tick = r.Enabled ? "" : " (off)";
            RuleCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{DisplayName(r)}{tick}",
                Tag = i
            });
        }
        if (selectIndex >= 0 && selectIndex < RuleCombo.Items.Count)
            RuleCombo.SelectedIndex = selectIndex;
        _loading = false;
        LoadEditor();
    }

    private static string DisplayName(LinkRule r) =>
        string.IsNullOrWhiteSpace(r.Name)
            ? $"{LinkTradeRegistry.Display(r.SourceTrade)} → {LinkTradeRegistry.Display(r.TargetTrade)}"
            : r.Name;

    // ---- editor <-> model -------------------------------------------------

    private LinkRule? Selected =>
        RuleCombo.SelectedItem is ComboBoxItem { Tag: int i } && i >= 0 && i < Book.Rules.Count
            ? Book.Rules[i]
            : null;

    private void RuleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        LoadEditor();
    }

    private void LoadEditor()
    {
        var r = Selected;
        _loading = true;
        if (r is null)
        {
            NameBox.Text = "";
            NotesBox.Text = "";
            RateCodeBox.Text = "";
            RateBox.Value = double.NaN;
            FactorBox.Value = 1;
            UnitBox.Text = "";
            EnabledToggle.IsOn = true;
            PerItemCheck.IsChecked = true;
            EquationText.Text = "";
            _loading = false;
            return;
        }
        NameBox.Text = r.Name;
        NotesBox.Text = r.Notes;
        RateCodeBox.Text = r.RateCodeOverride;
        RateBox.Value = r.RateOverride > 0 ? r.RateOverride : double.NaN;
        FactorBox.Value = r.Factor;
        UnitBox.Text = r.TargetUnit;
        EnabledToggle.IsOn = r.Enabled;
        PerItemCheck.IsChecked = r.PerItem;
        SelectByTag(SourceCombo, r.SourceTrade);
        SelectByTag(TargetCombo, r.TargetTrade);
        SelectByTag(BasisCombo, LinkBasisInfo.Label(r.Basis));
        _loading = false;
        UpdateEquation();
    }

    private void ApplyRule_Click(object sender, RoutedEventArgs e)
    {
        var r = Selected;
        if (r is null) { Warn("No rule", "Add a rule first."); return; }

        r.Name = NameBox.Text?.Trim() ?? "";
        r.Notes = NotesBox.Text?.Trim() ?? "";
        r.RateCodeOverride = RateCodeBox.Text?.Trim() ?? "";
        r.RateOverride = double.IsNaN(RateBox.Value) || RateBox.Value < 0 ? 0 : RateBox.Value;
        r.SourceTrade = TagOf(SourceCombo) ?? r.SourceTrade;
        r.TargetTrade = TagOf(TargetCombo) ?? r.TargetTrade;
        r.Basis = LinkBasisInfo.Parse(TagOf(BasisCombo));
        r.Factor = double.IsNaN(FactorBox.Value) ? 0 : FactorBox.Value;
        r.TargetUnit = string.IsNullOrWhiteSpace(UnitBox.Text) ? LinkBasisInfo.Unit(r.Basis) : UnitBox.Text.Trim();
        r.Enabled = EnabledToggle.IsOn;
        r.PerItem = PerItemCheck.IsChecked == true;

        int idx = Book.Rules.IndexOf(r);
        ProjectStore.Current.Notify();
        RefreshRuleCombo(idx);
        RefreshRulesTable();
        RefreshPreview();
        Success("Applied", $"{DisplayName(r)} updated. Refresh preview reflects the change.");
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var r = new LinkRule
        {
            Name = "New link",
            SourceTrade = "Masonry",
            TargetTrade = "Plaster",
            Basis = LinkBasis.Area,
            Factor = 1.0,
            TargetUnit = "m²",
            PerItem = true,
            Enabled = true
        };
        Book.Rules.Add(r);
        ProjectStore.Current.Notify();
        RefreshRuleCombo(Book.Rules.Count - 1);
        RefreshRulesTable();
        Success("Added", "New rule created — set source, target, basis and factor, then Apply.");
    }

    private async void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        var r = Selected;
        if (r is null) return;
        var dlg = new ContentDialog
        {
            Title = "Delete rule?",
            Content = $"Remove “{DisplayName(r)}”? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        Book.Rules.Remove(r);
        ProjectStore.Current.Notify();
        RefreshRuleCombo(Book.Rules.Count > 0 ? 0 : -1);
        RefreshRulesTable();
        RefreshPreview();
        Success("Deleted", "Rule removed.");
    }

    private async void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            Title = "Reset link rules?",
            Content = "Replace all rules with the standard India-practice library. Your custom rules will be lost.",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        Book.ResetToDefaults();
        ProjectStore.Current.Notify();
        RefreshRuleCombo(Book.Rules.Count > 0 ? 0 : -1);
        RefreshRulesTable();
        RefreshPreview();
        Success("Reset", $"{Book.Rules.Count} standard rules restored.");
    }

    private void RefreshPreview_Click(object sender, RoutedEventArgs e)
    {
        RefreshPreview();
        MainPivot.SelectedIndex = 1;
    }

    private void ExportSchema_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = System.IO.Path.Combine(Branding.AppDataDirectory, "schema");
            var (dbml, _) = SchemaExport.WriteFiles(dir);
            Success("ERD exported",
                $"aqc-core.dbml + .sql written to {System.IO.Path.GetDirectoryName(dbml)}. "
                + "Open the .dbml in dbdiagram.io (New Diagram → Import) to draw it.");
        }
        catch (System.Exception ex)
        {
            Warn("Export failed", ex.Message);
        }
    }

    private async void ApplyToSheets_Click(object sender, RoutedEventArgs e)
    {
        var items = DerivationEngine.Preview(ProjectStore.Current, Book);
        var totals = DerivationEngine.Totals(items);
        if (totals.Count == 0)
        {
            Warn("Nothing to apply", "No linked quantities yet — add take-off, then Refresh preview.");
            return;
        }

        RateBookStore.Current.EnsureLoaded();
        var version = RateBookStore.Current.ActiveOrFirst();
        var (costTotal, missing) = DerivationEngine.Price(items, version);

        string list = string.Join("\n", totals.Select(t =>
            $"  •  {t.Trade}   {t.Qty.ToString("0.##", CultureInfo.InvariantCulture)} {t.Unit}   ({t.Lines})"));
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "These linked quantities will be written into the take-off sheets, so they flow into the BOQ and estimate:",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock { Text = list, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") });
        if (version is not null)
        {
            string costLine = $"Derived cost ≈ ₹{costTotal.ToString("N2", CultureInfo.InvariantCulture)} at {version.Name} rates"
                            + (missing.Count > 0 ? $"  ({missing.Count} code(s) unpriced: {string.Join(", ", missing)})" : "");
            panel.Children.Add(new TextBlock { Text = costLine, TextWrapping = TextWrapping.Wrap });
        }
        panel.Children.Add(new TextBlock
        {
            Text = "Previously applied link rows are replaced. If a target trade is also measured directly or via Finishes reconcile (plaster, paint), applying here adds on top — use one route, not both.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        });

        var dlg = new ContentDialog
        {
            Title = "Apply linked items?",
            Content = panel,
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        int n = DerivationEngine.Apply(ProjectStore.Current, Book);
        RefreshPreview();
        Success("Applied", $"{n} linked row(s) written to take-off sheets — now in Quantities / Estimate. Use Clear applied to undo.");
    }

    private void ClearApplied_Click(object sender, RoutedEventArgs e)
    {
        int n = DerivationEngine.ClearApplied(ProjectStore.Current);
        RefreshPreview();
        if (n > 0) Success("Cleared", $"Removed {n} applied link row(s) from the take-off sheets.");
        else Warn("Nothing to clear", "No applied link rows found.");
    }

    private void BasisCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        // Offer the basis' natural unit if the unit box is empty or still a bare default.
        var basis = LinkBasisInfo.Parse(TagOf(BasisCombo));
        if (string.IsNullOrWhiteSpace(UnitBox.Text))
            UnitBox.Text = LinkBasisInfo.Unit(basis);
        UpdateEquation();
    }

    private void UpdateEquation()
    {
        string src = LinkTradeRegistry.Display(TagOf(SourceCombo));
        string tgt = LinkTradeRegistry.Display(TagOf(TargetCombo));
        var basis = LinkBasisInfo.Parse(TagOf(BasisCombo));
        double f = double.IsNaN(FactorBox.Value) ? 0 : FactorBox.Value;
        string unit = string.IsNullOrWhiteSpace(UnitBox.Text) ? LinkBasisInfo.Unit(basis) : UnitBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(tgt)) { EquationText.Text = ""; return; }
        EquationText.Text =
            $"{tgt} = {f.ToString("0.###", CultureInfo.InvariantCulture)} × {src} {LinkBasisInfo.Label(basis).ToLowerInvariant()} ({unit})";
    }

    // ---- tables -----------------------------------------------------------

    private void RefreshRulesTable()
    {
        var rows = Book.Rules.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Enabled ? "On" : "—",
            DisplayName(r),
            LinkTradeRegistry.Display(r.SourceTrade),
            LinkBasisInfo.Label(r.Basis),
            "× " + r.Factor.ToString("0.###", CultureInfo.InvariantCulture),
            LinkTradeRegistry.Display(r.TargetTrade),
            r.TargetUnit,
            r.PerItem ? "per item" : "aggregate"
        }).ToList();

        _rulesTable.SetTable(
            new[] { "On", "Rule", "Source", "Basis", "Factor", "Target", "Unit", "Mode" }, rows);
    }

    private void RefreshPreview()
    {
        var items = DerivationEngine.Preview(ProjectStore.Current, Book);

        RateBookStore.Current.EnsureLoaded();
        var version = RateBookStore.Current.ActiveOrFirst();
        var (costTotal, missing) = DerivationEngine.Price(items, version);

        var rows = items.Select(i => (IReadOnlyList<string>)new[]
        {
            i.RuleName,
            i.SourceTrade + (i.Chained ? " ⭑" : ""),
            i.SourceMark,
            i.Level,
            i.SourceQty.ToString("0.###", CultureInfo.InvariantCulture),
            LinkBasisInfo.Label(i.Basis),
            "× " + i.Factor.ToString("0.###", CultureInfo.InvariantCulture),
            i.TargetTrade,
            i.TargetQty.ToString("0.###", CultureInfo.InvariantCulture),
            i.TargetUnit,
            i.RateCode,
            i.RateMissing ? "—" : i.Rate.ToString("0.00", CultureInfo.InvariantCulture),
            i.RateMissing ? "no rate" : i.Amount.ToString("0.00", CultureInfo.InvariantCulture)
        }).ToList();

        _previewTable.SetTable(
            new[] { "Rule", "Source", "Mark", "Level", "Source qty", "Basis", "Factor", "Target", "Qty", "Unit", "Code", "Rate (₹)", "Amount (₹)" },
            rows);

        var totals = DerivationEngine.Totals(items);
        if (totals.Count == 0)
        {
            TotalsText.Text = "No linked quantities yet — add take-off (masonry, flooring…) so the source trades have measured quantities, then Refresh preview. ⭑ marks a chained link (source produced by another rule).";
        }
        else
        {
            string summary = string.Join("   ·   ",
                totals.Select(t => $"{t.Trade} {t.Qty.ToString("0.##", CultureInfo.InvariantCulture)} {t.Unit} ({t.Lines})"));
            string cost = version is null
                ? (costTotal > 0
                    ? $"  ·  Derived cost ≈ ₹{costTotal.ToString("N2", CultureInfo.InvariantCulture)} (manual rates only)"
                    : "  ·  no rate book to price against")
                : $"  ·  Derived cost ≈ ₹{costTotal.ToString("N2", CultureInfo.InvariantCulture)} ({version.Name})"
                  + (missing.Count > 0 ? $"  ·  {missing.Count} code(s) unpriced: {string.Join(", ", missing)}" : "");
            TotalsText.Text = "Derived totals:  " + summary + cost + "     ⭑ = chained link.";
        }
    }

    // ---- helpers ----------------------------------------------------------

    private static void SelectByTag(ComboBox combo, string? tag)
    {
        if (tag is null) { combo.SelectedIndex = -1; return; }
        foreach (var obj in combo.Items)
            if (obj is ComboBoxItem ci && ci.Tag is string s
                && s.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = ci;
                return;
            }
        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    private static string? TagOf(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag: string s } ? s : null;

    private void ExportDb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Branding.AppDataFolder);
            string safe = string.Join("_",
                (ProjectStore.Current.Name ?? "project").Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safe)) safe = "project";
            string path = Path.Combine(dir, safe + AqcSqliteStore.FileExtension);
            AqcSqliteStore.Export(ProjectStore.Current, path);
            Success("Exported", $"SQLite database written to {path}");
        }
        catch (Exception ex)
        {
            Warn("Export failed", ex.Message);
        }
    }

    private void VerifyDb_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string result = AqcSqliteStore.SelfTest(ProjectStore.Current);
            // SelfTest wipes and re-imports the store — refresh the page off the restored data.
            RefreshRuleCombo(Book.Rules.Count > 0 ? 0 : -1);
            RefreshRulesTable();
            RefreshPreview();
            if (result.StartsWith("Round-trip OK", StringComparison.Ordinal))
                Success("Database round-trip OK", result);
            else
                Warn("Round-trip mismatch", result);
        }
        catch (Exception ex)
        {
            Warn("Verify failed", ex.Message);
        }
    }

    private void Success(string title, string msg) => Show(title, msg, InfoBarSeverity.Success);
    private void Warn(string title, string msg) => Show(title, msg, InfoBarSeverity.Warning);

    private void Show(string title, string msg, InfoBarSeverity sev)
    {
        Info.Title = title;
        Info.Message = msg;
        Info.Severity = sev;
        Info.IsOpen = true;
    }
}
