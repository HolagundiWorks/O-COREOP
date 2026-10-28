// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using BBSApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BBSApp.Views;

public sealed partial class SettingsPage : Page
{
    public enum SettingsTab { Project, Engineering, CostPercent }

    private string _pendingLogoPath = "";
    private readonly SettingsTab _initialTab;

    public SettingsPage(SettingsTab tab = SettingsTab.Project)
    {
        _initialTab = tab;
        InitializeComponent();
        LoadProjectFields();
        LoadEngineeringFields();
        LoadMarkupFields();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        SettingsPivot.SelectedIndex = _initialTab switch
        {
            SettingsTab.Engineering => 1,
            SettingsTab.CostPercent => 2,
            _ => 0
        };
    }

    private void LoadMarkupFields()
    {
        var m = ProjectStore.Current.Markups;
        ElectricalPctBox.Value = m.ElectricalPct;
        PlumbingPctBox.Value = m.PlumbingPct;
        EscalationPctBox.Value = m.EscalationPct;
        ConsultingPctBox.Value = m.ConsultingFeePct;
    }

    /// <summary>
    /// When the project is linked to the company file, company and project identity are kept in Studio.
    /// They are shown here for reference and every report prints them from there.
    /// </summary>
    private void ShowWhereDetailsAreKept(bool linked)
    {
        (TextBox Box, string Header)[] fields =
        [
            (ProjectNameBox, "Project name"),
            (ProjectLocationBox, "Project location"),
            (ClientNameBox, "Client name"),
            (CompanyNameBox, "Company name"),
            (ContactPhoneBox, "Contact number"),
            (ContactEmailBox, "Email"),
            (AddressBox, "Address"),
            (GstinBox, "GSTIN"),
            (PanBox, "PAN"),
        ];
        foreach (var (box, header) in fields)
        {
            box.IsReadOnly = linked;
            box.Header = linked ? $"{header} (change it in Studio)" : header;
        }
        // PM firm fields follow the company file when linked — do not let Settings overwrite them.
        PmCompanyBox.IsReadOnly = linked;
        PmGstinBox.IsReadOnly = linked;
        PmPanBox.IsReadOnly = linked;
        PmCompanyBox.Header = linked ? "Company / firm (from Studio)" : "Company / firm";
    }

    private void LoadProjectFields()
    {
        var info = ProjectStore.Current.Info;
        ProjectNameBox.Text = info.Name;
        ProjectLocationBox.Text = info.Location;
        ClientNameBox.Text = info.ClientName;
        PreparedByRoleBox.SelectedItem = ProjectInfo.PreparedByRoles.Contains(info.PreparedByRole)
            ? info.PreparedByRole
            : "Engineer";
        PreparedByNameBox.Text = info.PreparedByName;
        CompanyNameBox.Text = info.CompanyName;
        ContactPhoneBox.Text = info.ContactPhone;
        ContactEmailBox.Text = info.ContactEmail;
        AddressBox.Text = info.Address;
        GstinBox.Text = info.Gstin;
        CinBox.Text = info.Cin;
        PanBox.Text = info.Pan;
        ShowWhereDetailsAreKept(info.FromCompanyFile);
        _pendingLogoPath = info.LogoPath ?? "";
        RefreshLogoPreview();
        LoadPersonaFields();
    }

    private void LoadPersonaFields()
    {
        var parties = ProjectStore.Current.Parties;
        parties.EnsureDefaults(ProjectStore.Current.Info);
        PersonaActiveBox.SelectedIndex = parties.Active == PartyRole.Contractor ? 1 : 0;

        PmCompanyBox.Text = parties.Pm.Company;
        PmSignNameBox.Text = parties.Pm.SignatoryName;
        PmSignRoleBox.Text = parties.Pm.SignatoryRole;
        PmGstinBox.Text = parties.Pm.Gstin;
        PmPanBox.Text = parties.Pm.Pan;
        PmPrefixBox.Text = parties.Pm.NumberPrefix;

        ConCompanyBox.Text = parties.Contractor.Company;
        ConSignNameBox.Text = parties.Contractor.SignatoryName;
        ConSignRoleBox.Text = parties.Contractor.SignatoryRole;
        ConGstinBox.Text = parties.Contractor.Gstin;
        ConPanBox.Text = parties.Contractor.Pan;
        ConPrefixBox.Text = parties.Contractor.NumberPrefix;
    }

