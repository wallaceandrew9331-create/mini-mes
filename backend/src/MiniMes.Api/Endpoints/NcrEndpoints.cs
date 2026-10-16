using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Data;
using MiniMes.Api.Domain;
using MiniMes.Api.Events;
using MiniMes.Api.Services;

namespace MiniMes.Api.Endpoints;

public record NcrDto(int Id, string Number, string SerialNumber, string PartNumber, int? WorkOrderId, int? OperationId,
    string Description, string RaisedBy, DateTimeOffset RaisedAt, NcrStatus Status, Disposition? Disposition,
    string? DispositionedBy, DateTimeOffset? DispositionedAt, string? DispositionNotes);

public record RaiseNcrRequest(string SerialNumber, int? WorkOrderId, int? OperationId, string Description, string RaisedBy);
public record DispositionRequest(Disposition Disposition, string By, string? Notes);

public static class NcrEndpoints
{
    public static void MapNcrs(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/ncrs").WithTags("NCRs");

        group.MapGet("/", async (MesDbContext db, NcrStatus? status) =>
        {
            var query = db.Ncrs.Include(n => n.Serial).ThenInclude(s => s.PartRevision).ThenInclude(r => r.Part).AsQueryable();
            if (status is not null) query = query.Where(n => n.Status == status);
            var ncrs = await query.OrderByDescending(n => n.RaisedAt).ToListAsync();
            return ncrs.Select(ToDto);
        });

        group.MapPost("/", async (RaiseNcrRequest req, MesDbContext db, Numbering numbering, TimeProvider clock) =>
        {
            var serial = await db.Serials.Include(s => s.PartRevision).ThenInclude(r => r.Part)
                .SingleOrDefaultAsync(s => s.SerialNumber == req.SerialNumber)
                ?? throw new NotFoundException($"Serial {req.SerialNumber} not found");

            WorkOrder? wo = null;
            WorkOrderOperation? op = null;
            if (req.WorkOrderId is int woId)
            {
                wo = await WorkOrderEndpoints.LoadAsync(db, woId);
                if (req.OperationId is int opId)
                    op = wo.Operations.SingleOrDefault(o => o.Id == opId)
                        ?? throw new NotFoundException($"Operation {opId} not found on {wo.Number}");
            }

            var now = clock.GetUtcNow();
            var ncr = Ncr.Raise(await numbering.NextNcrAsync(), serial, wo, op, req.Description, req.RaisedBy, now);
            db.Ncrs.Add(ncr);
            db.Outbox.Add(OutboxMessage.Create("ncr.raised", serial.SerialNumber, new
            {
                ncr = ncr.Number,
                serial = serial.SerialNumber,
                workOrder = wo?.Number,
                station = op?.Station.Code,
                ncr.Description,
                ncr.RaisedBy,
                at = now,
            }, now));
            await db.SaveChangesAsync();
            return Results.Created($"/api/ncrs/{ncr.Id}", ToDto(ncr));
        });

        group.MapPost("/{id:int}/disposition",
            async (int id, DispositionRequest req, MesDbContext db, GenealogyService genealogy, TimeProvider clock) =>
        {
            var ncr = await db.Ncrs
                .Include(n => n.Serial).ThenInclude(s => s.PartRevision).ThenInclude(r => r.Part)
                .Include(n => n.WorkOrder).ThenInclude(w => w!.Operations)
                .Include(n => n.WorkOrder).ThenInclude(w => w!.Serial)
                .Include(n => n.WorkOrderOperation)
                .SingleOrDefaultAsync(n => n.Id == id)
                ?? throw new NotFoundException($"NCR {id} not found");

            var now = clock.GetUtcNow();
            ncr.ApplyDisposition(req.Disposition, req.By, req.Notes, now);
            if (req.Disposition == Disposition.Scrap)
                await genealogy.RemoveAsync(ncr.Serial.SerialNumber, req.By, $"Scrapped per {ncr.Number}", now);

            db.Outbox.Add(OutboxMessage.Create("ncr.dispositioned", ncr.Serial.SerialNumber, new
            {
                ncr = ncr.Number,
                serial = ncr.Serial.SerialNumber,
                disposition = req.Disposition,
                by = req.By,
                at = now,
            }, now));
            await db.SaveChangesAsync();
            return ToDto(ncr);
        });
    }

    internal static NcrDto ToDto(Ncr n) => new(n.Id, n.Number, n.Serial.SerialNumber, n.Serial.PartRevision.Part.PartNumber,
        n.WorkOrderId, n.WorkOrderOperationId, n.Description, n.RaisedBy, n.RaisedAt, n.Status, n.Disposition,
        n.DispositionedBy, n.DispositionedAt, n.DispositionNotes);
}
