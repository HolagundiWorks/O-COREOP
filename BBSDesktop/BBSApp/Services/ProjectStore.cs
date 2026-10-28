// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using BBSApp.Services;

namespace BBSApp.Services;

/// <summary>In-memory project shared across pages.</summary>
public sealed class ProjectStore
{
    public static ProjectStore Current { get; } = new();

    public string Name
    {
        get => Info.Name;
        set => Info.Name = value;
    }
    public string? FilePath { get; set; }

    /// <summary>Set when this project lives in the shared company file; saving then writes there. Not part of the project data.</summary>
    public BBSApp.Services.Company.SharedProjectLink? Shared { get; set; }
    public bool IsDirty { get; set; }

    /// <summary>Project identity: client, company, prepared-by, logo.</summary>
    public ProjectInfo Info { get; } = new();

    /// <summary>The two operating personas (Project Manager / Contractor) + the active one.
    /// Letters, bills, contracts and cash are branded and numbered per persona.</summary>
    public PartyBook Parties { get; } = new();

    /// <summary>Estimate % add-ons: electrical, plumbing, escalation, consulting fees.</summary>
    public EstimateMarkups Markups { get; } = new();

    public ObservableCollection<int> Diameters { get; } = new() { 8, 10, 12, 16, 20, 25, 28, 32, 36, 40 };
    public ObservableCollection<LevelDef> Levels { get; } = new();

    /// <summary>IS 456 Cl. 26.2.1.1 — apply 1.6× τbd for HYSD (Fe415+).</summary>
    public bool HysdBond { get; set; } = true;
    public double HysdBondFactor { get; set; } = 1.6;
    public double MinHookMm { get; set; } = 75;
    /// <summary>Hook cutting allowances (×φ) for 90 / 135 / 180.</summary>
    public Dictionary<int, double> HookAllowance { get; } = new() { [90] = 9, [135] = 10, [180] = 16 };
    /// <summary>Bend deductions (×φ) for 45 / 90 / 135.</summary>
    public Dictionary<int, double> BendDeduction { get; } = new() { [45] = 1, [90] = 2, [135] = 3 };