    private void SavePersonaFields()
    {
        var parties = ProjectStore.Current.Parties;
        var linked = ProjectStore.Current.Info.FromCompanyFile;
        parties.Active = (PersonaActiveBox.SelectedItem as ComboBoxItem)?.Tag as string == "contractor"
            ? PartyRole.Contractor
            : PartyRole.PM;

        if (!linked)
        {
            parties.Pm.Company = (PmCompanyBox.Text ?? "").Trim();
            parties.Pm.Gstin = (PmGstinBox.Text ?? "").Trim();
            parties.Pm.Pan = (PmPanBox.Text ?? "").Trim();
        }
        parties.Pm.SignatoryName = (PmSignNameBox.Text ?? "").Trim();
        parties.Pm.SignatoryRole = (PmSignRoleBox.Text ?? "").Trim();
        parties.Pm.NumberPrefix = (PmPrefixBox.Text ?? "").Trim();

        parties.Contractor.Company = (ConCompanyBox.Text ?? "").Trim();
        parties.Contractor.SignatoryName = (ConSignNameBox.Text ?? "").Trim();
        parties.Contractor.SignatoryRole = (ConSignRoleBox.Text ?? "").Trim();
        parties.Contractor.Gstin = (ConGstinBox.Text ?? "").Trim();
        parties.Contractor.Pan = (ConPanBox.Text ?? "").Trim();
        parties.Contractor.NumberPrefix = (ConPrefixBox.Text ?? "").Trim();
    }

    private void LoadEngineeringFields()
    {
        var s = ProjectStore.Current;
        DiaBox.Text = string.Join(", ", s.Diameters);
        HysdBondToggle.IsOn = s.HysdBond;
        HysdFactorBox.Value = s.HysdBondFactor;
        MinHookBox.Value = s.MinHookMm;
        HookBox.Text = FormatMap(s.HookAllowance);
        BendBox.Text = FormatMap(s.BendDeduction);
        var y = s.Yields;
        BricksM3Box.Value = y.BricksPerM3;
        BricksM2Box.Value = y.BricksPerM2Half;
        MortarFracBox.Value = y.MortarFraction;
        DryFactorBox.Value = y.MortarDryFactor;
        WastageBox.Value = y.Wastage;
        ShutterWasteBox.Value = y.ShutteringWastage;
        IgnoreOpenBox.Value = y.IgnoreOpeningBelowM2;
        BeamSlabDeductToggle.IsOn = y.BeamSlabInterfaceDeduct;
        WallFacesBox.Value = y.WallPlasterFaces;
        ColSidesBox.Value = y.DefaultColumnSidesExposed;
        PlasterCeilingToggle.IsOn = y.DefaultPlasterCeiling;
        BeamSoffitToggle.IsOn = y.DefaultBeamSoffit;
        CoverColBox.Value = s.CoverColumnMm;
        CoverBeamBox.Value = s.CoverBeamMm;
        CoverSlabBox.Value = s.CoverSlabMm;
        CoverFootBox.Value = s.CoverFootingMm;
        CoverPedBox.Value = s.CoverPedestalMm;
        CoverLintBox.Value = s.CoverLintelMm;
        ColLapBox.SelectedItem = s.DefaultColumnLap is "Yes" or "No" ? s.DefaultColumnLap : "No";
        BeamLapBox.SelectedItem = s.DefaultBeamLap is "None" or "Tension" ? s.DefaultBeamLap : "None";
    }

    private void RefreshLogoPreview()
    {
        var resolved = ProjectInfo.ResolveLogoFile(_pendingLogoPath);
        if (resolved is null)
        {
            LogoPreview.Source = null;
            LogoPathText.Text = string.IsNullOrWhiteSpace(_pendingLogoPath)
                ? "No logo selected"
                : "Logo file missing — browse again";
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.UriSource = new Uri(resolved);
            LogoPreview.Source = bmp;
            LogoPathText.Text = Path.GetFileName(resolved);
        }
        catch
        {
            LogoPreview.Source = null;
            LogoPathText.Text = "Could not load logo preview";
        }
    }

