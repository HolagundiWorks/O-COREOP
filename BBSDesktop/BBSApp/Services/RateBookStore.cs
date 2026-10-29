// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BBSApp.Models;

namespace BBSApp.Services;

public sealed class RateItem
{
    public string Code { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Rate { get; set; }
    /// <summary>The item number in the schedule this rate comes from (a DSR item number such as the user's copy prints it); empty when not set. Printed on the estimate.</summary>
    public string DsrItemNo { get; set; } = "";
}

public sealed class RateBookVersion
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Notes { get; set; } = "";
    public List<RateItem> Items { get; set; } = new();
}

/// <summary>App-level rate book library with versioned schedules (%LocalAppData%/O-CoreOp/ratebooks.json).</summary>
public sealed class RateBookStore
{
    public static RateBookStore Current { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public List<RateBookVersion> Versions { get; } = new();
    public string? ActiveVersionId { get; set; }

    public string LibraryPath => Path.Combine(Branding.AppDataDirectory, "ratebooks.json");

    public RateBookVersion? Find(string? id) =>
        Versions.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    public RateBookVersion? ActiveOrFirst() =>
        Find(ActiveVersionId) ?? Versions.FirstOrDefault();

    public void EnsureLoaded()
    {
        if (Versions.Count > 0) return;
        Load();
        if (Versions.Count == 0)
        {
            var v1 = CreateSeedVersion("v1-default", "v1 Default");
            Versions.Add(v1);
            ActiveVersionId = v1.Id;
            Save();
        }
    }

    public void Load()
    {
        Versions.Clear();
        ActiveVersionId = null;
        try
        {
            string path = Branding.ResolveAppDataFile("ratebooks.json");
            if (!File.Exists(path)) return;
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root is null) return;
            ActiveVersionId = root["activeVersionId"]?.GetValue<string>();
            if (root["versions"] is not JsonArray arr) return;
            foreach (var n in arr)
            {
                if (n is not JsonObject o) continue;
                var ver = new RateBookVersion
                {
                    Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
                    Name = o["name"]?.GetValue<string>() ?? "Version",
                    Notes = o["notes"]?.GetValue<string>() ?? "",
                    CreatedUtc = DateTime.TryParse(o["createdUtc"]?.GetValue<string>(), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.UtcNow
                };
                if (o["items"] is JsonArray items)
                {
                    foreach (var it in items)
                    {
                        if (it is not JsonObject io) continue;
                        ver.Items.Add(new RateItem
                        {
                            Code = io["code"]?.GetValue<string>() ?? "",
                            Category = io["category"]?.GetValue<string>() ?? "",
                            Description = io["description"]?.GetValue<string>() ?? "",
                            Unit = io["unit"]?.GetValue<string>() ?? "",
                            Rate = io["rate"]?.GetValue<double>() ?? 0,
                            DsrItemNo = io["dsr_item_no"]?.GetValue<string>() ?? ""
                        });
                    }
                }
                Versions.Add(ver);
            }
        }
        catch
        {
            Versions.Clear();
        }
    }

    public void Save()
    {
        string dir = Path.GetDirectoryName(LibraryPath)!;
        Directory.CreateDirectory(dir);
        var arr = new JsonArray();
        foreach (var v in Versions)
        {
            var items = new JsonArray();
            foreach (var it in v.Items)
            {
                items.Add(new JsonObject
                {
                    ["code"] = it.Code,
                    ["category"] = it.Category,
                    ["description"] = it.Description,
                    ["unit"] = it.Unit,
                    ["rate"] = it.Rate,
                    ["dsr_item_no"] = it.DsrItemNo
                });
            }
            arr.Add(new JsonObject
            {
                ["id"] = v.Id,
                ["name"] = v.Name,
                ["notes"] = v.Notes,
                ["createdUtc"] = v.CreatedUtc.ToString("o", CultureInfo.InvariantCulture),
                ["items"] = items
            });
        }
        var root = new JsonObject
        {
            ["format"] = "bbsrates",
            ["version"] = 1,
            ["activeVersionId"] = ActiveVersionId ?? "",
            ["versions"] = arr
        };
        File.WriteAllText(LibraryPath, root.ToJsonString(JsonOpts));
    }

    /// <summary>Clone source (or active) into a new version with a new id.</summary>
    public RateBookVersion CreateVersion(string name, string? cloneFromId = null, string notes = "")
    {
        EnsureLoaded();
        var src = Find(cloneFromId) ?? ActiveOrFirst();
        var neu = new RateBookVersion
        {
            Id = "v-" + Guid.NewGuid().ToString("N")[..10],
            Name = string.IsNullOrWhiteSpace(name) ? $"v{Versions.Count + 1}" : name.Trim(),
            CreatedUtc = DateTime.UtcNow,
            Notes = notes,
            Items = src is null
                ? CreateSeedVersion("tmp", "tmp").Items
                : src.Items.Select(CloneItem).ToList()
        };
        Versions.Add(neu);
        ActiveVersionId = neu.Id;
        Save();
        return neu;
    }

    public void UpdateVersion(RateBookVersion version)
    {
        var existing = Find(version.Id);
        if (existing is null) return;
        existing.Name = version.Name;
        existing.Notes = version.Notes;
        existing.Items = version.Items.Select(CloneItem).ToList();
        Save();
    }

    public Dictionary<string, RateItem> IndexByCode(RateBookVersion ver) =>
        ver.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Code))
            .GroupBy(i => i.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private static RateItem CloneItem(RateItem i) => new()
    {
        Code = i.Code,
        Category = i.Category,
        Description = i.Description,
        Unit = i.Unit,
        Rate = i.Rate,
        DsrItemNo = i.DsrItemNo
    };

    public static RateBookVersion CreateSeedVersion(string id, string name)
    {
        var items = new List<RateItem>();
        void Add(string code, string cat, string desc, string unit, double rate) =>
            items.Add(new RateItem { Code = code, Category = cat, Description = desc, Unit = unit, Rate = rate });

        // Civil works (placeholder rates — edit in Rate book)
        Add("MSN-BRICK-M3", "Masonry", "Brick / block masonry (m³)", "m³", 6500);
        Add("MSN-BRICK-M2", "Masonry", "Half-brick / thin wall masonry (m²)", "m²", 950);
        Add("PL-STD", "Plaster", "Plastering", "m²", 280);
        Add("PT-STD", "Finishes", "Painting (generic)", "m²", 120);
        Add("PCC-STD", "Concrete", "PCC bed", "m³", 5200);
        Add("EW-STD", "Earthwork", "Earthwork excavation / filling", "m³", 350);
        Add("SSM-STD", "Masonry", "Size stone masonry", "m³", 5800);
        Add("SH-STD", "Shuttering", "Formwork / shuttering", "m²", 450);
        Add("FL-STD", "Finishes", "Flooring (generic)", "m²", 900);
        Add("WP-STD", "Finishes", "Waterproofing", "m²", 650);
        Add("DPC-STD", "Finishes", "Damp-proof course", "m²", 400);
        Add("CP-STD", "Finishes", "Coping", "m", 350);
        Add("SC-STD", "Finishes", "Screed", "m³", 4800);
        Add("VDF-STD", "Finishes", "VDF flooring", "m²", 750);
        Add("SK-STD", "Finishes", "Skirting", "m²", 500);
        Add("PR-STD", "Masonry", "Parapet", "m³", 6200);
        Add("PP-STD", "Finishes", "Plinth protection", "m²", 420);

        // Floor / wall tiles
        foreach (var surface in FinishCatalog.SurfaceKinds)
        foreach (var finish in (surface == "Wall" ? FinishCatalog.WallFinishTypes : FinishCatalog.FloorFinishTypes))
        {
            if (FinishCatalog.NeedsTileSize(finish))
            {
                foreach (var size in FinishCatalog.TileSizes)
                {
                    if (size.StartsWith("Other", StringComparison.OrdinalIgnoreCase)) continue;
                    string code = FinishCatalog.FloorItemCode(surface, finish, size);
                    Add(code, surface == "Wall" ? "Wall tiles" : "Flooring",
                        FinishCatalog.FloorDescription(surface, finish, size), "m²",
                        SampleTileRate(finish, size, surface));
                }
            }
            else
            {
                string code = FinishCatalog.FloorItemCode(surface, finish, "");
                Add(code, surface == "Wall" ? "Wall tiles" : "Flooring",
                    FinishCatalog.FloorDescription(surface, finish, ""), "m²",
                    SampleTileRate(finish, "", surface));
            }
        }

        // Paint systems
        foreach (var loc in FinishCatalog.PaintLocations)
        foreach (var pt in FinishCatalog.PaintTypes)
        {
            if (pt.Equals("Other", StringComparison.OrdinalIgnoreCase)) continue;
            string system = FinishCatalog.PaintSystems[0];
            string code = FinishCatalog.PaintItemCode(loc, pt, system);
            Add(code, "Painting", FinishCatalog.PaintDescription(loc, pt, system), "m²",
                SamplePaintRate(loc, pt));
        }

        // Wood finishes (doors / windows)
        foreach (var wf in FinishCatalog.WoodFinishes)
        {
            if (wf.StartsWith("None", StringComparison.OrdinalIgnoreCase)) continue;
            Add(FinishCatalog.WoodFinishCode(wf), "Wood finish", $"Wood {wf.ToLowerInvariant()}", "m²",
                wf.Contains("Polish", StringComparison.OrdinalIgnoreCase) ? 280
                : wf.Contains("Varnish", StringComparison.OrdinalIgnoreCase) ? 220
                : wf.Contains("Melamine", StringComparison.OrdinalIgnoreCase) ? 320
                : 180);
        }

        // Doors — MS
        Add("DR-MS", "Doors", "MS door", "m²", 4500);

        // Doors — wood frames × shutter thickness × type
        foreach (var frame in DoorWindowCatalog.WoodFrames)
        foreach (var thick in DoorWindowCatalog.ShutterThicknesses)
        foreach (var (type, abbr) in DoorWindowCatalog.ShutterTypes)
        {
            string code = DoorWindowCatalog.WoodDoorCode(frame, thick, type);
            Add(code, "Doors",
                $"Wood door · frame {frame} · shutter {thick} · {type}",
                "m²", SampleWoodDoorRate(type, thick));
        }

        // Windows
        foreach (var track in DoorWindowCatalog.Tracks)
        {
            Add(DoorWindowCatalog.WindowCode("System Aluminium", track, ""), "Windows",
                $"System aluminium window · {track}", "m²", track.StartsWith("3") ? 4200 : 3800);
            Add(DoorWindowCatalog.WindowCode("UPVC", track, ""), "Windows",
                $"UPVC window · {track}", "m²", track.StartsWith("3") ? 3900 : 3500);
        }
        foreach (var open in DoorWindowCatalog.WoodOpenings)
        {
            Add(DoorWindowCatalog.WindowCode("Wooden", "", open), "Windows",
                $"Wooden window · {open}", "m²", 3200);
        }

        // Materials / steel placeholders used by estimate materials pass
        Add("MAT-BRICK", "Units", "Bricks (modular)", "nos", 8);
        Add("MAT-ACC", "Units", "ACC blocks", "nos", 55);
        Add("MAT-CEMBLK", "Units", "Cement blocks", "nos", 35);
        Add("MAT-CEMENT", "Cement", "OPC / PPC (civil works)", "bags (50kg)", 420);
        Add("MAT-SAND", "Sand", "Fine aggregate (civil)", "m³", 1800);
        Add("MAT-AGG", "Aggregate", "Coarse aggregate (civil)", "m³", 2200);
        Add("STL-KG", "Steel", "Reinforcement steel", "kg", 65);

        return new RateBookVersion
        {
            Id = id,
            Name = name,
            CreatedUtc = DateTime.UtcNow,
            Notes = "Seeded placeholder rates — edit and clone versions for project estimates.",
            Items = items
        };
    }

    private static double SampleWoodDoorRate(string type, string thick)
    {
        double baseR = thick.StartsWith("50") ? 3800 : 3200;
        if (type.Contains("Teak Class-1", StringComparison.OrdinalIgnoreCase)) return baseR + 2200;
        if (type.Contains("Teak Class-2", StringComparison.OrdinalIgnoreCase)) return baseR + 1500;
        if (type.Contains("Teak Class-3", StringComparison.OrdinalIgnoreCase)) return baseR + 900;
        if (type.Contains("Solid", StringComparison.OrdinalIgnoreCase)) return baseR + 800;
        if (type.Contains("WPC", StringComparison.OrdinalIgnoreCase)) return baseR + 600;
        if (type.Contains("Prelam", StringComparison.OrdinalIgnoreCase)) return baseR + 200;
        return baseR;
    }

    private static double SampleTileRate(string finish, string size, string surface)
    {
        double baseR = surface.Equals("Wall", StringComparison.OrdinalIgnoreCase) ? 850 : 950;
        if (finish.Contains("Granite", StringComparison.OrdinalIgnoreCase)) return baseR + 1200;
        if (finish.Contains("Marble", StringComparison.OrdinalIgnoreCase)) return baseR + 1800;
        if (finish.Contains("Vitrified", StringComparison.OrdinalIgnoreCase))
        {
            if (size.Contains("2400") || size.Contains("2100")) return baseR + 700;
            if (size.Contains("1200")) return baseR + 400;
            return baseR + 200;
        }
        if (finish.Contains("Ceramic", StringComparison.OrdinalIgnoreCase)) return baseR;
        if (finish.Contains("Kota", StringComparison.OrdinalIgnoreCase)) return baseR - 100;
        if (finish.Contains("IPS", StringComparison.OrdinalIgnoreCase)) return 450;
        return baseR;
    }

    private static double SamplePaintRate(string location, string paintType)
    {
        double baseR = 140; // includes primer+putty+2 coat build-up
        if (location.Contains("Outside", StringComparison.OrdinalIgnoreCase)) baseR += 40;
        if (location.Contains("Ceiling", StringComparison.OrdinalIgnoreCase)) baseR += 10;
        if (paintType.Contains("Distemper", StringComparison.OrdinalIgnoreCase)) return baseR - 40;
        if (paintType.Contains("Exterior", StringComparison.OrdinalIgnoreCase)) return baseR + 50;
        if (paintType.Contains("Enamel", StringComparison.OrdinalIgnoreCase)) return baseR + 30;
        return baseR;
    }
}
