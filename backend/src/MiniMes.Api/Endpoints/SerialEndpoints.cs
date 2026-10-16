using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Data;
using MiniMes.Api.Domain;
using MiniMes.Api.Queries;
using MiniMes.Api.Services;

namespace MiniMes.Api.Endpoints;

public record CreateSerialRequest(string PartNumber, string Revision, string SerialNumber, string? LotNumber);
public record InstallRequest(string ChildSerialNumber, string By);
public record RemoveRequest(string By, string Reason);

public record GenealogyResponse(string SerialNumber, IReadOnlyList<GenealogyNodeDto> Nodes, IReadOnlyList<string>? FlightReadinessProblems);
public record GenealogyNodeDto(int Id, int? ParentId, int Depth, string SerialNumber, string PartNumber, string PartName,
    string PartKind, string Revision, string? LotNumber, string Status, int? RefurbIntervalFlights, int TotalFlights,
    int FlightsSinceRefurb, bool RefurbDue, int OpenNcrs);

public record HistoryEvent(DateTimeOffset At, string Kind, string Title, string? Detail, int? WorkOrderId);
public record HistoryResponse(SerialRef Serial, int TotalFlights, int FlightsSinceRefurb, int? RefurbIntervalFlights,
    string? InstalledIn, IReadOnlyList<HistoryEvent> Events);

