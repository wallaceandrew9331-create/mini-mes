using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Data;
using MiniMes.Api.Domain;
using MiniMes.Api.Queries;
using MiniMes.Api.Services;

namespace MiniMes.Api.Endpoints;

public record VehicleSummary(string SerialNumber, string PartNumber, SerialStatus Status, int Flights,
    DateTimeOffset? LastFlownAt, IReadOnlyList<string> FlightReadinessProblems);

public record FlightDto(int FlightNumber, string Mission, DateTimeOffset FlownAt, int SerialsFlown);
public record RecordFlightRequest(string Mission, DateTimeOffset? FlownAt);

public record RecallResponse(string LotNumber, IReadOnlyList<RecallPart> Parts, IReadOnlyList<RecallVehicle> AffectedVehicles);
public record RecallPart(string SerialNumber, string PartNumber, string Revision, SerialStatus Status, IReadOnlyList<string> CurrentPath,
    IReadOnlyList<RecallFlight> Flights);
public record RecallFlight(string Vehicle, int FlightNumber, string Mission, DateTimeOffset FlownAt);
public record RecallVehicle(string SerialNumber, int FlightsWithLotParts, IReadOnlyList<string> SuspectSerialsInstalledNow);

public record LotSummary(string LotNumber, string PartNumber, int Serials);

public static class FlightEndpoints
{
    public static void MapFlights(this IEndpointRouteBuilder api)
    {
        var vehicles = api.MapGroup("/vehicles").WithTags("Vehicles & flights");

        vehicles.MapGet("/", async (MesDbContext db, GenealogyQueries queries) =>
        {
            var list = await db.Serials
                .Where(s => s.PartRevision.Part.Kind == PartKind.Vehicle)
                .OrderBy(s => s.SerialNumber)
                .Select(s => new
                {
                    s.SerialNumber, s.PartRevision.Part.PartNumber, s.Status,
                    Flights = db.Flights.Count(f => f.VehicleId == s.Id),
                    LastFlownAt = db.Flights.Where(f => f.VehicleId == s.Id).Max(f => (DateTimeOffset?)f.FlownAt),
                })
                .ToListAsync();

            var result = new List<VehicleSummary>();
            foreach (var v in list) // a handful of vehicles; one tree query each is fine
                result.Add(new(v.SerialNumber, v.PartNumber, v.Status, v.Flights, v.LastFlownAt,
                    FlightReadiness.Check(await queries.TreeAsync(v.SerialNumber))));
            return result;
        });

        vehicles.MapGet("/{serialNumber}/flights", async (string serialNumber, MesDbContext db) =>
            await db.Flights.Where(f => f.Vehicle.SerialNumber == serialNumber)
                .OrderBy(f => f.FlightNumber)
                .Select(f => new FlightDto(f.FlightNumber, f.Mission, f.FlownAt, f.Manifest.Count))
                .ToListAsync());

        vehicles.MapPost("/{serialNumber}/flights", async (string serialNumber, RecordFlightRequest req, FlightService flights, TimeProvider clock) =>
        {
            var flight = await flights.RecordAsync(serialNumber, req.Mission, req.FlownAt ?? clock.GetUtcNow());
            return Results.Created($"/api/vehicles/{serialNumber}/flights",
                new FlightDto(flight.FlightNumber, flight.Mission, flight.FlownAt, flight.Manifest.Count));
        });

        api.MapGet("/lots", async (MesDbContext db) =>
            await db.Serials.Where(s => s.LotNumber != null)
                .GroupBy(s => new { s.LotNumber, s.PartRevision.Part.PartNumber })
                .OrderBy(g => g.Key.LotNumber)
                .Select(g => new LotSummary(g.Key.LotNumber!, g.Key.PartNumber, g.Count()))
                .ToListAsync())
            .WithTags("Recall");

        api.MapGet("/recall", RecallAsync).WithTags("Recall");

        api.MapGet("/events", async (MesDbContext db, int? limit) =>
            await db.Outbox.OrderByDescending(m => m.Id).Take(Math.Clamp(limit ?? 50, 1, 500))
                .Select(m => new { m.Id, m.Type, m.Key, m.OccurredAt, m.PublishedAt, m.Payload })
                .ToListAsync())
            .WithTags("Events");
    }

    /// <summary>
    /// Which flown vehicles contain (or contained) a part from this lot? Flight manifests answer "what flew",
    /// including parts swapped out since; the live genealogy answers "what is installed now".
    /// </summary>
    private static async Task<RecallResponse> RecallAsync(string lot, MesDbContext db, GenealogyQueries queries)
    {
        var serials = await db.Serials.Include(s => s.PartRevision).ThenInclude(r => r.Part)
            .Where(s => s.LotNumber == lot).OrderBy(s => s.SerialNumber).ToListAsync();
        if (serials.Count == 0) throw new NotFoundException($"No serials in lot {lot}");

        var paths = await queries.CurrentPathsForLotAsync(lot);
        var flown = await db.FlightManifest.Where(m => m.Serial.LotNumber == lot)
            .Join(db.Flights, m => m.FlightId, f => f.Id, (m, f) => new
            {
                m.SerialId, Vehicle = f.Vehicle.SerialNumber, f.FlightNumber, f.Mission, f.FlownAt,
            })
            .ToListAsync();

        var parts = serials.Select(s => new RecallPart(
            s.SerialNumber, s.PartRevision.Part.PartNumber, s.PartRevision.Revision, s.Status,
            paths.GetValueOrDefault(s.Id, [s.SerialNumber]),
            flown.Where(f => f.SerialId == s.Id).OrderBy(f => f.FlownAt)
                .Select(f => new RecallFlight(f.Vehicle, f.FlightNumber, f.Mission, f.FlownAt)).ToList()
        )).ToList();

        // A vehicle is affected if it ever flew with a lot part. Report which suspect parts are still aboard.
        var vehicles = flown.GroupBy(f => f.Vehicle).Select(g => new RecallVehicle(
            g.Key,
            g.Select(f => f.FlightNumber).Distinct().Count(),
            parts.Where(p => p.CurrentPath.Count > 1 && p.CurrentPath[0] == g.Key).Select(p => p.SerialNumber).ToList()
        )).OrderBy(v => v.SerialNumber).ToList();

        return new RecallResponse(lot, parts, vehicles);
    }
}
