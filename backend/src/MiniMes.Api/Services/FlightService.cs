using Microsoft.EntityFrameworkCore;
using MiniMes.Api.Data;
using MiniMes.Api.Domain;
using MiniMes.Api.Events;
using MiniMes.Api.Queries;

namespace MiniMes.Api.Services;

public class FlightService(MesDbContext db, GenealogyQueries queries)
{
    /// <summary>
    /// Records a flight after checking readiness, and snapshots the vehicle's current tree into the manifest.
    /// Concurrent records for the same vehicle collide on the (vehicle_id, flight_number) unique index.
    /// </summary>
    public async Task<Flight> RecordAsync(string vehicleSn, string mission, DateTimeOffset flownAt)
    {
        if (string.IsNullOrWhiteSpace(mission)) throw new DomainException("Flight requires a mission name");

        var tree = await queries.TreeAsync(vehicleSn);
        if (tree.Count == 0) throw new NotFoundException($"Serial {vehicleSn} not found");

        var problems = FlightReadiness.Check(tree);
        if (problems.Count > 0) throw new DomainException($"{vehicleSn} is not flight-ready", problems);

        var vehicleId = tree.Single(n => n.Depth == 0).Id;
        var previous = await db.Flights.Where(f => f.VehicleId == vehicleId)
            .OrderByDescending(f => f.FlightNumber).FirstOrDefaultAsync();
        if (previous is not null && flownAt <= previous.FlownAt)
            throw new DomainException($"Flight must be after flight {previous.FlightNumber} ({previous.FlownAt:yyyy-MM-dd})");

        var flight = new Flight
        {
            VehicleId = vehicleId,
            FlightNumber = (previous?.FlightNumber ?? 0) + 1,
            Mission = mission.Trim(),
            FlownAt = flownAt,
            Manifest = tree.Select(n => new FlightManifestEntry { SerialId = n.Id, ParentSerialId = n.ParentId }).ToList(),
        };
        db.Flights.Add(flight);
        db.Outbox.Add(OutboxMessage.Create("flight.recorded", vehicleSn,
            new { vehicle = vehicleSn, flight.FlightNumber, flight.Mission, flownAt, serials = tree.Select(n => n.SerialNumber) },
            flownAt));
        await db.SaveChangesAsync();
        return flight;
    }
}