public static class SerialEndpoints
{
    public static void MapSerials(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/serials").WithTags("Serials & genealogy");

        group.MapGet("/", async (MesDbContext db, string? q) =>
        {
            var query = db.Serials.Include(s => s.PartRevision).ThenInclude(r => r.Part).AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(s => EF.Functions.ILike(s.SerialNumber, $"%{q}%") || EF.Functions.ILike(s.PartRevision.Part.PartNumber, $"%{q}%"));
            var serials = await query.OrderBy(s => s.SerialNumber).Take(25).ToListAsync();
            return serials.Select(WorkOrderEndpoints.ToSerialRef);
        });

        group.MapPost("/", async (CreateSerialRequest req, MesDbContext db, TimeProvider clock) =>
        {
            var revision = await db.PartRevisions.Include(r => r.Part)
                .SingleOrDefaultAsync(r => r.Part.PartNumber == req.PartNumber && r.Revision == req.Revision)
                ?? throw new NotFoundException($"Part {req.PartNumber} rev {req.Revision} not found");
            var serial = new SerializedPart
            {
                SerialNumber = req.SerialNumber.Trim(),
                PartRevision = revision,
                LotNumber = req.LotNumber,
                Status = SerialStatus.Serviceable,
                CreatedAt = clock.GetUtcNow(),
            };
            db.Serials.Add(serial);
            await db.SaveChangesAsync();
            return Results.Created($"/api/serials/{serial.SerialNumber}", WorkOrderEndpoints.ToSerialRef(serial));
        });

        group.MapGet("/{serialNumber}/genealogy", async (string serialNumber, GenealogyQueries queries) =>
        {
            var tree = await queries.TreeAsync(serialNumber);
            if (tree.Count == 0) throw new NotFoundException($"Serial {serialNumber} not found");
            var isVehicle = tree[0].PartKind == nameof(PartKind.Vehicle);
            return new GenealogyResponse(serialNumber, tree.Select(ToDto).ToList(), isVehicle ? FlightReadiness.Check(tree) : null);
        });

        group.MapGet("/{serialNumber}/history", GetHistoryAsync);

        group.MapPost("/{serialNumber}/children", async (string serialNumber, InstallRequest req, GenealogyService genealogy,
            MesDbContext db, TimeProvider clock) =>
        {
            await genealogy.InstallAsync(serialNumber, req.ChildSerialNumber, req.By, clock.GetUtcNow());
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/{serialNumber}/remove", async (string serialNumber, RemoveRequest req, GenealogyService genealogy,
            MesDbContext db, TimeProvider clock) =>
        {
            if (string.IsNullOrWhiteSpace(req.Reason)) throw new DomainException("Removal requires a reason");
            _ = await genealogy.RemoveAsync(serialNumber, req.By, req.Reason, clock.GetUtcNow())
                ?? throw new DomainException($"{serialNumber} is not installed in anything");
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    /// <summary>Everything that ever happened to one serial, oldest first: the "flight + refurb history" query.</summary>
    private static async Task<HistoryResponse> GetHistoryAsync(string serialNumber, MesDbContext db, GenealogyQueries queries)
    {
        var serial = await db.Serials.Include(s => s.PartRevision).ThenInclude(r => r.Part)
            .SingleOrDefaultAsync(s => s.SerialNumber == serialNumber)
            ?? throw new NotFoundException($"Serial {serialNumber} not found");
        var usage = (await queries.TreeAsync(serialNumber))[0];

        var events = new List<HistoryEvent>
        {
            new(serial.CreatedAt, "created", "Serial created",
                $"{serial.PartRevision.Part.PartNumber} rev {serial.PartRevision.Revision}" + (serial.LotNumber is null ? "" : $", lot {serial.LotNumber}"), null),
        };

        var installs = await db.Installations.Include(i => i.Parent).Include(i => i.Child)
            .Where(i => i.ChildId == serial.Id || i.ParentId == serial.Id).ToListAsync();
        foreach (var i in installs)
        {
            var asChild = i.ChildId == serial.Id;
            events.Add(new(i.InstalledAt, "install",
                asChild ? $"Installed in {i.Parent.SerialNumber}" : $"{i.Child.SerialNumber} installed", $"by {i.InstalledBy}", null));
            if (i.RemovedAt is { } removedAt)
                events.Add(new(removedAt, "remove",
                    asChild ? $"Removed from {i.Parent.SerialNumber}" : $"{i.Child.SerialNumber} removed", $"{i.RemovalReason} (by {i.RemovedBy})", null));
        }

        var workOrders = await db.WorkOrders.Where(w => w.SerialId == serial.Id).ToListAsync();
        foreach (var w in workOrders)
        {
            events.Add(new(w.CreatedAt, w.Type == WorkOrderType.Refurb ? "refurb" : "build", $"{w.Number} {w.Type} released", null, w.Id));
            if (w.CompletedAt is { } completedAt)
                events.Add(new(completedAt, w.Type == WorkOrderType.Refurb ? "refurb" : "build", $"{w.Number} {w.Type} complete",
                    w.Type == WorkOrderType.Refurb ? "Flights-since-refurb reset to 0" : null, w.Id));
        }

        var flights = await db.FlightManifest.Where(m => m.SerialId == serial.Id)
            .Join(db.Flights, m => m.FlightId, f => f.Id, (m, f) => new { f.FlownAt, f.FlightNumber, f.Mission, Vehicle = f.Vehicle.SerialNumber })
            .OrderBy(f => f.FlownAt).ToListAsync();
        for (var n = 0; n < flights.Count; n++)
        {
            var f = flights[n];
            events.Add(new(f.FlownAt, "flight", $"Flew on {f.Vehicle} flight {f.FlightNumber}", $"{f.Mission} (flight {n + 1} for this part)", null));
        }

        var ncrs = await db.Ncrs.Where(n => n.SerialId == serial.Id).ToListAsync();
        foreach (var n in ncrs)
        {
            events.Add(new(n.RaisedAt, "ncr", $"{n.Number} raised", n.Description, n.WorkOrderId));
            if (n.DispositionedAt is { } at)
                events.Add(new(at, "ncr", $"{n.Number} dispositioned {n.Disposition}", n.DispositionNotes, n.WorkOrderId));
        }

        var installedIn = installs.FirstOrDefault(i => i.ChildId == serial.Id && i.RemovedAt is null)?.Parent.SerialNumber;
        return new HistoryResponse(WorkOrderEndpoints.ToSerialRef(serial), usage.TotalFlights, usage.FlightsSinceRefurb,
            usage.RefurbIntervalFlights, installedIn, events.OrderBy(e => e.At).ToList());
    }

    private static GenealogyNodeDto ToDto(GenealogyNode n) => new(n.Id, n.ParentId, n.Depth, n.SerialNumber, n.PartNumber,
        n.PartName, n.PartKind, n.Revision, n.LotNumber, n.Status, n.RefurbIntervalFlights, n.TotalFlights,
        n.FlightsSinceRefurb, n.RefurbDue, n.OpenNcrs);
}
