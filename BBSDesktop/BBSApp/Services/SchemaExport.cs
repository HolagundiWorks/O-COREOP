// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Globalization;
using System.Text;

namespace BBSApp.Services;

/// <summary>
/// Generates the full logical ERD of O-CoreOp's estimation model as DBML (for
/// <c>dbdiagram.io</c>) and an equivalent SQL script. The structural tables are fixed; the
/// reference-bearing ones (trade, material, mix_design/mix_component, link_rule) are populated
/// from the live registries (<see cref="LinkTradeRegistry"/>, <see cref="MaterialsCalculator.MixFor"/>,
/// <see cref="LinkRuleBook.Defaults"/>) so the model tracks the code instead of drifting.
///
/// Two item relationships are modelled: <b>derivation</b> (trade → link_rule → derived_item,
/// e.g. brick wall → plaster → paint) and <b>composition</b> (trade / mix_design → material,
/// e.g. concrete → cement + fine + coarse aggregate). Regenerate from the app
/// (Item links → Export ERD); the committed snapshot is <c>docs/schema/aqc-core.{dbml,sql}</c>.
/// </summary>
public static class SchemaExport
{
    /// <summary>bbsproj JSON schema version (ProjectStore writes this).</summary>
    public const int FormatVersion = 17;

    private sealed record Material(string Key, string Name, string Unit, string Category);

    private static readonly Material[] Materials =
    {
        new("CEMENT",     "Cement (OPC / PPC)",       "bags (50kg)", "Binder"),
        new("FINE_AGG",   "Fine aggregate (sand)",    "m³",          "Aggregate"),
        new("COARSE_AGG", "Coarse aggregate",         "m³",          "Aggregate"),
        new("WATER",      "Water",                    "litre",       "Binder"),
        new("BRICK",      "Bricks (modular)",         "nos",         "Unit"),
        new("ACC_BLOCK",  "AAC / ACC blocks",         "nos",         "Unit"),
        new("CEM_BLOCK",  "Cement / concrete blocks", "nos",         "Unit"),
        new("STEEL",      "Reinforcement steel",      "kg",          "Steel"),
        new("SIZE_STONE", "Size stone",               "m³",          "Stone"),
    };

    private static readonly string[] ConcreteGrades = { "M15", "M20", "M25", "M30", "M35", "M40" };

    // Cement mortars (cement:sand) and PCC (cement:sand:aggregate). Mixes are user inputs on the
    // sheets; these are the representative defaults the calculators assume (mortar dry factor 1.33).
    private sealed record MortarMix(string Key, string Name, double Cement, double FineAgg, double CoarseAgg);
    private static readonly MortarMix[] MortarMixes =
    {
        new("CM_1_3",    "Cement mortar 1:3", 1, 3, 0),
        new("CM_1_4",    "Cement mortar 1:4", 1, 4, 0),
        new("CM_1_6",    "Cement mortar 1:6", 1, 6, 0),
        new("PCC_1_4_8", "PCC 1:4:8",         1, 4, 8),
    };

    // Item → material edges. via_mix is a mix_design key (consumed through a mix) or "" (direct unit).
    private sealed record TradeMaterial(string Trade, string Material, string ViaMix, string Note);
    private static readonly TradeMaterial[] TradeMaterials =
    {
        new("Masonry", "BRICK",      "",       "Brick masonry — unit count incl. wastage"),
        new("Masonry", "ACC_BLOCK",  "",       "AAC/ACC block masonry — alternative unit"),
        new("Masonry", "CEM_BLOCK",  "",       "Concrete block masonry — alternative unit"),
        new("Masonry", "CEMENT",     "CM_1_6", "Masonry mortar → cement"),
        new("Masonry", "FINE_AGG",   "CM_1_6", "Masonry mortar → sand"),
        new("Plaster", "CEMENT",     "CM_1_3", "Plaster mortar → cement"),
        new("Plaster", "FINE_AGG",   "CM_1_3", "Plaster mortar → sand"),
        new("PCC",     "CEMENT",     "PCC_1_4_8", "PCC → cement"),
        new("PCC",     "FINE_AGG",   "PCC_1_4_8", "PCC → sand"),
        new("PCC",     "COARSE_AGG", "PCC_1_4_8", "PCC → coarse aggregate"),
        new("SSM",     "SIZE_STONE", "",       "Size-stone masonry — stone volume"),
    };

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Write <c>aqc-core.dbml</c> and <c>aqc-core.sql</c> into <paramref name="dir"/>.
    /// Returns the two paths.</summary>
    public static (string Dbml, string Sql) WriteFiles(string dir)
    {
        Directory.CreateDirectory(dir);
        string dbmlPath = Path.Combine(dir, "aqc-core.dbml");
        string sqlPath = Path.Combine(dir, "aqc-core.sql");
        File.WriteAllText(dbmlPath, BuildDbml());
        File.WriteAllText(sqlPath, BuildSql());
        return (dbmlPath, sqlPath);
    }

