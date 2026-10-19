// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;

namespace BBSApp.Services;

public sealed class Supplier
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Gstin { get; set; } = "";
    public string Address { get; set; } = "";

    public JsonObject ToJson() => new()
    { ["id"] = Id, ["name"] = Name, ["contact"] = Contact, ["gstin"] = Gstin, ["address"] = Address };
    public static Supplier FromJson(JsonObject o) => new()
    {
        Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
        Name = o["name"]?.GetValue<string>() ?? "",
        Contact = o["contact"]?.GetValue<string>() ?? "",
        Gstin = o["gstin"]?.GetValue<string>() ?? "",
        Address = o["address"]?.GetValue<string>() ?? ""
    };
}

public sealed class Warehouse
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";

    public JsonObject ToJson() => new() { ["id"] = Id, ["name"] = Name, ["location"] = Location };
    public static Warehouse FromJson(JsonObject o) => new()
    {
        Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
        Name = o["name"]?.GetValue<string>() ?? "",
        Location = o["location"]?.GetValue<string>() ?? ""
    };
}

/// <summary>One material line on a PO / GRN / issue.</summary>
public sealed class StoreLine
{
    public string Material { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Qty { get; set; }
    public double Rate { get; set; }
    public double Amount => Qty * Rate;

    public JsonObject ToJson() => new() { ["material"] = Material, ["unit"] = Unit, ["qty"] = Qty, ["rate"] = Rate };
    public static StoreLine FromJson(JsonObject o) => new()
    {
        Material = o["material"]?.GetValue<string>() ?? "",
        Unit = o["unit"]?.GetValue<string>() ?? "",
        Qty = o["qty"]?.GetValue<double>() ?? 0,
        Rate = o["rate"]?.GetValue<double>() ?? 0
    };
}

/// <summary>Purchase order (draft = indent, placed = numbered PO).</summary>
public sealed class PurchaseOrder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Number { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today;
    public string SupplierId { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string WarehouseId { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<StoreLine> Lines { get; } = new();
    public bool Placed { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public double Total => Lines.Sum(l => l.Amount);

    public JsonObject ToJson()
    {
        var lines = new JsonArray();
        foreach (var l in Lines) lines.Add(l.ToJson());
        return new JsonObject
        {
            ["id"] = Id, ["number"] = Number,
            ["date"] = Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["supplier_id"] = SupplierId, ["supplier_name"] = SupplierName,
            ["warehouse_id"] = WarehouseId, ["notes"] = Notes,
            ["placed"] = Placed ? 1 : 0, ["lines"] = lines
        };
    }
    public static PurchaseOrder FromJson(JsonObject o)
    {
        var p = new PurchaseOrder
        {
            Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            Number = o["number"]?.GetValue<string>() ?? "",
            SupplierId = o["supplier_id"]?.GetValue<string>() ?? "",
            SupplierName = o["supplier_name"]?.GetValue<string>() ?? "",
            WarehouseId = o["warehouse_id"]?.GetValue<string>() ?? "",
            Notes = o["notes"]?.GetValue<string>() ?? "",
            Placed = (o["placed"]?.GetValue<int>() ?? 0) != 0
        };
        if (DateTime.TryParse(o["date"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) p.Date = d;
        if (o["lines"] is JsonArray la) foreach (var it in la) if (it is JsonObject lo) p.Lines.Add(StoreLine.FromJson(lo));
        return p;
    }
}

/// <summary>Goods receipt note. When Received, its lines add to stock.</summary>
public sealed class Grn
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Number { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today;
    public string PoId { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string WarehouseId { get; set; } = "";
    public List<StoreLine> Lines { get; } = new();
    public bool Received { get; set; }

    public JsonObject ToJson()
    {
        var lines = new JsonArray();
        foreach (var l in Lines) lines.Add(l.ToJson());
        return new JsonObject
        {
            ["id"] = Id, ["number"] = Number,
            ["date"] = Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["po_id"] = PoId, ["supplier_name"] = SupplierName,
            ["warehouse_id"] = WarehouseId, ["received"] = Received ? 1 : 0, ["lines"] = lines
        };
    }
    public static Grn FromJson(JsonObject o)
    {
        var g = new Grn
        {
            Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            Number = o["number"]?.GetValue<string>() ?? "",
            PoId = o["po_id"]?.GetValue<string>() ?? "",
            SupplierName = o["supplier_name"]?.GetValue<string>() ?? "",
            WarehouseId = o["warehouse_id"]?.GetValue<string>() ?? "",
            Received = (o["received"]?.GetValue<int>() ?? 0) != 0
        };
        if (DateTime.TryParse(o["date"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) g.Date = d;
        if (o["lines"] is JsonArray la) foreach (var it in la) if (it is JsonObject lo) g.Lines.Add(StoreLine.FromJson(lo));
        return g;
    }
}

/// <summary>Material issue (stock out).</summary>
public sealed class StockIssue
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Number { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today;
    public string WarehouseId { get; set; } = "";
    public string IssuedTo { get; set; } = "";
    public string Material { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Qty { get; set; }

    public JsonObject ToJson() => new()
    {
        ["id"] = Id, ["number"] = Number,
        ["date"] = Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["warehouse_id"] = WarehouseId, ["issued_to"] = IssuedTo,
        ["material"] = Material, ["unit"] = Unit, ["qty"] = Qty
    };
    public static StockIssue FromJson(JsonObject o)
    {
        var s = new StockIssue
        {
            Id = o["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"),
            Number = o["number"]?.GetValue<string>() ?? "",
            WarehouseId = o["warehouse_id"]?.GetValue<string>() ?? "",
            IssuedTo = o["issued_to"]?.GetValue<string>() ?? "",
            Material = o["material"]?.GetValue<string>() ?? "",
            Unit = o["unit"]?.GetValue<string>() ?? "",
            Qty = o["qty"]?.GetValue<double>() ?? 0
        };
        if (DateTime.TryParse(o["date"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) s.Date = d;
        return s;
    }
}

/// <summary>Computed inventory row (material at a warehouse).</summary>
public sealed class StockRow
{
    public string Material { get; init; } = "";
    public string Unit { get; init; } = "";
    public string Warehouse { get; init; } = "";
    public double Received { get; init; }
    public double Issued { get; init; }
    public double InStock => Received - Issued;
}

/// <summary>Procurement + stores: suppliers, warehouses, POs, GRNs, issues, inventory.</summary>
public sealed class StoresBook
{
    public string Prefix { get; set; } = "";
    public ObservableCollection<Supplier> Suppliers { get; } = new();
    public ObservableCollection<Warehouse> Warehouses { get; } = new();
    public ObservableCollection<PurchaseOrder> Orders { get; } = new();
    public ObservableCollection<Grn> Grns { get; } = new();
    public ObservableCollection<StockIssue> Issues { get; } = new();
    private readonly Dictionary<string, int> _counters = new(StringComparer.OrdinalIgnoreCase);

    public string EffectivePrefix(string companyName)
    {
        if (!string.IsNullOrWhiteSpace(Prefix)) return Prefix.Trim();
        var initials = new string((companyName ?? "")
            .Split(new[] { ' ', '-', '.', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0])).ToArray());
        return string.IsNullOrWhiteSpace(initials) ? "OC" : initials;
    }

    public string Preview(string code, DateTime date, string company)
    {
        string fy = OfficeRegister.FinancialYear(date);
        int next = (_counters.TryGetValue($"{code}|{fy}", out var last) ? last : 0) + 1;
        return $"{EffectivePrefix(company)}/{code}/{fy}/{next:000}";
    }
    public string Assign(string code, DateTime date, string company)
    {
        string fy = OfficeRegister.FinancialYear(date);
        string key = $"{code}|{fy}";
        int next = (_counters.TryGetValue(key, out var last) ? last : 0) + 1;
        _counters[key] = next;
        return $"{EffectivePrefix(company)}/{code}/{fy}/{next:000}";
    }

    public string WarehouseName(string id) =>
        Warehouses.FirstOrDefault(w => w.Id == id)?.Name ?? "(unassigned)";

    /// <summary>Stock per material+warehouse: received (from received GRNs) minus issued.</summary>
    public IReadOnlyList<StockRow> Inventory()
    {
        var recv = new Dictionary<(string, string), (double qty, string unit)>();
        foreach (var g in Grns.Where(g => g.Received))
            foreach (var l in g.Lines)
            {
                var key = (l.Material.Trim(), g.WarehouseId);
                var cur = recv.TryGetValue(key, out var v) ? v : (0, l.Unit);
                recv[key] = (cur.qty + l.Qty, string.IsNullOrWhiteSpace(cur.unit) ? l.Unit : cur.unit);
            }
        var issued = new Dictionary<(string, string), double>();
        foreach (var s in Issues.Where(s => s.Qty > 0))
        {
            var key = (s.Material.Trim(), s.WarehouseId);
            issued[key] = (issued.TryGetValue(key, out var v) ? v : 0) + s.Qty;
        }
        var keys = recv.Keys.Union(issued.Keys).Where(k => !string.IsNullOrWhiteSpace(k.Item1));
        var rows = new List<StockRow>();
        foreach (var k in keys)
        {
            double r = recv.TryGetValue(k, out var rv) ? rv.qty : 0;
            double i = issued.TryGetValue(k, out var iv) ? iv : 0;
            string unit = recv.TryGetValue(k, out var uv) ? uv.unit : "";
            rows.Add(new StockRow { Material = k.Item1, Unit = unit, Warehouse = WarehouseName(k.Item2), Received = r, Issued = i });
        }
        return rows.OrderBy(x => x.Warehouse).ThenBy(x => x.Material).ToList();
    }

    public void Clear()
    {
        Suppliers.Clear(); Warehouses.Clear(); Orders.Clear(); Grns.Clear(); Issues.Clear();
        _counters.Clear(); Prefix = "";
    }

    public void EnsureSeeded()
    {
        if (Warehouses.Count == 0)
            Warehouses.Add(new Warehouse { Name = "Main store", Location = "Site" });
    }

    public JsonObject ToJson()
    {
        JsonArray Arr<T>(IEnumerable<T> src, Func<T, JsonObject> f) { var a = new JsonArray(); foreach (var x in src) a.Add(f(x)); return a; }
        var counters = new JsonObject();
        foreach (var kv in _counters) counters[kv.Key] = kv.Value;
        return new JsonObject
        {
            ["prefix"] = Prefix ?? "",
            ["counters"] = counters,
            ["suppliers"] = Arr(Suppliers, s => s.ToJson()),
            ["warehouses"] = Arr(Warehouses, w => w.ToJson()),
            ["orders"] = Arr(Orders, o => o.ToJson()),
            ["grns"] = Arr(Grns, g => g.ToJson()),
            ["issues"] = Arr(Issues, i => i.ToJson())
        };
    }

    public void LoadFrom(JsonObject? o)
    {
        Clear();
        if (o is null) { EnsureSeeded(); return; }
        Prefix = o["prefix"]?.GetValue<string>() ?? "";
        if (o["counters"] is JsonObject c)
            foreach (var kv in c) if (kv.Value is JsonValue v && v.TryGetValue<int>(out var n)) _counters[kv.Key] = n;
        if (o["suppliers"] is JsonArray sa) foreach (var it in sa) if (it is JsonObject so) Suppliers.Add(Supplier.FromJson(so));
        if (o["warehouses"] is JsonArray wa) foreach (var it in wa) if (it is JsonObject wo) Warehouses.Add(Warehouse.FromJson(wo));
        if (o["orders"] is JsonArray oa) foreach (var it in oa) if (it is JsonObject oo) Orders.Add(PurchaseOrder.FromJson(oo));
        if (o["grns"] is JsonArray ga) foreach (var it in ga) if (it is JsonObject go) Grns.Add(Grn.FromJson(go));
        if (o["issues"] is JsonArray ia) foreach (var it in ia) if (it is JsonObject io) Issues.Add(StockIssue.FromJson(io));
        EnsureSeeded();
    }
}
