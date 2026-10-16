using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Data;
using MiniMes.Api.Domain;
using MiniMes.Api.Events;
using MiniMes.Api.Services;

namespace MiniMes.Api.Endpoints;

public record WorkOrderSummary(int Id, string Number, WorkOrderType Type, WorkOrderStatus Status, string? HoldReason,
    string SerialNumber, string PartNumber, string PartName, OperationRef? CurrentOperation,
    int CompletedOperations, int TotalOperations, int OpenNcrs, DateTimeOffset UpdatedAt);

public record OperationRef(int Sequence, string Name, string StationCode);

public record SerialRef(string SerialNumber, string PartNumber, string PartName, string Revision, string? LotNumber, SerialStatus Status);

public record SignOffDto(string SignedBy, DateTimeOffset SignedAt, string? Notes);

public record TravelerOperation(int Id, int Sequence, string Name, string StationCode, string StationName, bool IsRework,
    OperationStatus Status, bool IsCurrent, SignOffDto? SignOff, bool BlockedByNcr);

public record Traveler(int Id, string Number, WorkOrderType Type, WorkOrderStatus Status, string? HoldReason, SerialRef Serial,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, uint Version, IReadOnlyList<TravelerOperation> Operations,
    IReadOnlyList<NcrDto> Ncrs);

public record ReleaseWorkOrderRequest(string SerialNumber, WorkOrderType Type);
public record SignOffRequest(string SignedBy, string? Notes);
public record HoldRequest(string Reason);

public static class WorkOrderEndpoints
{
    public static void MapWorkOrders(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/work-orders").WithTags("Work orders");

        group.MapGet("/", async (MesDbContext db, WorkOrderStatus? status) =>
        {
            var query = db.WorkOrders.AsQueryable();
            if (status is not null) query = query.Where(w => w.Status == status);
            return await query
                .OrderByDescending(w => w.UpdatedAt)
                .Select(w => new WorkOrderSummary(
                    w.Id, w.Number, w.Type, w.Status, w.HoldReason,
                    w.Serial.SerialNumber, w.Serial.PartRevision.Part.PartNumber, w.Serial.PartRevision.Part.Name,
                    // Same ordering as WorkOrder.OrderedOperations(), expressed so EF can translate it to SQL.
                    w.Operations.Where(o => o.Status == OperationStatus.Pending)
                        .OrderBy(o => o.Sequence).ThenByDescending(o => o.IsRework).ThenBy(o => o.AddedAt)
                        .Select(o => new OperationRef(o.Sequence, o.Name, o.Station.Code))
                        .FirstOrDefault(),
                    w.Operations.Count(o => o.Status == OperationStatus.Complete),
                    w.Operations.Count,
                    w.Ncrs.Count(n => n.Status == NcrStatus.Open),
                    w.UpdatedAt))
                .ToListAsync();
        });

        group.MapGet("/{id:int}", async (int id, MesDbContext db, HttpContext http) =>
            WithETag(http, ToTraveler(await LoadAsync(db, id))));

        group.MapPost("/", async (ReleaseWorkOrderRequest req, MesDbContext db, Numbering numbering, TimeProvider clock, HttpContext http) =>
        {
            var serial = await db.Serials.SingleOrDefaultAsync(s => s.SerialNumber == req.SerialNumber)
                ?? throw new NotFoundException($"Serial {req.SerialNumber} not found");
            var routing = await db.Routings.Include(r => r.Operations)
                .SingleOrDefaultAsync(r => r.PartRevisionId == serial.PartRevisionId && r.Type == req.Type)
                ?? throw new DomainException($"No {req.Type} routing for the revision of {req.SerialNumber}");
            if (await db.WorkOrders.AnyAsync(w => w.SerialId == serial.Id &&
                    w.Status != WorkOrderStatus.Complete && w.Status != WorkOrderStatus.Cancelled))
                throw new DomainException($"{req.SerialNumber} already has an open work order");

            var wo = WorkOrder.Release(await numbering.NextWorkOrderAsync(), serial, routing, clock.GetUtcNow());
            db.WorkOrders.Add(wo);
            await db.SaveChangesAsync();

            var traveler = ToTraveler(await LoadAsync(db, wo.Id));
            http.Response.Headers.ETag = ETag(traveler.Version);
            return Results.Created($"/api/work-orders/{wo.Id}", traveler);
        });

        group.MapPost("/{id:int}/operations/{opId:int}/sign-off",
            async (int id, int opId, SignOffRequest req, MesDbContext db, TimeProvider clock, HttpContext http) =>
        {
            var wo = await LoadAsync(db, id);
            ExpectVersion(wo, http.Request);
            var op = wo.Operations.SingleOrDefault(o => o.Id == opId)
                ?? throw new NotFoundException($"Operation {opId} not found on {wo.Number}");

            var now = clock.GetUtcNow();
            wo.SignOff(op, req.SignedBy, req.Notes, now);
            db.Outbox.Add(OutboxMessage.Create("operation.signed_off", wo.Serial.SerialNumber, new
            {
                workOrder = wo.Number,
                serial = wo.Serial.SerialNumber,
                station = op.Station.Code,
                operation = new { op.Sequence, op.Name, op.IsRework },
                signedBy = req.SignedBy,
                workOrderStatus = wo.Status,
                at = now,
            }, now));
            await db.SaveChangesAsync();
            return WithETag(http, ToTraveler(wo));
        });

        group.MapPost("/{id:int}/hold", async (int id, HoldRequest req, MesDbContext db, TimeProvider clock, HttpContext http) =>
        {
            var wo = await LoadAsync(db, id);
            ExpectVersion(wo, http.Request);
            wo.Hold(req.Reason, clock.GetUtcNow());
            await db.SaveChangesAsync();
            return WithETag(http, ToTraveler(wo));
        });

        group.MapPost("/{id:int}/resume", async (int id, MesDbContext db, TimeProvider clock, HttpContext http) =>
        {
            var wo = await LoadAsync(db, id);
            ExpectVersion(wo, http.Request);
            wo.Resume(clock.GetUtcNow());
            await db.SaveChangesAsync();
            return WithETag(http, ToTraveler(wo));
        });
    }