    /// <summary>Item → material edges (trade key, material name, optional mix key). Direct or via a mix.</summary>
    public static IReadOnlyList<(string Trade, string MaterialName, string ViaMix)> ItemMaterialEdges() =>
        TradeMaterials
            .Select(e => (e.Trade, Materials.FirstOrDefault(m => m.Key == e.Material)?.Name ?? e.Material, e.ViaMix))
            .ToList();

    /// <summary>The full ERD as DBML — paste into dbdiagram.io (New Diagram → Import → DBML).</summary>
    public static string BuildDbml()
    {
        var b = new StringBuilder();
        b.AppendLine("// ─────────────────────────────────────────────────────────────────────────────");
        b.AppendLine("// O-CoreOp — estimation data model (logical ERD / schema)");
        b.AppendLine("//");
        b.AppendLine("// Generated by SchemaExport.BuildDbml() from the live registries — do not hand-edit;");
        b.AppendLine("// regenerate from the app (Item links → Export ERD).");
        b.AppendLine("//");
        b.AppendLine($"// Relational view of the model the app persists as JSON (.bbsproj, version {FormatVersion}).");
        b.AppendLine("// Two item relationships are modelled:");
        b.AppendLine("//   • derivation  — trade → link_rule → derived_item (masonry→plaster→paint)");
        b.AppendLine("//   • composition — trade/mix_design → material (concrete→cement+aggregates)");
        b.AppendLine("//");
        b.AppendLine("// Open: dbdiagram.io → New Diagram → Import → paste (DBML).");
        b.AppendLine("// ─────────────────────────────────────────────────────────────────────────────");
        b.AppendLine();
        b.AppendLine("Project aqc_core {");
        b.AppendLine("  database_type: 'Generic'");
        b.AppendLine("  Note: 'O-CoreOp (Human Centric Works) — quantity take-off + costing. Two item relationships: quantities of one trade derive from another (masonry→plaster→paint, flooring→skirting), and items decompose into materials (concrete→cement+aggregates, masonry→bricks+mortar).'");
        b.AppendLine("}");
        b.AppendLine();

        b.AppendLine("// ── Reference / lookup ───────────────────────────────────────────────────────");
        b.AppendLine("Table trade {");
        b.AppendLine("  key           varchar [pk, note: 'Trade node key = CivilLine.Element, e.g. \"Masonry\"']");
        b.AppendLine("  display       varchar [note: 'UI label, e.g. \"Plastering\"']");
        b.AppendLine("  unit          varchar [note: 'm² | m³ | m | nos']");
        b.AppendLine("  default_basis varchar [note: 'Area | Volume | Length | Perimeter | Count']");
        b.AppendLine("  Note: '''");
        b.AppendLine("  The linkable trades — graph nodes of the derivation model (LinkTradeRegistry):");
        foreach (var t in LinkTradeRegistry.Sources)
            b.AppendLine($"    • {t.Key} — {t.Display} ({t.Unit}, {LinkBasisInfo.Label(t.DefaultBasis)})");
        b.AppendLine("  '''");
        b.AppendLine("}");
        b.AppendLine();

        b.AppendLine("// ── Project & measurement ────────────────────────────────────────────────────");
        b.AppendLine("Table project {");
        b.AppendLine("  id             varchar [pk]");
        b.AppendLine("  name           varchar");
        b.AppendLine($"  format_version int     [note: 'bbsproj schema version ({FormatVersion})']");
        b.AppendLine("}");
        b.AppendLine();
        b.AppendLine("Table level {");
        b.AppendLine("  id                 varchar [pk]");
        b.AppendLine("  project_id         varchar [ref: > project.id]");
        b.AppendLine("  name               varchar");
        b.AppendLine("  height_mm          int");
        b.AppendLine("  slab_thickness_mm  int");
        b.AppendLine("  beam_depth_mm      int");
        b.AppendLine("}");
        b.AppendLine();
        b.AppendLine("Table takeoff_item {");
        b.AppendLine("  id          varchar [pk]");
        b.AppendLine("  project_id  varchar [ref: > project.id]");
        b.AppendLine("  trade_key   varchar [ref: > trade.key]");
        b.AppendLine("  mark        varchar  [note: 'Item mark, e.g. MW1, C1']");
        b.AppendLine("  level_id    varchar [ref: > level.id, null]");
        b.AppendLine("  length_m    double");
        b.AppendLine("  breadth_m   double");
        b.AppendLine("  height_m    double");
        b.AppendLine("  area_m2     double");
        b.AppendLine("  volume_m3   double");
        b.AppendLine("  qty         double");
        b.AppendLine("  unit        varchar");
        b.AppendLine("  source      varchar  [note: 'manual | auto_wall | from_plaster | …']");
        b.AppendLine("  source_mark varchar  [note: 'Provenance: parent item this row derives from']");
        b.AppendLine("  notes       varchar");
        b.AppendLine("  Note: 'Unified measurement rows — one per item per trade sheet (masonry, plaster, flooring…).'");
        b.AppendLine("}");
        b.AppendLine();

        b.AppendLine("// ── Linked-item derivation engine (item → item) ──────────────────────────────");
        b.AppendLine("Table link_rule {");
        b.AppendLine("  id           varchar [pk]");
        b.AppendLine("  project_id   varchar [ref: > project.id]");
        b.AppendLine("  name         varchar");
        b.AppendLine("  enabled      boolean");
        b.AppendLine("  source_trade varchar [ref: > trade.key, note: 'Producer trade']");
        b.AppendLine("  target_trade varchar [ref: > trade.key, note: 'Consumer trade']");
        b.AppendLine("  basis        varchar [note: 'Area | Volume | Length | Perimeter | Count']");
        b.AppendLine("  factor       double  [note: 'target_qty = factor × source[basis]']");
        b.AppendLine("  target_unit  varchar");
        b.AppendLine("  per_item     boolean [note: 'true: one linked line per source item; false: one aggregate']");
        b.AppendLine("  rate_code    varchar [note: 'Optional pinned rate code']");
        b.AppendLine("  rate_override double [note: 'Optional manual unit rate']");
        b.AppendLine("  notes        varchar");
        b.AppendLine("  Note: '''");
        b.AppendLine("  A derivation edge — target quantity follows the source. Standard library (LinkRuleBook.Defaults):");
        foreach (var r in LinkRuleBook.Defaults())
            b.AppendLine($"    • {LinkTradeRegistry.Display(r.SourceTrade)} → {LinkTradeRegistry.Display(r.TargetTrade)}"
                       + $"  ({LinkBasisInfo.Label(r.Basis)} × {Num(r.Factor)})" + (r.Enabled ? "" : " [off]"));
        b.AppendLine("  '''");
        b.AppendLine("}");
        b.AppendLine();
        b.AppendLine("Table derived_item {");
        b.AppendLine("  id           varchar [pk]");
        b.AppendLine("  rule_id      varchar [ref: > link_rule.id]");
        b.AppendLine("  source_trade varchar [ref: > trade.key]");
        b.AppendLine("  source_mark  varchar");
        b.AppendLine("  level_id     varchar [ref: > level.id, null]");
        b.AppendLine("  source_qty   double");
        b.AppendLine("  basis        varchar");
        b.AppendLine("  factor       double");
        b.AppendLine("  target_trade varchar [ref: > trade.key]");
        b.AppendLine("  target_qty   double");
        b.AppendLine("  target_unit  varchar");
        b.AppendLine("  chained      boolean [note: 'Source was itself produced by an upstream rule']");
        b.AppendLine("  Note: 'Engine output (computed by DerivationEngine). Logically a view over takeoff_item × link_rule.'");
        b.AppendLine("}");
        b.AppendLine();

        b.AppendLine("// ── Materials & composition (item → material) ────────────────────────────────");
        b.AppendLine("Table material {");
        b.AppendLine("  key      varchar [pk]");
        b.AppendLine("  name     varchar");
        b.AppendLine("  unit     varchar [note: 'bags | m³ | m | nos | kg | litre']");
        b.AppendLine("  category varchar [note: 'Binder | Aggregate | Unit | Steel | Stone']");
        b.AppendLine("  Note: '''");
        b.AppendLine("  Purchasable materials a work item resolves to (MaterialsCalculator / CivilBoqCalculator PO):");
        foreach (var m in Materials)
            b.AppendLine($"    • {m.Key} — {m.Name} ({m.Unit})");
        b.AppendLine("  '''");
        b.AppendLine("}");
        b.AppendLine();
        b.AppendLine("Table mix_design {");
        b.AppendLine("  key        varchar [pk]");
        b.AppendLine("  kind       varchar [note: 'concrete | mortar | pcc']");
        b.AppendLine("  grade      varchar [note: 'M15..M40 for concrete; a mix like 1:6 for mortar']");
        b.AppendLine("  dry_factor double  [note: 'Wet→dry volume factor (concrete 1.54, mortar 1.33)']");
        b.AppendLine("  note       varchar");
        b.AppendLine("  Note: '''");
        b.AppendLine("  Ratio recipes (MaterialsCalculator.MixFor / mortar mixes) — decompose into materials via");
        b.AppendLine("  mix_component. RCC concrete volume references a concrete grade here; steel is added directly.");
        b.AppendLine("  Concrete (dry factor 1.54, cement : fine : coarse):");
        foreach (var g in ConcreteGrades)
        {
            var mx = MaterialsCalculator.MixFor(g);
            b.AppendLine($"    • CONC_{g} — {g} ({Num(mx.Cement)} : {Num(mx.Sand)} : {Num(mx.Aggregate)})");
        }
        b.AppendLine("  Mortar / PCC (dry factor 1.33):");
        foreach (var mm in MortarMixes)
            b.AppendLine($"    • {mm.Key} — {mm.Name} ({Num(mm.Cement)} : {Num(mm.FineAgg)}"
                       + (mm.CoarseAgg > 0 ? $" : {Num(mm.CoarseAgg)}" : "") + ")");
        b.AppendLine("  '''");
        b.AppendLine("}");
        b.AppendLine();
        b.AppendLine("Table mix_component {");
        b.AppendLine("  id           varchar [pk]");
        b.AppendLine("  mix_key      varchar [ref: > mix_design.key]");
        b.AppendLine("  material_key varchar [ref: > material.key]");
        b.AppendLine("  parts        double  [note: 'Proportion by volume, e.g. cement 1 : sand 1 : aggregate 2']");
        b.AppendLine("  Note: 'One material line of a mix. e.g. CONC_M25 → CEMENT 1, FINE_AGG 1, COARSE_AGG 2.'");
        b.AppendLine("}");
        b.AppendLine();
        b.AppendLine("Table trade_material {");
        b.AppendLine("  id           varchar [pk]");
        b.AppendLine("  trade_key    varchar [ref: > trade.key, note: 'The work item that consumes the material']");
        b.AppendLine("  material_key varchar [ref: > material.key]");
        b.AppendLine("  via_mix_key  varchar [ref: > mix_design.key, null, note: 'Set when consumed through a mix (masonry → CM_1_6 → cement+sand); null for direct units (masonry → bricks)']");
        b.AppendLine("  note         varchar");
        b.AppendLine("  Note: 'Item → material edge. Direct (masonry → bricks, size-stone → stone) or through a mix (masonry / plaster → mortar → cement + sand; PCC → pcc mix).'");
        b.AppendLine("}");
        b.AppendLine();

        b.AppendLine("// ── Rates & priced estimate ──────────────────────────────────────────────────");
        b.AppendLine("Table rate_book_version {");
        b.AppendLine("  id    varchar [pk]");
        b.AppendLine("  name  varchar");
        b.AppendLine("  notes varchar");
        b.AppendLine("}");
        b.AppendLine();
        b.AppendLine("Table rate_item {");
        b.AppendLine("  id          varchar [pk]");
        b.AppendLine("  version_id  varchar [ref: > rate_book_version.id]");
        b.AppendLine("  code        varchar [note: 'Stable rate code, e.g. MSN-BRICK-M3, PL-STD, PT-STD']");
        b.AppendLine("  category    varchar");
        b.AppendLine("  description varchar");
        b.AppendLine("  unit        varchar");
        b.AppendLine("  rate        double");
        b.AppendLine();
        b.AppendLine("  indexes {");
        b.AppendLine("    (version_id, code) [unique]");
        b.AppendLine("  }");
        b.AppendLine("}");
        b.AppendLine();
        b.AppendLine("Table estimate {");
        b.AppendLine("  id                   varchar [pk]");
        b.AppendLine("  project_id           varchar [ref: > project.id]");
        b.AppendLine("  rate_book_version_id varchar [ref: > rate_book_version.id]");
        b.AppendLine("  base_total           double");
        b.AppendLine("  grand_total          double");
        b.AppendLine("}");
        b.AppendLine();
        b.AppendLine("Table estimate_line {");
        b.AppendLine("  id           varchar [pk]");
        b.AppendLine("  estimate_id  varchar [ref: > estimate.id]");
        b.AppendLine("  rate_item_id varchar [ref: > rate_item.id, null]");
        b.AppendLine("  code         varchar");
        b.AppendLine("  category     varchar");
        b.AppendLine("  description  varchar");
        b.AppendLine("  unit         varchar");
        b.AppendLine("  qty          double");
        b.AppendLine("  rate         double");
        b.AppendLine("  amount       double");
        b.AppendLine("  level_id     varchar [ref: > level.id, null]");
        b.AppendLine("  mark         varchar");
        b.AppendLine("  length_m     double");
        b.AppendLine("  breadth_m    double");
        b.AppendLine("  height_m     double");
        b.AppendLine("  area_m2      double");
        b.AppendLine("  volume_m3    double");
        b.AppendLine("  Note: 'Priced BOQ line: qty × rate = amount. Qty flows from takeoff_item / derived_item.'");
        b.AppendLine("}");

        return b.ToString();
    }

