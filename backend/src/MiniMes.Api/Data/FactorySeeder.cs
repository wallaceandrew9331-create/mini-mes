using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Domain;
using MiniMes.Api.Services;

namespace MiniMes.Api.Data;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies migrations and seeds an empty database. Migrating on startup keeps `docker compose up`
    /// one step; a production deployment would run migrations as a separate release step instead.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<MesDbContext>();
        await db.Database.MigrateAsync();
        if (config.GetValue("Seed:Enabled", true) && !await db.Parts.AnyAsync())
        {
            await new FactorySeeder(db, sp.GetRequiredService<Numbering>(), sp.GetRequiredService<GenealogyService>(),
                sp.GetRequiredService<FlightService>()).SeedAsync();
        }
    }
}

/// <summary>
/// Seeds a small booster factory with ten months of history, driven through the same domain methods and
/// services the API uses, so the seed also exercises the business rules (flight readiness included).
///
/// The story:
///   B-07 flew three times. After flight 2, a turbopump on EN-0103 was scrapped (NCR), so EN-0103 was swapped
///        for spare EN-0110. It is now in refurb with an open NCR on a grid fin, and EN-0102 is due for refurb.
///   B-09 flew once, was refurbished, and is flight-ready.
///   B-11 is in build, waiting on its third engine (EN-0109, at hot-fire test).
///   Valve lot L26-0412 is the suspect lot for the recall query: it flew on B-07 and B-09 and sits in B-11.
/// </summary>
public class FactorySeeder(MesDbContext db, Numbering numbering, GenealogyService genealogy, FlightService flights)
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 5, 15, 0, 0, TimeSpan.Zero);
    private static readonly string[] Technicians = ["J. Alvarez", "K. Okafor", "M. Chen", "R. Patel", "S. Novak", "T. Brooks"];

    private readonly Dictionary<string, Station> stations = [];
    private readonly Dictionary<string, PartRevision> revisions = [];
    private readonly Dictionary<(PartRevision, WorkOrderType), Routing> routings = [];
    private readonly Dictionary<string, int> serialCounters = [];
    private int technician;

    private static DateTimeOffset Day(double day) => Epoch.AddDays(day);
    private string NextTechnician() => Technicians[technician++ % Technicians.Length];

    public async Task SeedAsync()
    {
        await SeedReferenceDataAsync();

        // January: B-07 and its engines. EN-0101's valve install needed rework.
        var en0101 = await BuildEngineAsync("A", "L26-0412", day: 5, stopAfter: 2);
        var en0101Build = db.WorkOrders.Local.Single(w => w.Serial == en0101);
        var ncr1 = await RaiseNcrAsync(Installed(en0101, "MOV"), en0101Build,
            "MOV inlet flange bolts torqued to 38 Nm; drawing calls for 45 +/- 2 Nm", day: 7);
        ncr1.ApplyDisposition(Disposition.Rework, "MRB: R. Patel", "Re-torque flange bolts to 45 Nm and re-stripe", Day(7.5));
        SignOffOps(en0101Build, fromDay: 8); // the rework step, then the rest of the traveler
        await db.SaveChangesAsync();
        var en0102 = await BuildEngineAsync("A", "L26-0412", day: 6);
        var en0103 = await BuildEngineAsync("A", "L26-0519", day: 7);
        var b07 = await BuildBoosterAsync("B-07", [en0101, en0102, en0103], day: 15);

        // March: first flight and turnaround. April: B-09.
        await flights.RecordAsync("B-07", "Constellation Batch 41", Day(68));
        await RefurbAsync(b07, day: 70);
        var en0104 = await BuildEngineAsync("A", "L26-0519", day: 90);
        var en0105 = await BuildEngineAsync("A", "L26-0412", day: 91);
        var en0106 = await BuildEngineAsync("A", "L26-0519", day: 92);
        var b09 = await BuildBoosterAsync("B-09", [en0104, en0105, en0106], day: 100);

        // May: second flight. Post-flight borescope finds turbopump erosion on EN-0103: scrap the pump, swap the engine.
        await flights.RecordAsync("B-07", "Rideshare Transporter-4", Day(135));
        var b07Refurb2 = await ReleaseAsync(b07, WorkOrderType.Refurb, day: 137);
        await RefurbAsync(en0101, day: 138);
        var tp = Installed(en0103, "TP");
        var ncr2 = await RaiseNcrAsync(tp, b07Refurb2, "Turbine blade tip erosion beyond limit on 3 blades (borescope)", day: 138.3);
        ncr2.ApplyDisposition(Disposition.Scrap, "MRB: K. Okafor", "Erosion exceeds repair limits", Day(139));
        await genealogy.RemoveAsync(tp.SerialNumber, "K. Okafor", $"Scrapped per {ncr2.Number}", Day(139));
        SignOffOps(b07Refurb2, fromDay: 140, count: 1);
        await genealogy.RemoveAsync(en0103.SerialNumber, "S. Novak", $"Engine swap: turbopump scrapped per {ncr2.Number}", Day(141));
        await db.SaveChangesAsync();

        var en0110 = await BuildEngineAsync("A", "L26-0519", day: 140, serialNumber: "EN-0110");
        await InstallAsync(b07, [en0110], day: 148);
        SignOffOps(b07Refurb2, fromDay: 148);
        await db.SaveChangesAsync();

        var en0103Refurb = await RefurbAsync(en0103, day: 153, stopAfter: 1);
        en0103Refurb.Hold($"Awaiting replacement turbopump ({ncr2.Number})", Day(156));
        await db.SaveChangesAsync();

        // August: third flight. B-07 goes back into refurb; EN-0102 has now hit its 3-flight refurb interval.
        await flights.RecordAsync("B-07", "Constellation Batch 47", Day(209));
        var b07Refurb3 = await RefurbAsync(b07, day: 212, stopAfter: 1);

        // September: B-09 flies and is turned around. B-11's first two engines are built.
        var en0107 = await BuildEngineAsync("B", "L26-0807", day: 230);
        var en0108 = await BuildEngineAsync("A", "L26-0412", day: 236);
        await flights.RecordAsync("B-09", "Constellation Batch 52", Day(250));
        await RefurbAsync(b09, day: 253);

        // October: B-11 waits on EN-0109 (at hot-fire); EN-0111 just released; an open NCR holds up B-07.
        await BuildBoosterAsync("B-11", [en0107, en0108], day: 260, complete: false);
        await BuildEngineAsync("B", "L26-0807", day: 270, stopAfter: 4, serialNumber: "EN-0109");
        var (en0111, _) = NewEngine("B", "L26-0807", day: 281, serialNumber: "EN-0111");
        await db.SaveChangesAsync();
        await ReleaseAsync(en0111, WorkOrderType.Build, day: 282);
        await RaiseNcrAsync(Installed(b07, "GF"), b07Refurb3, "Hydraulic seep at grid fin actuator rod seal", day: 283);

        // Stock on the shelf: a spare valve from the suspect lot and a replacement turbopump for EN-0103.
        NewSerial("VLV-115/A", "MOV", "L26-0412", day: 20);
        NewSerial("TP-20/A", "TP", "TP-L26-02", day: 270);
        await db.SaveChangesAsync();
    }

    private async Task SeedReferenceDataAsync()
    {
        foreach (var (code, name) in new[]
                 {
                     ("RCV", "Receiving Inspection"), ("WLD", "Weld Cell 2"), ("ENG", "Engine Integration"),
                     ("TST", "Test Stand"), ("INT", "Vehicle Integration"), ("QA", "Final Inspection"),
                     ("RFB", "Refurbishment Bay"),
                 })
        {
            stations[code] = db.Stations.Add(new Station { Code = code, Name = name }).Entity;
        }

        AddPart("BK-1", "Booster", PartKind.Vehicle, null, "C");
        AddPart("EN-2", "Methalox Engine", PartKind.Assembly, 3, "B");
        AddPart("TP-20", "Turbopump Assembly", PartKind.Component, 5, "A");
        AddPart("VLV-115", "Main Oxidizer Valve", PartKind.Component, 6, "A", "B");
        AddPart("TVC-31", "TVC Actuator", PartKind.Component, 10, "A");
        AddPart("GF-4", "Grid Fin", PartKind.Component, null, "D");
        AddPart("CV-50", "COPV", PartKind.Component, 10, "A");

        AddRouting("EN-2/B", WorkOrderType.Build, "Engine build",
            ("Kit receiving inspection", "RCV"), ("Install turbopump", "ENG"), ("Install main oxidizer valve", "ENG"),
            ("Install TVC actuator", "ENG"), ("Hot-fire acceptance test", "TST"), ("Final inspection", "QA"));
        AddRouting("EN-2/B", WorkOrderType.Refurb, "Engine refurbishment",
            ("Post-flight borescope", "RFB"), ("Turbopump inspection", "RFB"), ("Leak check", "TST"), ("Return to service", "QA"));
        AddRouting("BK-1/C", WorkOrderType.Build, "Booster build",
            ("Tank weld and X-ray", "WLD"), ("Cryo proof test", "TST"), ("Install COPVs", "INT"),
            ("Install grid fins", "INT"), ("Install engines", "INT"), ("Final inspection", "QA"));
        AddRouting("BK-1/C", WorkOrderType.Refurb, "Booster refurbishment",
            ("Post-flight inspection", "RFB"), ("Grid fin actuator check", "RFB"), ("Leak check", "TST"), ("Return to service", "QA"));

        await db.SaveChangesAsync();
    }

    private void AddPart(string partNumber, string name, PartKind kind, int? refurbInterval, params string[] revs)
    {
        var part = new Part { PartNumber = partNumber, Name = name, Kind = kind, RefurbIntervalFlights = refurbInterval };
        for (var i = 0; i < revs.Length; i++)
        {
            var rev = new PartRevision { Part = part, Revision = revs[i], ReleasedAt = Epoch.AddDays(-200 + 120 * i) };
            part.Revisions.Add(rev);
            revisions[$"{partNumber}/{revs[i]}"] = rev;
        }
        db.Parts.Add(part);
    }

    private void AddRouting(string revision, WorkOrderType type, string name, params (string Name, string Station)[] ops)
    {
        var routing = new Routing
        {
            PartRevision = revisions[revision],
            Type = type,
            Name = name,
            Operations = ops.Select((o, i) => new RoutingOperation { Sequence = (i + 1) * 10, Name = o.Name, Station = stations[o.Station] }).ToList(),
        };
        routings[(routing.PartRevision, type)] = routing;
        db.Routings.Add(routing);
    }

    private SerializedPart NewSerial(string revision, string prefix, string? lot, double day, string? serialNumber = null)
    {
        if (serialNumber is null)
        {
            var next = serialCounters.GetValueOrDefault(prefix, prefix == "EN" ? 100 : 1000) + 1;
            serialCounters[prefix] = next;
            serialNumber = $"{prefix}-{next:0000}";
        }
        var serial = new SerializedPart
        {
            SerialNumber = serialNumber,
            PartRevision = revisions[revision],
            LotNumber = lot,
            Status = SerialStatus.Serviceable,
            CreatedAt = Day(day),
        };
        db.Serials.Add(serial);
        return serial;
    }

    private (SerializedPart Engine, SerializedPart[] Kit) NewEngine(string valveRev, string valveLot, double day, string? serialNumber = null)
    {
        var engine = NewSerial("EN-2/B", "EN", null, day, serialNumber);
        SerializedPart[] kit =
        [
            NewSerial("TP-20/A", "TP", "TP-L26-01", day - 3),
            NewSerial($"VLV-115/{valveRev}", "MOV", valveLot, day - 3),
            NewSerial("TVC-31/A", "TVC", "TVC-L26-A", day - 3),
        ];
        return (engine, kit);
    }

    private async Task<SerializedPart> BuildEngineAsync(string valveRev, string valveLot, double day, int stopAfter = int.MaxValue,
        string? serialNumber = null)
    {
        var (engine, kit) = NewEngine(valveRev, valveLot, day, serialNumber);
        await db.SaveChangesAsync();
        var wo = await ReleaseAsync(engine, WorkOrderType.Build, day);
        await InstallAsync(engine, kit, day + 0.8);
        SignOffOps(wo, day, stopAfter);
        await db.SaveChangesAsync();
        return engine;
    }

    private async Task<SerializedPart> BuildBoosterAsync(string serialNumber, SerializedPart[] engines, double day, bool complete = true)
    {
        var booster = NewSerial("BK-1/C", "B", null, day, serialNumber);
        var copvs = Enumerable.Range(0, 2).Select(_ => NewSerial("CV-50/A", "CV", "CV-26-117", day - 5)).ToArray();
        var fins = Enumerable.Range(0, 4).Select(_ => NewSerial("GF-4/D", "GF", "GF-26-03", day - 5)).ToArray();
        await db.SaveChangesAsync();

        var wo = await ReleaseAsync(booster, WorkOrderType.Build, day);
        SignOffOps(wo, day + 1, count: 2);            // weld, proof test
        await InstallAsync(booster, copvs, day + 3.5);
        SignOffOps(wo, day + 4, count: 1);
        await InstallAsync(booster, fins, day + 5.5);
        SignOffOps(wo, day + 6, count: 1);
        await InstallAsync(booster, engines, day + 7.5);
        if (complete) SignOffOps(wo, day + 8);         // install engines, final inspection
        await db.SaveChangesAsync();
        return booster;
    }

    private async Task<WorkOrder> RefurbAsync(SerializedPart unit, double day, int stopAfter = int.MaxValue)
    {
        var wo = await ReleaseAsync(unit, WorkOrderType.Refurb, day);
        SignOffOps(wo, day + 1, stopAfter);
        await db.SaveChangesAsync();
        return wo;
    }

    private async Task<WorkOrder> ReleaseAsync(SerializedPart unit, WorkOrderType type, double day)
    {
        var wo = WorkOrder.Release(await numbering.NextWorkOrderAsync(), unit, routings[(unit.PartRevision, type)], Day(day));
        db.WorkOrders.Add(wo);
        await db.SaveChangesAsync();
        return wo;
    }

    /// <summary>Signs off the next <paramref name="count"/> operations, one per day.</summary>
    private void SignOffOps(WorkOrder wo, double fromDay, int count = int.MaxValue)
    {
        for (var i = 0; i < count && wo.CurrentOperation is { } op; i++)
            wo.SignOff(op, NextTechnician(), null, Day(fromDay + i));
    }

    private async Task InstallAsync(SerializedPart parent, IEnumerable<SerializedPart> children, double day)
    {
        foreach (var child in children)
            await genealogy.InstallAsync(parent.SerialNumber, child.SerialNumber, NextTechnician(), Day(day));
        await db.SaveChangesAsync();
    }

    private async Task<Ncr> RaiseNcrAsync(SerializedPart serial, WorkOrder wo, string description, double day)
    {
        var ncr = Ncr.Raise(await numbering.NextNcrAsync(), serial, wo, wo.CurrentOperation, description, NextTechnician(), Day(day));
        db.Ncrs.Add(ncr);
        await db.SaveChangesAsync();
        return ncr;
    }

    /// <summary>First serial with the given prefix currently installed directly in <paramref name="parent"/>.</summary>
    private SerializedPart Installed(SerializedPart parent, string prefix) =>
        db.Installations.Local
            .Where(i => i.ParentId == parent.Id && i.RemovedAt is null && i.Child.SerialNumber.StartsWith(prefix + "-"))
            .OrderBy(i => i.Child.SerialNumber)
            .Select(i => i.Child)
            .First();
}
