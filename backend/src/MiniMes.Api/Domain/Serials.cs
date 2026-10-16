namespace MiniMes.Api.Domain;

public enum SerialStatus { InBuild, Serviceable, InRefurb, Scrapped }

public class SerializedPart
{
    public int Id { get; set; }
    public required string SerialNumber { get; set; }
    public int PartRevisionId { get; set; }
    public PartRevision PartRevision { get; set; } = null!;

    /// <summary>Supplier or production lot. Recalls are issued against lots.</summary>
    public string? LotNumber { get; set; }

    public SerialStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public void Scrap()
    {
        if (Status == SerialStatus.Scrapped) throw new DomainException($"{SerialNumber} is already scrapped");
        Status = SerialStatus.Scrapped;
    }
}

/// <summary>
/// As-built genealogy edge: child serial installed in parent serial. Rows are never deleted; a removal
/// sets <see cref="RemovedAt"/>, so the table doubles as the full install/remove history.
/// A partial unique index guarantees a serial is installed in at most one parent at a time.
/// </summary>
public class Installation
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public SerializedPart Parent { get; set; } = null!;
    public int ChildId { get; set; }
    public SerializedPart Child { get; set; } = null!;
    public DateTimeOffset InstalledAt { get; set; }
    public required string InstalledBy { get; set; }
    public DateTimeOffset? RemovedAt { get; private set; }
    public string? RemovedBy { get; private set; }
    public string? RemovalReason { get; private set; }

    public void Remove(string by, string reason, DateTimeOffset now)
    {
        if (RemovedAt is not null) throw new DomainException("Installation already removed");
        RemovedAt = now;
        RemovedBy = by;
        RemovalReason = reason;
    }
}