    public ObservableCollection<Dictionary<string, string>> Columns { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Beams { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Pedestals { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Lintels { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Slabs { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Footings { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Walls { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Stairs { get; } = new();
    // Civil BOQ
    public ObservableCollection<Dictionary<string, string>> MasonryWalls { get; } = new();
    /// <summary>One opening type per line; same wall_mark can repeat. Fields: wall_mark, nos, opening_l, opening_h, level.</summary>
    public ObservableCollection<Dictionary<string, string>> MasonryOpenings { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Plaster { get; } = new();
    /// <summary>Proposed plaster/paint surfaces before Finalize (walls + RCC exposure).</summary>
    public ObservableCollection<Dictionary<string, string>> FinishPropose { get; } = new();
    public ObservableCollection<Dictionary<string, string>> PccBeds { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Earthwork { get; } = new();
    public ObservableCollection<Dictionary<string, string>> SizeStone { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Shuttering { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Flooring { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Painting { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Waterproofing { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Dpc { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Coping { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Screed { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Vdf { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Skirting { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Parapet { get; } = new();
    public ObservableCollection<Dictionary<string, string>> PlinthProtection { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Doors { get; } = new();
    public ObservableCollection<Dictionary<string, string>> Windows { get; } = new();

    public CivilYields Yields { get; } = new();
    public TakeoffState Takeoff { get; } = new();

    /// <summary>CPM/PERT project schedule (activities, dependencies, Gantt).</summary>
    public ProjectSchedule Schedule { get; } = new();

    /// <summary>Correspondence register: letters, memos, certificates, etc. with auto-numbering.</summary>
    public OfficeRegister Office { get; } = new();

    /// <summary>Contracts, work orders, tenders, schedule of rates, and standard terms.</summary>
    public ContractRegister ContractBook { get; } = new();

    /// <summary>Running-account bills, cash/bank transactions, ledgers.</summary>
    public AccountsBook Accounts { get; } = new();

    /// <summary>Procurement & stores: suppliers, warehouses, POs, GRNs, issues, inventory.</summary>
    public StoresBook Stores { get; } = new();

    /// <summary>Sites, resources, employees, attendance and payroll.</summary>
    public OrgBook Org { get; } = new();

    /// <summary>Derivation rules linking trades (masonry→plaster→paint, flooring→skirting …).</summary>
    public LinkRuleBook LinkRules { get; } = new();

    /// <summary>Last calculated estimate snapshot (qty × rates).</summary>
    public EstimateResult? LastEstimate { get; set; }
    public string? LastEstimateRateBookVersionId { get; set; }

    /// <summary>IS 456 Cl. 26.4 nominal covers (mm) by member family.</summary>
    public double CoverColumnMm { get; set; } = 40;
    public double CoverBeamMm { get; set; } = 25;
    public double CoverSlabMm { get; set; } = 20;
    public double CoverFootingMm { get; set; } = 50;
    public double CoverPedestalMm { get; set; } = 50;
    public double CoverLintelMm { get; set; } = 25;
    public string DefaultColumnLap { get; set; } = "No";
    public string DefaultBeamLap { get; set; } = "None";

    public double DefaultCoverMm(string family) => family.ToLowerInvariant() switch
    {
        "column" or "columns" => CoverColumnMm,
        "lintel" or "lintels" => CoverLintelMm,
        "beam" or "beams" => CoverBeamMm,
        "slab" or "slabs" or "stair" or "stairs" => CoverSlabMm,
        "footing" or "footings" or "wall" or "walls" => CoverFootingMm,
        "pedestal" or "pedestals" => CoverPedestalMm,
        _ => CoverBeamMm
    };

    public GenTable? LastSummary { get; set; }
    public GenTable? LastBbs { get; set; }
    public GenTable? LastCivilSummary { get; set; }

    /// <summary>When true, RCC concrete is ordered as RMC by grade — no cement/sand/agg split.</summary>
    public bool ConcreteFromRmc { get; set; } = true;
    public event Action? Changed;

    public void Notify() { IsDirty = true; Changed?.Invoke(); }

    /// <summary>Take-off sheet for a link-rule trade key (see LinkTradeRegistry). Null when not writable
    /// (e.g. Shuttering, which is always rebuilt from RCC). Used by DerivationEngine.Apply.</summary>
    public ObservableCollection<Dictionary<string, string>>? SheetForTrade(string? tradeKey) =>
        string.IsNullOrWhiteSpace(tradeKey) ? null : tradeKey.Trim().ToLowerInvariant() switch
        {
            "masonry" => MasonryWalls,
            "plaster" => Plaster,
            "painting" => Painting,
            "flooring" => Flooring,
            "skirting" => Skirting,
            "screed" => Screed,
            "vdf" => Vdf,
            "waterproofing" => Waterproofing,
            "dpc" => Dpc,
            "coping" => Coping,
            "parapet" => Parapet,
            "pcc" => PccBeds,
            "ssm" => SizeStone,
            "earthwork" => Earthwork,
            "plinth protection" or "plinth_protection" => PlinthProtection,
            "doors" => Doors,
            "windows" => Windows,
            _ => null
        };

    public string[] LevelIds() => Levels.Select(l => l.Id).ToArray();

    public LevelDef? FindLevel(string id) => Levels.FirstOrDefault(l => l.Id == id);

    public double ColumnHeightFor(string levelId)
    {
        var lv = FindLevel(levelId);
        return lv?.ColumnHeightMm ?? 0;
    }

    public void EnsureDefaultLevels()
    {
        if (Levels.Count > 0) return;
        Levels.Add(new LevelDef { Id = "Lvl0", Name = "Plinth", HeightMm = 3200, SlabThicknessMm = 150, BeamDepthMm = 450 });
        Levels.Add(new LevelDef { Id = "Lvl1", Name = "First floor", HeightMm = 3000, SlabThicknessMm = 150, BeamDepthMm = 450 });
    }

    public void RenumberLevels()
    {
        for (int i = 0; i < Levels.Count; i++)
        {
            Levels[i].Id = "Lvl" + i;
            if (i == 0 && string.IsNullOrWhiteSpace(Levels[i].Name)) Levels[i].Name = "Plinth";
        }
    }

    public JsonObject SettingsJson()
    {
        var dias = new JsonArray();
        foreach (var d in Diameters) dias.Add(d);
        var hooks = new JsonObject();
        foreach (var kv in HookAllowance) hooks[kv.Key.ToString(CultureInfo.InvariantCulture)] = kv.Value;
        var bends = new JsonObject();
        foreach (var kv in BendDeduction) bends[kv.Key.ToString(CultureInfo.InvariantCulture)] = kv.Value;
        return new JsonObject
        {
            ["diameters"] = dias,
            ["hook_allowance"] = hooks,
            ["bend_deduction"] = bends,
            ["hysd_bond"] = HysdBond ? 1 : 0,
            ["hysd_bond_factor"] = HysdBondFactor,
            ["min_hook_mm"] = MinHookMm,
            ["covers"] = new JsonObject
            {
                ["column"] = CoverColumnMm,
                ["beam"] = CoverBeamMm,
                ["slab"] = CoverSlabMm,
                ["footing"] = CoverFootingMm,
                ["pedestal"] = CoverPedestalMm,
                ["lintel"] = CoverLintelMm
            },
            ["default_column_lap"] = DefaultColumnLap,
            ["default_beam_lap"] = DefaultBeamLap,
            ["tau_bd"] = new JsonObject
            {
                ["M20"] = 1.2, ["M25"] = 1.4, ["M30"] = 1.5, ["M35"] = 1.7, ["M40"] = 1.9
            },
            ["fy"] = new JsonObject
            {
                ["Fe250"] = 250, ["Fe415"] = 415, ["Fe500"] = 500, ["Fe550"] = 550
            },
            ["civil_yields"] = new JsonObject
            {
                ["bricks_per_m3"] = Yields.BricksPerM3,
                ["bricks_per_m2_half"] = Yields.BricksPerM2Half,
                ["mortar_fraction"] = Yields.MortarFraction,
                ["ssm_mortar_fraction"] = Yields.SsmMortarFraction,
                ["mortar_dry_factor"] = Yields.MortarDryFactor,
                ["wastage"] = Yields.Wastage,
                ["shuttering_wastage"] = Yields.ShutteringWastage,
                ["ignore_opening_below_m2"] = Yields.IgnoreOpeningBelowM2,
                ["beam_slab_interface_deduct"] = Yields.BeamSlabInterfaceDeduct ? 1 : 0,
                ["wall_plaster_faces"] = Yields.WallPlasterFaces,
                ["default_column_sides_exposed"] = Yields.DefaultColumnSidesExposed,
                ["default_plaster_ceiling"] = Yields.DefaultPlasterCeiling ? 1 : 0,
                ["default_beam_soffit"] = Yields.DefaultBeamSoffit ? 1 : 0
            }
        };
    }

    public JsonObject ToJson()
    {
        var levels = new JsonArray();
        foreach (var l in Levels)
        {
            levels.Add(new JsonObject
            {
                ["id"] = l.Id,
                ["name"] = l.Name,
                ["height_mm"] = l.HeightMm,
                ["slab_thickness_mm"] = l.SlabThicknessMm,
                ["beam_depth_mm"] = l.BeamDepthMm
            });
        }
        var root = new JsonObject
        {
            ["format"] = "bbsproj",
            ["version"] = 17,
            ["name"] = Name,
            ["project"] = Info.ToJson(),
            ["parties"] = Parties.ToJson(),
            ["estimate_markups"] = Markups.ToJson(),
            ["concrete_from_rmc"] = ConcreteFromRmc ? 1 : 0,
            ["settings"] = SettingsJson(),
            ["levels"] = levels,
            ["columns"] = RowsToJson(Columns),
            ["beams"] = RowsToJson(Beams),
            ["pedestals"] = RowsToJson(Pedestals),
            ["lintels"] = RowsToJson(Lintels),
            ["slabs"] = RowsToJson(Slabs),
            ["footings"] = RowsToJson(Footings),
            ["walls"] = RowsToJson(Walls),
            ["stairs"] = RowsToJson(Stairs),
            ["masonry"] = RowsToJson(MasonryWalls),
            ["masonry_openings"] = RowsToJson(MasonryOpenings),
            ["plaster"] = RowsToJson(Plaster),
            ["finish_propose"] = RowsToJson(FinishPropose),
            ["pcc"] = RowsToJson(PccBeds),
            ["earthwork"] = RowsToJson(Earthwork),
            ["ssm"] = RowsToJson(SizeStone),
            ["shuttering"] = RowsToJson(Shuttering),
            ["flooring"] = RowsToJson(Flooring),
            ["painting"] = RowsToJson(Painting),
            ["waterproofing"] = RowsToJson(Waterproofing),
            ["dpc"] = RowsToJson(Dpc),
            ["coping"] = RowsToJson(Coping),
            ["screed"] = RowsToJson(Screed),
            ["vdf"] = RowsToJson(Vdf),
            ["skirting"] = RowsToJson(Skirting),
            ["parapet"] = RowsToJson(Parapet),
            ["plinth_protection"] = RowsToJson(PlinthProtection),
            ["doors"] = RowsToJson(Doors),
            ["windows"] = RowsToJson(Windows),
            ["takeoff"] = Takeoff.ToJson(),
            ["schedule"] = Schedule.ToJson(),
            ["office"] = Office.ToJson(),
            ["contracts"] = ContractBook.ToJson(),
            ["accounts"] = Accounts.ToJson(),
            ["stores"] = Stores.ToJson(),
            ["org"] = Org.ToJson(),
            ["link_rules"] = LinkRules.ToJson(),
            ["last_estimate"] = LastEstimate is null ? null : EstimateCalculator.ToJson(LastEstimate),
            ["last_estimate_rate_book_version_id"] = LastEstimateRateBookVersionId ?? ""
        };
        // Suite identity: company file is the system of record (ProjectRef). Present when this project lives there.
        if (Shared is not null && Company.CompanyService.Current is { } company && !string.IsNullOrWhiteSpace(company.Uuid))
            // global:: — Office is also this store's OfficeRegister property.
            root["project_ref"] = global::Office.Shared.ArtifactRefs.ProjectRef(company.Uuid, Shared.ProjectId);
        return root;
    }

    private static JsonArray RowsToJson(IEnumerable<Dictionary<string, string>> rows)
    {
        var arr = new JsonArray();
        foreach (var row in rows)
        {
            var o = new JsonObject();
            foreach (var kv in row) o[kv.Key] = kv.Value;
            arr.Add(o);
        }
        return arr;
    }

    public void LoadFrom(JsonObject root)
    {
        Name = root["name"]?.GetValue<string>() ?? "Untitled Project";
        Info.LoadFrom(root["project"] as JsonObject);
        // Personas load after Info so a legacy project seeds the PM identity from its own company.
        Parties.LoadFrom(root["parties"] as JsonObject, Info);
        Markups.LoadFrom(root["estimate_markups"] as JsonObject);
        // Keep legacy root name in sync if project block missing name
        if (string.IsNullOrWhiteSpace(Info.Name) || Info.Name == "Untitled Project")
            Info.Name = Name;
        else
            Name = Info.Name;
        ConcreteFromRmc = NumRoot(root, "concrete_from_rmc", 1) != 0;
        Columns.Clear(); Beams.Clear(); Pedestals.Clear(); Lintels.Clear();
        Slabs.Clear(); Footings.Clear(); Walls.Clear(); Stairs.Clear();
        MasonryWalls.Clear(); MasonryOpenings.Clear(); Plaster.Clear(); FinishPropose.Clear(); PccBeds.Clear(); Earthwork.Clear(); SizeStone.Clear();
        Shuttering.Clear(); Flooring.Clear(); Painting.Clear();
        Waterproofing.Clear(); Dpc.Clear(); Coping.Clear(); Screed.Clear();
        Vdf.Clear(); Skirting.Clear(); Parapet.Clear(); PlinthProtection.Clear();
        Doors.Clear(); Windows.Clear();
        Levels.Clear();
        LastEstimate = null;
        LastEstimateRateBookVersionId = null;
        LoadRows(root["columns"] as JsonArray, Columns);
        LoadRows(root["beams"] as JsonArray, Beams);
        LoadRows(root["pedestals"] as JsonArray, Pedestals);
        LoadRows(root["lintels"] as JsonArray, Lintels);
        LoadRows(root["slabs"] as JsonArray, Slabs);
        LoadRows(root["footings"] as JsonArray, Footings);
        LoadRows(root["walls"] as JsonArray, Walls);
        LoadRows(root["stairs"] as JsonArray, Stairs);
        LoadRows(root["masonry"] as JsonArray, MasonryWalls);
        foreach (var mw in MasonryWalls) MasonryWallBuild.EnsureWallBuild(mw);
        LoadRows(root["masonry_openings"] as JsonArray, MasonryOpenings);
        MigrateMasonryOpeningsFromWalls();
        LoadRows(root["plaster"] as JsonArray, Plaster);
        LoadRows(root["finish_propose"] as JsonArray, FinishPropose);
        LoadRows(root["pcc"] as JsonArray, PccBeds);
        LoadRows(root["earthwork"] as JsonArray, Earthwork);
        LoadRows(root["ssm"] as JsonArray, SizeStone);
        // Ignore persisted shuttering — always rebuild from RCC concrete members.
        LoadRows(root["flooring"] as JsonArray, Flooring);
        LoadRows(root["painting"] as JsonArray, Painting);
        LoadRows(root["waterproofing"] as JsonArray, Waterproofing);
        LoadRows(root["dpc"] as JsonArray, Dpc);
        LoadRows(root["coping"] as JsonArray, Coping);
        LoadRows(root["screed"] as JsonArray, Screed);
        LoadRows(root["vdf"] as JsonArray, Vdf);
        LoadRows(root["skirting"] as JsonArray, Skirting);
        LoadRows(root["parapet"] as JsonArray, Parapet);
        LoadRows(root["plinth_protection"] as JsonArray, PlinthProtection);
        LoadRows(root["doors"] as JsonArray, Doors);
        LoadRows(root["windows"] as JsonArray, Windows);
        Takeoff.LoadFrom(root["takeoff"] as JsonObject);
        Schedule.LoadFrom(root["schedule"] as JsonObject);
        Office.LoadFrom(root["office"] as JsonObject);
        ContractBook.LoadFrom(root["contracts"] as JsonObject);
        Accounts.LoadFrom(root["accounts"] as JsonObject);
        Stores.LoadFrom(root["stores"] as JsonObject);
        Org.LoadFrom(root["org"] as JsonObject);
        LinkRules.LoadFrom(root["link_rules"] as JsonArray);
        LinkRules.SeedIfEmpty();
        LastEstimate = EstimateCalculator.FromJson(root["last_estimate"] as JsonObject);
        LastEstimateRateBookVersionId = root["last_estimate_rate_book_version_id"]?.GetValue<string>();
        if (root["levels"] is JsonArray la)
        {
            foreach (var item in la)
            {
                if (item is not JsonObject o) continue;
                Levels.Add(new LevelDef
                {
                    Id = o["id"]?.GetValue<string>() ?? "Lvl0",
                    Name = o["name"]?.GetValue<string>() ?? "",
                    HeightMm = Num(o, "height_mm", 3000),
                    SlabThicknessMm = Num(o, "slab_thickness_mm", 150),
                    BeamDepthMm = Num(o, "beam_depth_mm", 450)
                });
            }
        }
        EnsureDefaultLevels();

        Diameters.Clear();
        if (root["settings"]?["diameters"] is JsonArray dias)
        {
            foreach (var d in dias)
                if (d is JsonValue v) Diameters.Add((int)v.GetValue<double>());
        }
        if (Diameters.Count == 0)
            foreach (var d in new[] { 8, 10, 12, 16, 20, 25, 28, 32, 36, 40 }) Diameters.Add(d);

        if (root["settings"] is JsonObject set)
        {
            HysdBond = Num(set, "hysd_bond", 1) != 0;
            HysdBondFactor = Num(set, "hysd_bond_factor", 1.6);
            MinHookMm = Num(set, "min_hook_mm", 75);
            if (set["covers"] is JsonObject cov)
            {
                CoverColumnMm = Num(cov, "column", 40);
                CoverBeamMm = Num(cov, "beam", 25);
                CoverSlabMm = Num(cov, "slab", 20);
                CoverFootingMm = Num(cov, "footing", 50);
                CoverPedestalMm = Num(cov, "pedestal", 50);
                CoverLintelMm = Num(cov, "lintel", 25);
            }
            DefaultColumnLap = set["default_column_lap"]?.GetValue<string>() ?? "No";
            DefaultBeamLap = set["default_beam_lap"]?.GetValue<string>() ?? "None";
            LoadIntMap(set["hook_allowance"] as JsonObject, HookAllowance, new Dictionary<int, double> { [90] = 9, [135] = 10, [180] = 16 });
            LoadIntMap(set["bend_deduction"] as JsonObject, BendDeduction, new Dictionary<int, double> { [45] = 1, [90] = 2, [135] = 3 });
            if (set["civil_yields"] is JsonObject y)
            {
                Yields.BricksPerM3 = Num(y, "bricks_per_m3", 500);
                Yields.BricksPerM2Half = Num(y, "bricks_per_m2_half", 55);
                Yields.MortarFraction = Num(y, "mortar_fraction", 0.30);
                Yields.SsmMortarFraction = Num(y, "ssm_mortar_fraction", 0.30);
                Yields.MortarDryFactor = Num(y, "mortar_dry_factor", 1.33);
                Yields.Wastage = Num(y, "wastage", 1.05);
                Yields.ShutteringWastage = Num(y, "shuttering_wastage", 1.05);
                Yields.IgnoreOpeningBelowM2 = Num(y, "ignore_opening_below_m2", 0.1);
                Yields.BeamSlabInterfaceDeduct = Num(y, "beam_slab_interface_deduct", 0) != 0;
                Yields.WallPlasterFaces = (int)Num(y, "wall_plaster_faces", 2);
                Yields.DefaultColumnSidesExposed = (int)Num(y, "default_column_sides_exposed", 3);
                Yields.DefaultPlasterCeiling = Num(y, "default_plaster_ceiling", 0) != 0;
                Yields.DefaultBeamSoffit = Num(y, "default_beam_soffit", 0) != 0;
            }
        }

        ShutteringCalculator.SyncStore(this);
        if (FinishPropose.Count == 0)
            FinishSurfacesCalculator.SyncPropose(this);
        MigratePedestalsFromColumns();
        IsDirty = false;
        Changed?.Invoke();
    }

    /// <summary>Move legacy opening_*/opening2_* on wall rows into MasonryOpenings (one type per line).</summary>
    private void MigrateMasonryOpeningsFromWalls()
    {
        foreach (var w in MasonryWalls)
        {
            string mark = w.TryGetValue("mark", out var m) ? m : "";
            if (string.IsNullOrWhiteSpace(mark)) continue;
            string level = w.TryGetValue("level", out var lv) ? lv : "Lvl0";

            void Take(string nosKey, string lKey, string hKey)
            {
                if (!w.TryGetValue(nosKey, out var ns) || string.IsNullOrWhiteSpace(ns)) return;
                if (!int.TryParse(ns, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nos) || nos <= 0)
                {
                    // clear legacy even if zero
                }
                else
                {
                    string ol = w.TryGetValue(lKey, out var l) ? l : "0";
                    string oh = w.TryGetValue(hKey, out var h) ? h : "0";
                    bool exists = MasonryOpenings.Any(o =>
                        o.TryGetValue("wall_mark", out var wm) && wm.Equals(mark, StringComparison.OrdinalIgnoreCase)
                        && o.TryGetValue("opening_l", out var xl) && xl == ol
                        && o.TryGetValue("opening_h", out var xh) && xh == oh
                        && o.TryGetValue("nos", out var xn) && xn == nos.ToString(CultureInfo.InvariantCulture));
                    if (!exists)
                    {
                        MasonryOpenings.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["wall_mark"] = mark,
                            ["level"] = level,
                            ["nos"] = nos.ToString(CultureInfo.InvariantCulture),
                            ["opening_l"] = ol,
                            ["opening_h"] = oh
                        });
                    }
                }
                w.Remove(nosKey);
                w.Remove(lKey);
                w.Remove(hKey);
            }

            Take("opening_nos", "opening_l", "opening_h");
            Take("opening2_nos", "opening2_l", "opening2_h");
        }
    }

    /// <summary>One-time: copy embedded column pedestal fields into Pedestals collection.</summary>
    private void MigratePedestalsFromColumns()
    {
        if (Pedestals.Count > 0) return;
        int n = 0;
        foreach (var col in Columns)
        {
            if (!col.TryGetValue("pedestal_h", out var ph) || string.IsNullOrWhiteSpace(ph)) continue;
            if (!double.TryParse(ph, NumberStyles.Float, CultureInfo.InvariantCulture, out var h) || h <= 0) continue;
            n++;
            string mark = col.TryGetValue("mark", out var cm) ? $"P{n}_{cm}" : $"P{n}";
            Pedestals.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mark"] = mark,
                ["nos"] = "1",
                ["level"] = col.TryGetValue("level", out var lv) ? lv : "Lvl0",
                ["column_type"] = "Rectangular",
                ["width"] = col.TryGetValue("pedestal_w", out var pw) ? pw : "600",
                ["depth"] = col.TryGetValue("pedestal_d", out var pd) ? pd : "600",
                ["height"] = ph,
                ["cover"] = CoverPedestalMm.ToString("0", CultureInfo.InvariantCulture),
                ["concrete_grade"] = col.TryGetValue("concrete_grade", out var cg) ? cg : "M25",
                ["steel_grade"] = col.TryGetValue("steel_grade", out var sg) ? sg : "Fe500",
                ["stirrup_dia"] = col.TryGetValue("pedestal_stirrup_dia", out var sd) ? sd : "8",
                ["spacing"] = col.TryGetValue("pedestal_spacing", out var ps) ? ps : "150",
                ["hook_angle"] = "135",
                ["tie_type"] = "Auto",
                ["bars"] = col.TryGetValue("pedestal_bars", out var pb) ? pb : "12:4",
                ["provide_lap"] = "No"
            });
            foreach (var k in col.Keys.Where(k => k.StartsWith("pedestal", StringComparison.OrdinalIgnoreCase)).ToList())
                col.Remove(k);
        }
    }

    private static void LoadIntMap(JsonObject? src, Dictionary<int, double> dest, Dictionary<int, double> fallback)
    {
        dest.Clear();
        if (src is not null)
        {
            foreach (var kv in src)
            {
                if (!int.TryParse(kv.Key, out var k)) continue;
                if (kv.Value is JsonValue jv && jv.TryGetValue<double>(out var d))
                    dest[k] = d;
            }
        }
        if (dest.Count == 0)
            foreach (var kv in fallback) dest[kv.Key] = kv.Value;
    }

    private static double Num(JsonObject o, string key, double def)
    {
        if (o[key] is JsonValue jv && jv.TryGetValue<double>(out var d)) return d;
        return def;
    }

    private static double NumRoot(JsonObject o, string key, double def) => Num(o, key, def);

    private static void LoadRows(JsonArray? arr, ObservableCollection<Dictionary<string, string>> dest)
    {
        if (arr is null) return;
        foreach (var item in arr)
        {
            if (item is not JsonObject o) continue;
            var row = new Dictionary<string, string>();
            foreach (var kv in o)
            {
                if (kv.Value is JsonValue jv)
                {
                    if (jv.TryGetValue<string>(out var s)) row[kv.Key] = s ?? "";
                    else if (jv.TryGetValue<double>(out var d))
                        row[kv.Key] = d.ToString(CultureInfo.InvariantCulture);
                    else row[kv.Key] = jv.ToJsonString().Trim('"');
                }
                else row[kv.Key] = kv.Value?.ToString() ?? "";
            }
            dest.Add(row);
        }
    }

    public void Reset()
    {
        Info.Reset();
        Parties.Reset();
        Parties.EnsureDefaults(Info);
        Markups.Reset();
        Name = Info.Name;
        FilePath = null;
        Shared = null;
        Columns.Clear(); Beams.Clear(); Pedestals.Clear(); Lintels.Clear();
        Slabs.Clear(); Footings.Clear(); Walls.Clear(); Stairs.Clear();
        MasonryWalls.Clear(); MasonryOpenings.Clear(); Plaster.Clear(); FinishPropose.Clear(); PccBeds.Clear(); Earthwork.Clear(); SizeStone.Clear();
        Shuttering.Clear(); Flooring.Clear(); Painting.Clear();
        Waterproofing.Clear(); Dpc.Clear(); Coping.Clear(); Screed.Clear();
        Vdf.Clear(); Skirting.Clear(); Parapet.Clear(); PlinthProtection.Clear();
        Doors.Clear(); Windows.Clear();
        Takeoff.Clear();
        Schedule.Clear();
        Office.Clear();
        ContractBook.Clear();
        Accounts.Clear();
        Stores.Clear();
        Org.Clear();
        LinkRules.ResetToDefaults();
        Levels.Clear();
        LastSummary = null;
        LastBbs = null;
        LastCivilSummary = null;
        LastEstimate = null;
        LastEstimateRateBookVersionId = null;
        IsDirty = false;
        EnsureDefaultLevels();
        SeedDefaults();
        Changed?.Invoke();
    }

    public void SeedDefaults()
    {
        EnsureDefaultLevels();
        LinkRules.SeedIfEmpty();
        ContractBook.EnsureSeeded();
        Stores.EnsureSeeded();
        Org.EnsureSeeded();
        double h0 = ColumnHeightFor("Lvl0");
        if (Columns.Count == 0)
            Columns.Add(new Dictionary<string, string>
            {
                ["mark"] = "C1", ["nos"] = "1", ["level"] = "Lvl0", ["width"] = "300", ["depth"] = "450",
                ["height"] = h0 > 0 ? h0.ToString("0", CultureInfo.InvariantCulture) : "2600",
                ["cover"] = CoverColumnMm.ToString("0", CultureInfo.InvariantCulture), ["concrete_grade"] = "M25",
                ["column_type"] = "Rectangular",
                ["stirrup_dia"] = "8", ["spacing"] = "150", ["hook_angle"] = "135", ["tie_type"] = "Auto",
                ["bars"] = "16:8", ["steel_grade"] = "Fe500", ["provide_lap"] = DefaultColumnLap
            });
        if (Pedestals.Count == 0)
            Pedestals.Add(new Dictionary<string, string>
            {
                ["mark"] = "P1", ["nos"] = "1", ["level"] = "Lvl0", ["column_type"] = "Square",
                ["width"] = "600", ["depth"] = "600", ["height"] = "600",
                ["cover"] = CoverPedestalMm.ToString("0", CultureInfo.InvariantCulture),
                ["concrete_grade"] = "M25", ["steel_grade"] = "Fe500",
                ["stirrup_dia"] = "8", ["spacing"] = "150", ["hook_angle"] = "135", ["tie_type"] = "Auto",
                ["bars"] = "12:4", ["provide_lap"] = "No"
            });
        if (Beams.Count == 0)
            Beams.Add(new Dictionary<string, string>
            {
                ["mark"] = "PB1", ["nos"] = "1", ["beam_type"] = "PB", ["level"] = "Lvl0",
                ["span"] = "4000", ["width"] = "230", ["depth"] = "450",
                ["cover"] = CoverBeamMm.ToString("0", CultureInfo.InvariantCulture),
                ["concrete_grade"] = "M25", ["steel_grade"] = "Fe500", ["stirrup_dia"] = "8",
                ["spacing_support"] = "100", ["spacing_middle"] = "150", ["legs"] = "2", ["hook_angle"] = "135",
                ["top_bar_type"] = "At Support", ["hanger_bars"] = "12:2",
                ["top_bars"] = "16:2", ["bottom_bars"] = "16:3, 20:2",
                ["end_anchorage"] = "Straight Ld", ["provide_lap"] = DefaultBeamLap
            });
        if (Lintels.Count == 0)
            Lintels.Add(new Dictionary<string, string>
            {
                ["mark"] = "L1", ["nos"] = "1", ["level"] = "Lvl0",
                ["opening"] = "900", ["bearing"] = "150", ["span"] = "1200",
                ["width"] = "230", ["depth"] = "150",
                ["cover"] = CoverLintelMm.ToString("0", CultureInfo.InvariantCulture),
                ["concrete_grade"] = "M25", ["steel_grade"] = "Fe500",
                ["stirrup_dia"] = "8", ["spacing_support"] = "100", ["spacing_middle"] = "150",
                ["legs"] = "2", ["hook_angle"] = "135",
                ["hanger_bars"] = "8:2", ["top_bar_type"] = "Full Span",
                ["top_bars"] = "10:2", ["bottom_bars"] = "12:2",
                ["end_anchorage"] = "90 Hook", ["provide_lap"] = "None"
            });
        if (Slabs.Count == 0)
            Slabs.Add(new Dictionary<string, string>
            {
                ["mark"] = "S1", ["level"] = "Lvl0", ["span_x"] = "3000", ["span_y"] = "4500", ["thickness"] = "125", ["cover"] = "20",
                ["slab_type"] = "Two-Way", ["concrete_grade"] = "M25", ["steel_grade"] = "Fe415",
                ["dia_x"] = "10", ["spacing_x"] = "150", ["dia_y"] = "10", ["spacing_y"] = "150",
                ["crank_count"] = "0"
            });
        if (Footings.Count == 0)
            Footings.Add(new Dictionary<string, string>
            {
                ["mark"] = "F1", ["level"] = "Lvl0", ["footing_type"] = "Isolated", ["length_l"] = "2000", ["width_b"] = "2000",
                ["col_dim_l"] = "400", ["col_dim_b"] = "400", ["depth"] = "500", ["cover"] = "50",
                ["concrete_grade"] = "M25", ["steel_grade"] = "Fe500",
                ["dia_l"] = "12", ["spacing_l"] = "150", ["dia_b"] = "12", ["spacing_b"] = "150"
            });
        if (Walls.Count == 0)
            Walls.Add(new Dictionary<string, string>
            {
                ["mark"] = "RW1", ["level"] = "Lvl0", ["wall_length"] = "5000", ["stem_h"] = "3000", ["stem_t"] = "250",
                ["heel"] = "1500", ["include_toe"] = "Yes", ["toe"] = "600", ["base_t"] = "400", ["cover"] = "50",
                ["concrete_grade"] = "M25", ["steel_grade"] = "Fe500", ["tension_face"] = "Front",
                ["stem_v_dia"] = "12", ["stem_v_spacing"] = "150", ["stem_v_back_dia"] = "10",
                ["stem_v_back_spacing"] = "200", ["stem_h_dia"] = "10", ["stem_h_spacing"] = "200",
                ["base_l_dia"] = "12", ["base_l_spacing"] = "150", ["base_b_dia"] = "12", ["base_b_spacing"] = "150",
                ["link_legs"] = "2"
            });
        if (Stairs.Count == 0)
            Stairs.Add(new Dictionary<string, string>
            {
                ["mark"] = "ST1", ["level"] = "Lvl0",
                ["n_risers"] = "12", ["going"] = "250", ["riser"] = "150",
                ["waist_t"] = "150", ["flight_width"] = "1200", ["cover"] = "20",
                ["n_flights"] = "1",
                ["landing_len"] = "1200", ["landing_t"] = "150",
                ["concrete_grade"] = "M25", ["steel_grade"] = "Fe500",
                ["main_dia"] = "12", ["main_spacing"] = "150",
                ["dist_dia"] = "8", ["dist_spacing"] = "200",
                ["landing_dia"] = "10", ["landing_spacing"] = "150"
            });
        if (MasonryWalls.Count == 0)
        {
            var mw = new Dictionary<string, string>
            {
                ["mark"] = "MW1", ["level"] = "Lvl0", ["length"] = "5000", ["height"] = "3000",
                ["mortar_mix"] = "1:6",
                ["deduct_rule"] = "IS1200 masonry"
            };
            MasonryWallBuild.Apply(mw, "Brick · 230 mm");
            MasonryWalls.Add(mw);
        }
        else
        {
            foreach (var mw in MasonryWalls) MasonryWallBuild.EnsureWallBuild(mw);
        }
        MigrateMasonryOpeningsFromWalls();
        if (MasonryOpenings.Count == 0 && MasonryWalls.Count > 0)
        {
            string wm = MasonryWalls[0].TryGetValue("mark", out var m) ? m : "MW1";
            string wl = MasonryWalls[0].TryGetValue("level", out var l) ? l : "Lvl0";
            MasonryOpenings.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["wall_mark"] = wm,
                ["level"] = wl,
                ["nos"] = "1",
                ["opening_l"] = "900",
                ["opening_h"] = "2100"
            });
        }
        // Plaster/paint auto rows come from FinishSurfaces Finalize — no seed row.
        FinishSurfacesCalculator.SyncPropose(this);
        if (PccBeds.Count == 0)
            PccBeds.Add(new Dictionary<string, string>
            {
                ["mark"] = "PCC1", ["level"] = "Lvl0", ["length"] = "3000", ["breadth"] = "2000",
                ["thickness"] = "100", ["mix"] = "1:4:8"
            });
        if (Earthwork.Count == 0)
            Earthwork.Add(new Dictionary<string, string>
            {
                ["mark"] = "EW1", ["level"] = "Lvl0", ["work_type"] = "Excavation",
                ["length"] = "10000", ["breadth"] = "3000", ["depth"] = "1500"
            });
        if (SizeStone.Count == 0)
            SizeStone.Add(new Dictionary<string, string>
            {
                ["mark"] = "SSM1", ["level"] = "Lvl0", ["length"] = "5000", ["breadth"] = "450",
                ["height"] = "1500", ["mortar_mix"] = "1:6"
            });
        // Shuttering is always derived from RCC — never seed a manual row.
        ShutteringCalculator.SyncStore(this);
        if (Flooring.Count == 0)
            Flooring.Add(new Dictionary<string, string>
            {
                ["mark"] = "FL1", ["level"] = "Lvl0",
                ["surface_kind"] = "Floor",
                ["finish_type"] = "Vitrified tiles",
                ["tile_size"] = "600×600",
                ["length"] = "4000", ["breadth"] = "3000", ["deduct_rule"] = "Openings full",
                ["opening_nos"] = "0", ["opening_l"] = "0", ["opening_h"] = "0"
            });
        if (Doors.Count == 0)
            Doors.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mark"] = "D1", ["level"] = "Lvl0", ["door_type"] = "Wood door", ["nos"] = "1",
                ["width"] = "900", ["height"] = "2100",
                ["frame_size"] = "110×150", ["shutter_thick"] = "32 mm", ["shutter_type"] = "Block Board",
                ["wood_finish"] = "Varnish"
            });
        if (Windows.Count == 0)
            Windows.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mark"] = "W1", ["level"] = "Lvl0", ["window_system"] = "System Aluminium", ["nos"] = "2",
                ["width"] = "1200", ["height"] = "1200", ["track"] = "2.5 Track",
                ["wood_opening"] = "Single shutter — open outside",
                ["wood_finish"] = "Varnish"
            });
    }
}