    /// <summary>The full ERD as SQL DDL, plus seed rows for the reference / library tables.</summary>
    public static string BuildSql()
    {
        var b = new StringBuilder();
        b.AppendLine("-- ============================================================================");
        b.AppendLine("-- O-CoreOp — estimation data model (logical ERD / schema)");
        b.AppendLine("-- Generated by SchemaExport.BuildSql() from the live registries; regenerate from");
        b.AppendLine("-- the app (Item links -> Export ERD). Portable ANSI-ish DDL.");
        b.AppendLine("-- Two item relationships: derivation (trade -> link_rule -> derived_item) and");
        b.AppendLine("-- composition (trade / mix_design -> material).");
        b.AppendLine("-- ============================================================================");
        b.AppendLine();
        b.AppendLine("CREATE TABLE trade (");
        b.AppendLine("    key           VARCHAR(64) NOT NULL,");
        b.AppendLine("    display       VARCHAR(128),");
        b.AppendLine("    unit          VARCHAR(16),");
        b.AppendLine("    default_basis VARCHAR(16),");
        b.AppendLine("    CONSTRAINT pk_trade PRIMARY KEY (key)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE project (");
        b.AppendLine("    id              VARCHAR(64) NOT NULL,");
        b.AppendLine("    name            VARCHAR(255),");
        b.AppendLine("    format_version  INTEGER,");
        b.AppendLine("    CONSTRAINT pk_project PRIMARY KEY (id)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE level (");
        b.AppendLine("    id                 VARCHAR(64) NOT NULL,");
        b.AppendLine("    project_id         VARCHAR(64) NOT NULL,");
        b.AppendLine("    name               VARCHAR(128),");
        b.AppendLine("    height_mm          INTEGER,");
        b.AppendLine("    slab_thickness_mm  INTEGER,");
        b.AppendLine("    beam_depth_mm      INTEGER,");
        b.AppendLine("    CONSTRAINT pk_level PRIMARY KEY (id),");
        b.AppendLine("    CONSTRAINT fk_level_project FOREIGN KEY (project_id) REFERENCES project (id)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE takeoff_item (");
        b.AppendLine("    id           VARCHAR(64) NOT NULL,");
        b.AppendLine("    project_id   VARCHAR(64) NOT NULL,");
        b.AppendLine("    trade_key    VARCHAR(64) NOT NULL,");
        b.AppendLine("    mark         VARCHAR(64),");
        b.AppendLine("    level_id     VARCHAR(64),");
        b.AppendLine("    length_m     DOUBLE PRECISION,");
        b.AppendLine("    breadth_m    DOUBLE PRECISION,");
        b.AppendLine("    height_m     DOUBLE PRECISION,");
        b.AppendLine("    area_m2      DOUBLE PRECISION,");
        b.AppendLine("    volume_m3    DOUBLE PRECISION,");
        b.AppendLine("    qty          DOUBLE PRECISION,");
        b.AppendLine("    unit         VARCHAR(16),");
        b.AppendLine("    source       VARCHAR(32),");
        b.AppendLine("    source_mark  VARCHAR(64),");
        b.AppendLine("    notes        TEXT,");
        b.AppendLine("    CONSTRAINT pk_takeoff_item PRIMARY KEY (id),");
        b.AppendLine("    CONSTRAINT fk_takeoff_project FOREIGN KEY (project_id) REFERENCES project (id),");
        b.AppendLine("    CONSTRAINT fk_takeoff_trade   FOREIGN KEY (trade_key)  REFERENCES trade (key),");
        b.AppendLine("    CONSTRAINT fk_takeoff_level   FOREIGN KEY (level_id)   REFERENCES level (id)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE link_rule (");
        b.AppendLine("    id            VARCHAR(64) NOT NULL,");
        b.AppendLine("    project_id    VARCHAR(64) NOT NULL,");
        b.AppendLine("    name          VARCHAR(255),");
        b.AppendLine("    enabled       BOOLEAN,");
        b.AppendLine("    source_trade  VARCHAR(64) NOT NULL,");
        b.AppendLine("    target_trade  VARCHAR(64) NOT NULL,");
        b.AppendLine("    basis         VARCHAR(16),");
        b.AppendLine("    factor        DOUBLE PRECISION,");
        b.AppendLine("    target_unit   VARCHAR(16),");
        b.AppendLine("    per_item      BOOLEAN,");
        b.AppendLine("    rate_code     VARCHAR(64),");
        b.AppendLine("    rate_override DOUBLE PRECISION,");
        b.AppendLine("    notes         TEXT,");
        b.AppendLine("    CONSTRAINT pk_link_rule PRIMARY KEY (id),");
        b.AppendLine("    CONSTRAINT fk_rule_project FOREIGN KEY (project_id)   REFERENCES project (id),");
        b.AppendLine("    CONSTRAINT fk_rule_source  FOREIGN KEY (source_trade) REFERENCES trade (key),");
        b.AppendLine("    CONSTRAINT fk_rule_target  FOREIGN KEY (target_trade) REFERENCES trade (key)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE derived_item (");
        b.AppendLine("    id            VARCHAR(64) NOT NULL,");
        b.AppendLine("    rule_id       VARCHAR(64) NOT NULL,");
        b.AppendLine("    source_trade  VARCHAR(64) NOT NULL,");
        b.AppendLine("    source_mark   VARCHAR(64),");
        b.AppendLine("    level_id      VARCHAR(64),");
        b.AppendLine("    source_qty    DOUBLE PRECISION,");
        b.AppendLine("    basis         VARCHAR(16),");
        b.AppendLine("    factor        DOUBLE PRECISION,");
        b.AppendLine("    target_trade  VARCHAR(64) NOT NULL,");
        b.AppendLine("    target_qty    DOUBLE PRECISION,");
        b.AppendLine("    target_unit   VARCHAR(16),");
        b.AppendLine("    chained       BOOLEAN,");
        b.AppendLine("    CONSTRAINT pk_derived_item PRIMARY KEY (id),");
        b.AppendLine("    CONSTRAINT fk_derived_rule   FOREIGN KEY (rule_id)      REFERENCES link_rule (id),");
        b.AppendLine("    CONSTRAINT fk_derived_source FOREIGN KEY (source_trade) REFERENCES trade (key),");
        b.AppendLine("    CONSTRAINT fk_derived_target FOREIGN KEY (target_trade) REFERENCES trade (key),");
        b.AppendLine("    CONSTRAINT fk_derived_level  FOREIGN KEY (level_id)     REFERENCES level (id)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE material (");
        b.AppendLine("    key       VARCHAR(64) NOT NULL,");
        b.AppendLine("    name      VARCHAR(255),");
        b.AppendLine("    unit      VARCHAR(16),");
        b.AppendLine("    category  VARCHAR(32),");
        b.AppendLine("    CONSTRAINT pk_material PRIMARY KEY (key)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE mix_design (");
        b.AppendLine("    key         VARCHAR(64) NOT NULL,");
        b.AppendLine("    kind        VARCHAR(16),");
        b.AppendLine("    grade       VARCHAR(32),");
        b.AppendLine("    dry_factor  DOUBLE PRECISION,");
        b.AppendLine("    note        VARCHAR(255),");
        b.AppendLine("    CONSTRAINT pk_mix_design PRIMARY KEY (key)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE mix_component (");
        b.AppendLine("    id            VARCHAR(64) NOT NULL,");
        b.AppendLine("    mix_key       VARCHAR(64) NOT NULL,");
        b.AppendLine("    material_key  VARCHAR(64) NOT NULL,");
        b.AppendLine("    parts         DOUBLE PRECISION,");
        b.AppendLine("    CONSTRAINT pk_mix_component PRIMARY KEY (id),");
        b.AppendLine("    CONSTRAINT fk_mixcomp_mix      FOREIGN KEY (mix_key)      REFERENCES mix_design (key),");
        b.AppendLine("    CONSTRAINT fk_mixcomp_material FOREIGN KEY (material_key) REFERENCES material (key)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE trade_material (");
        b.AppendLine("    id            VARCHAR(64) NOT NULL,");
        b.AppendLine("    trade_key     VARCHAR(64) NOT NULL,");
        b.AppendLine("    material_key  VARCHAR(64) NOT NULL,");
        b.AppendLine("    via_mix_key   VARCHAR(64),");
        b.AppendLine("    note          VARCHAR(255),");
        b.AppendLine("    CONSTRAINT pk_trade_material PRIMARY KEY (id),");
        b.AppendLine("    CONSTRAINT fk_trademat_trade    FOREIGN KEY (trade_key)    REFERENCES trade (key),");
        b.AppendLine("    CONSTRAINT fk_trademat_material FOREIGN KEY (material_key) REFERENCES material (key),");
        b.AppendLine("    CONSTRAINT fk_trademat_mix      FOREIGN KEY (via_mix_key)  REFERENCES mix_design (key)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE rate_book_version (");
        b.AppendLine("    id     VARCHAR(64) NOT NULL,");
        b.AppendLine("    name   VARCHAR(255),");
        b.AppendLine("    notes  TEXT,");
        b.AppendLine("    CONSTRAINT pk_rate_book_version PRIMARY KEY (id)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE rate_item (");
        b.AppendLine("    id           VARCHAR(64) NOT NULL,");
        b.AppendLine("    version_id   VARCHAR(64) NOT NULL,");
        b.AppendLine("    code         VARCHAR(64),");
        b.AppendLine("    category     VARCHAR(128),");
        b.AppendLine("    description  VARCHAR(255),");
        b.AppendLine("    unit         VARCHAR(16),");
        b.AppendLine("    rate         DOUBLE PRECISION,");
        b.AppendLine("    CONSTRAINT pk_rate_item PRIMARY KEY (id),");
        b.AppendLine("    CONSTRAINT uq_rate_item_code UNIQUE (version_id, code),");
        b.AppendLine("    CONSTRAINT fk_rate_item_version FOREIGN KEY (version_id) REFERENCES rate_book_version (id)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE estimate (");
        b.AppendLine("    id                    VARCHAR(64) NOT NULL,");
        b.AppendLine("    project_id            VARCHAR(64) NOT NULL,");
        b.AppendLine("    rate_book_version_id  VARCHAR(64),");
        b.AppendLine("    base_total            DOUBLE PRECISION,");
        b.AppendLine("    grand_total           DOUBLE PRECISION,");
        b.AppendLine("    CONSTRAINT pk_estimate PRIMARY KEY (id),");
        b.AppendLine("    CONSTRAINT fk_estimate_project FOREIGN KEY (project_id)           REFERENCES project (id),");
        b.AppendLine("    CONSTRAINT fk_estimate_version FOREIGN KEY (rate_book_version_id) REFERENCES rate_book_version (id)");
        b.AppendLine(");");
        b.AppendLine("CREATE TABLE estimate_line (");
        b.AppendLine("    id            VARCHAR(64) NOT NULL,");
        b.AppendLine("    estimate_id   VARCHAR(64) NOT NULL,");
        b.AppendLine("    rate_item_id  VARCHAR(64),");
        b.AppendLine("    code          VARCHAR(64),");
        b.AppendLine("    category      VARCHAR(128),");
        b.AppendLine("    description   VARCHAR(255),");
        b.AppendLine("    unit          VARCHAR(16),");
        b.AppendLine("    qty           DOUBLE PRECISION,");
        b.AppendLine("    rate          DOUBLE PRECISION,");
        b.AppendLine("    amount        DOUBLE PRECISION,");
        b.AppendLine("    level_id      VARCHAR(64),");
        b.AppendLine("    mark          VARCHAR(64),");
        b.AppendLine("    length_m      DOUBLE PRECISION,");
        b.AppendLine("    breadth_m     DOUBLE PRECISION,");
        b.AppendLine("    height_m      DOUBLE PRECISION,");
        b.AppendLine("    area_m2       DOUBLE PRECISION,");
        b.AppendLine("    volume_m3     DOUBLE PRECISION,");
        b.AppendLine("    CONSTRAINT pk_estimate_line PRIMARY KEY (id),");
        b.AppendLine("    CONSTRAINT fk_line_estimate  FOREIGN KEY (estimate_id)  REFERENCES estimate (id),");
        b.AppendLine("    CONSTRAINT fk_line_rate_item FOREIGN KEY (rate_item_id) REFERENCES rate_item (id),");
        b.AppendLine("    CONSTRAINT fk_line_level     FOREIGN KEY (level_id)     REFERENCES level (id)");
        b.AppendLine(");");
        b.AppendLine();

        // ── Seed: reference & library data (from the registries) ──
        b.AppendLine("-- Trades (LinkTradeRegistry)");
        foreach (var t in LinkTradeRegistry.Sources)
            b.AppendLine($"INSERT INTO trade(key,display,unit,default_basis) VALUES ({Q(t.Key)},{Q(t.Display)},{Q(t.Unit)},{Q(LinkBasisInfo.Label(t.DefaultBasis))});");
        b.AppendLine();
        b.AppendLine("-- Materials");
        foreach (var m in Materials)
            b.AppendLine($"INSERT INTO material(key,name,unit,category) VALUES ({Q(m.Key)},{Q(m.Name)},{Q(m.Unit)},{Q(m.Category)});");
        b.AppendLine();
        b.AppendLine("-- Mix designs + components");
        int cid = 1;
        foreach (var g in ConcreteGrades)
        {
            var mx = MaterialsCalculator.MixFor(g);
            string rk = "CONC_" + g;
            b.AppendLine($"INSERT INTO mix_design(key,kind,grade,dry_factor,note) VALUES ({Q(rk)},'concrete',{Q(g)},{Num(MaterialsCalculator.DryFactor)},{Q(mx.Note)});");
            b.AppendLine($"INSERT INTO mix_component(id,mix_key,material_key,parts) VALUES ('mc{cid++}',{Q(rk)},'CEMENT',{Num(mx.Cement)});");
            b.AppendLine($"INSERT INTO mix_component(id,mix_key,material_key,parts) VALUES ('mc{cid++}',{Q(rk)},'FINE_AGG',{Num(mx.Sand)});");
            b.AppendLine($"INSERT INTO mix_component(id,mix_key,material_key,parts) VALUES ('mc{cid++}',{Q(rk)},'COARSE_AGG',{Num(mx.Aggregate)});");
        }
        foreach (var mm in MortarMixes)
        {
            string kind = mm.CoarseAgg > 0 ? "pcc" : "mortar";
            b.AppendLine($"INSERT INTO mix_design(key,kind,grade,dry_factor,note) VALUES ({Q(mm.Key)},{Q(kind)},{Q(mm.Name)},1.33,{Q(mm.Name)});");
            b.AppendLine($"INSERT INTO mix_component(id,mix_key,material_key,parts) VALUES ('mc{cid++}',{Q(mm.Key)},'CEMENT',{Num(mm.Cement)});");
            b.AppendLine($"INSERT INTO mix_component(id,mix_key,material_key,parts) VALUES ('mc{cid++}',{Q(mm.Key)},'FINE_AGG',{Num(mm.FineAgg)});");
            if (mm.CoarseAgg > 0)
                b.AppendLine($"INSERT INTO mix_component(id,mix_key,material_key,parts) VALUES ('mc{cid++}',{Q(mm.Key)},'COARSE_AGG',{Num(mm.CoarseAgg)});");
        }
        b.AppendLine();
        b.AppendLine("-- Item -> material edges");
        int tm = 1;
        foreach (var e in TradeMaterials)
            b.AppendLine($"INSERT INTO trade_material(id,trade_key,material_key,via_mix_key,note) VALUES "
                       + $"('tm{tm++}',{Q(e.Trade)},{Q(e.Material)},{(string.IsNullOrEmpty(e.ViaMix) ? "NULL" : Q(e.ViaMix))},{Q(e.Note)});");
        b.AppendLine();
        b.AppendLine("-- Linked-item derivation — standard library (LinkRuleBook.Defaults)");
        foreach (var r in LinkRuleBook.Defaults())
            b.AppendLine("INSERT INTO link_rule(id,name,enabled,source_trade,target_trade,basis,factor,target_unit,per_item,rate_code,rate_override,notes) VALUES "
                       + $"({Q(r.Id)},{Q(r.Name)},{(r.Enabled ? "TRUE" : "FALSE")},{Q(r.SourceTrade)},{Q(r.TargetTrade)},{Q(LinkBasisInfo.Label(r.Basis))},{Num(r.Factor)},{Q(r.TargetUnit)},{(r.PerItem ? "TRUE" : "FALSE")},{Q(r.RateCodeOverride)},{Num(r.RateOverride)},{Q(r.Notes)});");

        return b.ToString();
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Q(string? s) => "'" + (s ?? "").Replace("'", "''") + "'";
}