    private async void BrowseLogo_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow!);
        InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            _pendingLogoPath = await ProjectInfo.ImportLogoAsync(file.Path);
            RefreshLogoPreview();
        }
        catch (Exception ex)
        {
            AppNotify.Error("Logo import failed", ex.Message);
        }
    }

    private void ClearLogo_Click(object sender, RoutedEventArgs e)
    {
        _pendingLogoPath = "";
        RefreshLogoPreview();
    }

    private static string FormatMap(Dictionary<int, double> map) =>
        string.Join(", ", map.OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}:{kv.Value.ToString(CultureInfo.InvariantCulture)}"));

    private static Dictionary<int, double> ParseMap(string text, Dictionary<int, double> fallback)
    {
        var dest = new Dictionary<int, double>();
        foreach (var part in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(':');
            if (bits.Length < 2) continue;
            if (int.TryParse(bits[0].Trim(), out var k) &&
                double.TryParse(bits[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                dest[k] = v;
        }
        if (dest.Count == 0)
            foreach (var kv in fallback) dest[kv.Key] = kv.Value;
        return dest;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var parts = DiaBox.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<int>();
        foreach (var p in parts)
            if (int.TryParse(p.Trim(), out var d) && d > 0) list.Add(d);
        if (list.Count == 0)
        {
            AppNotify.Error("Enter at least one diameter.");
            return;
        }

        var store = ProjectStore.Current;
        var info = store.Info;
        var linked = info.FromCompanyFile;
        var name = (ProjectNameBox.Text ?? "").Trim();
        if (!linked && string.IsNullOrWhiteSpace(name))
        {
            AppNotify.Error("Enter a project name.");
            return;
        }

        // Suite-linked: do not write company/project identity from this page — Studio is the source of truth.
        if (!linked)
        {
            info.Name = name;
            info.Location = (ProjectLocationBox.Text ?? "").Trim();
            info.ClientName = (ClientNameBox.Text ?? "").Trim();
            info.CompanyName = string.IsNullOrWhiteSpace(CompanyNameBox.Text)
                ? Branding.Company
                : CompanyNameBox.Text.Trim();
            info.ContactPhone = (ContactPhoneBox.Text ?? "").Trim();
            info.ContactEmail = (ContactEmailBox.Text ?? "").Trim();
            info.Address = (AddressBox.Text ?? "").Trim();
            info.Gstin = (GstinBox.Text ?? "").Trim();
            info.Pan = (PanBox.Text ?? "").Trim();
        }
        info.PreparedByRole = PreparedByRoleBox.SelectedItem?.ToString() ?? "Engineer";
        info.PreparedByName = (PreparedByNameBox.Text ?? "").Trim();
        info.Cin = (CinBox.Text ?? "").Trim();
        info.LogoPath = _pendingLogoPath ?? "";
        store.Name = info.Name;
        SavePersonaFields();
        if (linked)
            store.Parties.FollowCompanyFile(info);

        store.Diameters.Clear();
        foreach (var d in list.Distinct().OrderBy(x => x))
            store.Diameters.Add(d);

        store.HysdBond = HysdBondToggle.IsOn;
        store.HysdBondFactor = double.IsNaN(HysdFactorBox.Value) || HysdFactorBox.Value <= 0
            ? 1.6 : HysdFactorBox.Value;
        store.MinHookMm = double.IsNaN(MinHookBox.Value) ? 75 : Math.Max(0, MinHookBox.Value);

        store.HookAllowance.Clear();
        foreach (var kv in ParseMap(HookBox.Text, new Dictionary<int, double> { [90] = 9, [135] = 10, [180] = 16 }))
            store.HookAllowance[kv.Key] = kv.Value;

        store.BendDeduction.Clear();
        foreach (var kv in ParseMap(BendBox.Text, new Dictionary<int, double> { [45] = 1, [90] = 2, [135] = 3 }))
            store.BendDeduction[kv.Key] = kv.Value;

        var y = store.Yields;
        y.BricksPerM3 = Val(BricksM3Box.Value, 500);
        y.BricksPerM2Half = Val(BricksM2Box.Value, 55);
        y.MortarFraction = Val(MortarFracBox.Value, 0.30);
        y.MortarDryFactor = Val(DryFactorBox.Value, 1.33);
        y.Wastage = Val(WastageBox.Value, 1.05);
        y.ShutteringWastage = Val(ShutterWasteBox.Value, 1.05);
        y.IgnoreOpeningBelowM2 = Val(IgnoreOpenBox.Value, 0.1);
        y.BeamSlabInterfaceDeduct = BeamSlabDeductToggle.IsOn;
        y.WallPlasterFaces = (int)Math.Clamp(Val(WallFacesBox.Value, 2), 1, 2);
        y.DefaultColumnSidesExposed = (int)Math.Clamp(Val(ColSidesBox.Value, 3), 0, 4);
        y.DefaultPlasterCeiling = PlasterCeilingToggle.IsOn;
        y.DefaultBeamSoffit = BeamSoffitToggle.IsOn;

        store.CoverColumnMm = Val(CoverColBox.Value, 40);
        store.CoverBeamMm = Val(CoverBeamBox.Value, 25);
        store.CoverSlabMm = Val(CoverSlabBox.Value, 20);
        store.CoverFootingMm = Val(CoverFootBox.Value, 50);
        store.CoverPedestalMm = Val(CoverPedBox.Value, 50);
        store.CoverLintelMm = Val(CoverLintBox.Value, 25);
        store.DefaultColumnLap = ColLapBox.SelectedItem?.ToString() ?? "No";
        store.DefaultBeamLap = BeamLapBox.SelectedItem?.ToString() ?? "None";

        var m = store.Markups;
        m.ElectricalPct = Pct(ElectricalPctBox.Value, 8);
        m.PlumbingPct = Pct(PlumbingPctBox.Value, 6);
        m.EscalationPct = Pct(EscalationPctBox.Value, 5);
        m.ConsultingFeePct = Pct(ConsultingPctBox.Value, 3);

        store.Notify();
        AppNotify.Success("Settings saved", "Project, engineering, and cost percentages.");
    }

    private static double Val(double v, double def) =>
        double.IsNaN(v) || v <= 0 ? def : v;

    private static double Pct(double v, double def) =>
        double.IsNaN(v) || v < 0 ? def : v;
}
