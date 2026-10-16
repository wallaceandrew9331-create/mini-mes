namespace MiniMes.Api.Domain;

/// <summary>One flight of a top-level vehicle. The manifest freezes the as-flown configuration.</summary>
public class Flight : IAppendOnly
{
    public int Id { get; init; }
    public int VehicleId { get; init; }
    public SerializedPart Vehicle { get; init; } = null!;

    /// <summary>1-based flight count of this vehicle.</summary>
    public int FlightNumber { get; init; }

    public required string Mission { get; init; }
    public DateTimeOffset FlownAt { get; init; }
    public List<FlightManifestEntry> Manifest { get; init; } = [];
}

/// <summary>
/// A serial that flew on a flight, with the parent it was installed in at the time. Genealogy can change
/// after the flight (engine swaps), so per-part flight history and recalls read this, not the live tree.
/// </summary>
public class FlightManifestEntry : IAppendOnly
{
    public int FlightId { get; init; }
    public int SerialId { get; init; }
    public SerializedPart Serial { get; init; } = null!;
    public int? ParentSerialId { get; init; }
}

/// <summary>A serial in a genealogy tree, with its usage. Read model produced by a recursive query.</summary>
public sealed record GenealogyNode
{
    public int Id { get; init; }
    public int? ParentId { get; init; }
    public int Depth { get; init; }
    public string SerialNumber { get; init; } = "";
    public string PartNumber { get; init; } = "";
    public string PartName { get; init; } = "";
    public string PartKind { get; init; } = "";
    public string Revision { get; init; } = "";
    public string? LotNumber { get; init; }
    public string Status { get; init; } = "";
    public int? RefurbIntervalFlights { get; init; }
    public int TotalFlights { get; init; }
    public int FlightsSinceRefurb { get; init; }
    public int OpenNcrs { get; init; }

    public bool RefurbDue => RefurbIntervalFlights is int limit && FlightsSinceRefurb >= limit;
}

public static class FlightReadiness
{
    /// <summary>Returns the reasons the vehicle cannot fly; empty means it is ready.</summary>
    public static IReadOnlyList<string> Check(IReadOnlyList<GenealogyNode> tree)
    {
        var problems = new List<string>();
        var root = tree.SingleOrDefault(n => n.Depth == 0);
        if (root is null) return ["Vehicle not found"];
        if (root.PartKind != nameof(Domain.PartKind.Vehicle))
            problems.Add($"{root.SerialNumber} is a {root.PartKind}, not a vehicle");

        foreach (var n in tree)
        {
            if (n.Status != nameof(SerialStatus.Serviceable))
                problems.Add($"{n.SerialNumber} is {n.Status}");
            if (n.OpenNcrs > 0)
                problems.Add($"{n.SerialNumber} has {n.OpenNcrs} open NCR(s)");
            if (n.RefurbDue)
                problems.Add($"{n.SerialNumber} is due for refurbishment ({n.FlightsSinceRefurb}/{n.RefurbIntervalFlights} flights since last refurb)");
        }
        return problems;
    }
}
