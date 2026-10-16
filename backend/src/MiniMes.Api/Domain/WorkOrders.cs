namespace MiniMes.Api.Domain;

public enum WorkOrderStatus { Released, InProgress, OnHold, Complete, Cancelled }

public enum OperationStatus { Pending, Complete }

/// <summary>
/// A work order builds or refurbishes one serial. Releasing it copies the routing into its own
/// operations (the "traveler"), so later routing edits never rewrite in-flight or historical work.
/// </summary>
public class WorkOrder
{
    public int Id { get; private set; }
    public string Number { get; private set; } = null!;
    public WorkOrderType Type { get; private set; }
    public int SerialId { get; private set; }
    public SerializedPart Serial { get; private set; } = null!;
    public WorkOrderStatus Status { get; private set; }
    public string? HoldReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Optimistic concurrency token, mapped to Postgres' xmin system column.</summary>
    public uint Version { get; private set; }

    public List<WorkOrderOperation> Operations { get; private set; } = [];
    public List<Ncr> Ncrs { get; private set; } = [];

    private WorkOrder() { }

    public static WorkOrder Release(string number, SerializedPart serial, Routing routing, DateTimeOffset now)
    {
        if (routing.PartRevisionId != serial.PartRevisionId)
            throw new DomainException($"Routing '{routing.Name}' is for a different part revision than {serial.SerialNumber}");
        if (serial.Status == SerialStatus.Scrapped)
            throw new DomainException($"{serial.SerialNumber} is scrapped");
        if (routing.Operations.Count == 0)
            throw new DomainException($"Routing '{routing.Name}' has no operations");

        var wo = new WorkOrder
        {
            Number = number,
            Type = routing.Type,
            Serial = serial,
            SerialId = serial.Id,
            Status = WorkOrderStatus.Released,
            CreatedAt = now,
            UpdatedAt = now,
        };
        foreach (var op in routing.Operations.OrderBy(o => o.Sequence))
        {
            wo.Operations.Add(new WorkOrderOperation(op.Sequence, op.Name, op.StationId, isRework: false, now));
        }
        serial.Status = routing.Type == WorkOrderType.Build ? SerialStatus.InBuild : SerialStatus.InRefurb;
        return wo;
    }

    /// <summary>Traveler order. Rework steps run before the operation they were raised against.</summary>
    public IEnumerable<WorkOrderOperation> OrderedOperations() =>
        Operations.OrderBy(o => o.Sequence).ThenByDescending(o => o.IsRework).ThenBy(o => o.AddedAt);

    public WorkOrderOperation? CurrentOperation =>
        OrderedOperations().FirstOrDefault(o => o.Status == OperationStatus.Pending);

    public SignOff SignOff(WorkOrderOperation op, string signedBy, string? notes, DateTimeOffset now)
    {
        if (Status is WorkOrderStatus.OnHold or WorkOrderStatus.Complete or WorkOrderStatus.Cancelled)
            throw new DomainException($"Work order {Number} is {Status}");
        if (!Operations.Contains(op))
            throw new DomainException($"Operation {op.Sequence} is not on work order {Number}");
        if (op.Status == OperationStatus.Complete)
            throw new DomainException($"Operation {op.Sequence} '{op.Name}' is already signed off");
        if (string.IsNullOrWhiteSpace(signedBy))
            throw new DomainException("Sign-off requires a name");

        var current = CurrentOperation!;
        if (current != op)
            throw new DomainException($"Operation {current.Sequence} '{current.Name}' must be signed off first");
        if (Ncrs.Any(n => n.WorkOrderOperation == op && n.Status == NcrStatus.Open))
            throw new DomainException($"Operation {op.Sequence} has an open NCR; disposition it before signing off");

        var signOff = op.Complete(signedBy, notes, now);
        Touch(now);

        if (Operations.All(o => o.Status == OperationStatus.Complete))
        {
            Status = WorkOrderStatus.Complete;
            CompletedAt = now;
            Serial.Status = SerialStatus.Serviceable;
        }
        else
        {
            Status = WorkOrderStatus.InProgress;
        }
        return signOff;
    }

    public void Hold(string reason, DateTimeOffset now)
    {
        if (Status is WorkOrderStatus.Complete or WorkOrderStatus.Cancelled or WorkOrderStatus.OnHold)
            throw new DomainException($"Cannot hold a work order that is {Status}");
        Status = WorkOrderStatus.OnHold;
        HoldReason = reason;
        Touch(now);
    }

    public void Resume(DateTimeOffset now)
    {
        if (Status != WorkOrderStatus.OnHold) throw new DomainException($"Work order {Number} is not on hold");
        Status = Operations.Any(o => o.Status == OperationStatus.Complete) ? WorkOrderStatus.InProgress : WorkOrderStatus.Released;
        HoldReason = null;
        Touch(now);
    }

    internal WorkOrderOperation AddRework(WorkOrderOperation before, string name, DateTimeOffset now)
    {
        var rework = new WorkOrderOperation(before.Sequence, name, before.StationId, isRework: true, now);
        Operations.Add(rework);
        Touch(now);
        return rework;
    }

    internal void Cancel(DateTimeOffset now)
    {
        Status = WorkOrderStatus.Cancelled;
        Touch(now);
    }

    // Every state change writes the work order row, which bumps xmin. That is what makes two
    // inspectors signing off the same traveler concurrently collide instead of silently interleaving.
    internal void Touch(DateTimeOffset now) => UpdatedAt = now;
}

public class WorkOrderOperation
{
    public int Id { get; private set; }
    public int WorkOrderId { get; private set; }
    public int Sequence { get; private set; }
    public string Name { get; private set; }
    public int StationId { get; private set; }
    public Station Station { get; private set; } = null!;
    public bool IsRework { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }
    public OperationStatus Status { get; private set; }
    public SignOff? SignOff { get; private set; }

    internal WorkOrderOperation(int sequence, string name, int stationId, bool isRework, DateTimeOffset addedAt)
    {
        Sequence = sequence;
        Name = name;
        StationId = stationId;
        IsRework = isRework;
        AddedAt = addedAt;
        Status = OperationStatus.Pending;
    }

    internal SignOff Complete(string signedBy, string? notes, DateTimeOffset now)
    {
        Status = OperationStatus.Complete;
        SignOff = new SignOff { SignedBy = signedBy.Trim(), Notes = notes, SignedAt = now };
        return SignOff;
    }
}

/// <summary>Who signed off an operation and when. Immutable once written.</summary>
public class SignOff : IAppendOnly
{
    public int Id { get; init; }
    public int WorkOrderOperationId { get; init; }
    public required string SignedBy { get; init; }
    public DateTimeOffset SignedAt { get; init; }
    public string? Notes { get; init; }
}
