// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Aqc.CadImport;
using Office.Shared;
using Xunit;
using Xunit.Abstractions;

namespace AQC.CadImport.Tests;

/// <summary>Headless CoreOp import plan for a DEMO take-off .osp (set <c>DEMO_OSP_PATH</c>).</summary>
public sealed class DemoDwgQuantityTests(ITestOutputHelper log)
{
    static List<TakeoffRecord> RecordsFromOsp(string path)
    {
        using var odp = OdpFile.Open(path);
        var list = new List<TakeoffRecord>();
        static ulong Id(int kind, uint slot, uint n) => ((ulong)kind << 56) | ((ulong)slot << 32) | n;
        foreach (var h in odp.TakeoffHeaders())
        {
            list.Add(new TakeoffRecord(Id(1, h.Slot, 0), 0, 0, 0, 0, h.Json));
            foreach (var r in odp.TakeoffRows(h.Slot))
                list.Add(new TakeoffRecord(Id(r.Kind, r.Slot, r.N), 0, 0, 0, 0, r.Json));
        }
        return list;
    }

    [Fact]
    public void Import_plan_from_demo_osp_when_path_set()
    {
        var path = Environment.GetEnvironmentVariable("DEMO_OSP_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            log.WriteLine("Skip: DEMO_OSP_PATH not set or missing.");
            return;
        }

        var recs = RecordsFromOsp(path);
        var drawings = OspTakeoff.ListDrawings(recs);
        Assert.NotEmpty(drawings);
        var d = drawings.First(x => x.IsComplete);
        log.WriteLine($"Drawing: {d.DisplayName} slot={d.Slot} rows={d.Rows}");
        foreach (var kv in d.Counts.OrderBy(k => k.Key))
            if (kv.Key is not (OspTakeoff.KindHeader or OspTakeoff.KindAnnotatedChunk))
                log.WriteLine($"  kind {kv.Key}: {kv.Value}");

        var read = OspTakeoff.Read(recs.Where(r => OspTakeoff.IsTakeoffRecord(r.Id)), d.Slot);
        var m = new ImportModel();
        m.Levels.Add(new LevelRec { Id = "Lvl0", Name = "Plinth", HeightMm = 3200, SlabThicknessMm = 150, BeamDepthMm = 450 });
        var plan = CadImporter.Plan(read.File, m);
        log.WriteLine(plan.Summary);
        log.WriteLine($"Bridge walls={read.File.Walls.Count} slabs={read.File.Slabs.Count} openings={read.File.Openings.Count} columns={read.File.Columns.Count}");

        Assert.True(read.File.Walls.Count > 0, "expected wall rows from DEMO take-off");
        Assert.True(plan.Count(PlanAction.Add) > 0, plan.Summary);
    }
}
