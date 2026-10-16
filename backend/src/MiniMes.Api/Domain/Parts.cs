namespace MiniMes.Api.Domain;

public enum PartKind { Component, Assembly, Vehicle }

public class Part
{
    public int Id { get; set; }
    public required string PartNumber { get; set; }
    public required string Name { get; set; }
    public PartKind Kind { get; set; }

    /// <summary>Flights allowed between refurbishments; null means the part is not life-limited.</summary>
    public int? RefurbIntervalFlights { get; set; }

    public List<PartRevision> Revisions { get; set; } = [];
}

public class PartRevision
{
    public int Id { get; set; }
    public int PartId { get; set; }
    public Part Part { get; set; } = null!;
    public required string Revision { get; set; }
    public DateTimeOffset ReleasedAt { get; set; }
}

public class Station
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
}

public enum WorkOrderType { Build, Refurb }

/// <summary>
/// The template a traveler is generated from. One routing per part revision and work order type,
/// so a design change (new revision) can change the build steps without touching in-flight work.
/// </summary>
public class Routing
{
    public int Id { get; set; }
    public int PartRevisionId { get; set; }
    public PartRevision PartRevision { get; set; } = null!;
    public WorkOrderType Type { get; set; }
    public required string Name { get; set; }
    public List<RoutingOperation> Operations { get; set; } = [];
}

public class RoutingOperation
{
    public int Id { get; set; }
    public int RoutingId { get; set; }
    public int Sequence { get; set; }
    public required string Name { get; set; }
    public int StationId { get; set; }
    public Station Station { get; set; } = null!;
}