    internal static async Task<WorkOrder> LoadAsync(MesDbContext db, int id) =>
        await db.WorkOrders
            .Include(w => w.Serial).ThenInclude(s => s.PartRevision).ThenInclude(r => r.Part)
            .Include(w => w.Operations).ThenInclude(o => o.Station)
            .Include(w => w.Operations).ThenInclude(o => o.SignOff)
            .Include(w => w.Ncrs).ThenInclude(n => n.Serial).ThenInclude(s => s.PartRevision).ThenInclude(r => r.Part)
            // One statement, so the version and the operations come from the same snapshot. With AsSplitQuery a
            // concurrent sign-off could commit between the queries: old version, but operations already complete.
            .AsSingleQuery()
            .SingleOrDefaultAsync(w => w.Id == id)
        ?? throw new NotFoundException($"Work order {id} not found");

    /// <summary>
    /// Optimistic concurrency, in two layers. The client echoes the version it loaded in If-Match; if the row has
    /// moved on since, reject before applying any business rules (412). If someone else commits between this load
    /// and our save, EF's UPDATE ... WHERE xmin = @loaded matches no row and the save fails, also as 412.
    /// </summary>
    private static void ExpectVersion(WorkOrder wo, HttpRequest request)
    {
        var header = request.Headers.IfMatch.ToString();
        if (string.IsNullOrEmpty(header))
            throw new HttpProblemException(StatusCodes.Status428PreconditionRequired, "If-Match header with the traveler version is required");
        if (!uint.TryParse(header.Replace("W/", "").Trim('"'), out var version))
            throw new HttpProblemException(StatusCodes.Status400BadRequest, "Malformed If-Match header");
        if (version != wo.Version)
            throw new HttpProblemException(StatusCodes.Status412PreconditionFailed,
                "The work order was changed by someone else. Reload the traveler and try again.");
    }

    private static string ETag(uint version) => $"\"{version}\"";

    private static Traveler WithETag(HttpContext http, Traveler traveler)
    {
        http.Response.Headers.ETag = ETag(traveler.Version);
        return traveler;
    }

    internal static SerialRef ToSerialRef(SerializedPart s) => new(s.SerialNumber, s.PartRevision.Part.PartNumber,
        s.PartRevision.Part.Name, s.PartRevision.Revision, s.LotNumber, s.Status);

    private static Traveler ToTraveler(WorkOrder wo)
    {
        var current = wo.CurrentOperation;
        var blocked = wo.Ncrs.Where(n => n.Status == NcrStatus.Open).Select(n => n.WorkOrderOperationId).ToHashSet();
        var ops = wo.OrderedOperations().Select(o => new TravelerOperation(
            o.Id, o.Sequence, o.Name, o.Station.Code, o.Station.Name, o.IsRework, o.Status, o == current,
            o.SignOff is { } s ? new SignOffDto(s.SignedBy, s.SignedAt, s.Notes) : null,
            blocked.Contains(o.Id))).ToList();
        return new Traveler(wo.Id, wo.Number, wo.Type, wo.Status, wo.HoldReason, ToSerialRef(wo.Serial), wo.CreatedAt,
            wo.CompletedAt, wo.Version, ops, wo.Ncrs.OrderByDescending(n => n.RaisedAt).Select(NcrEndpoints.ToDto).ToList());
    }
}
