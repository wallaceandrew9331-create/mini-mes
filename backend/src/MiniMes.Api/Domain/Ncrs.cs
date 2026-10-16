namespace MiniMes.Api.Domain;

public enum NcrStatus { Open, Closed }

public enum Disposition { UseAsIs, Rework, Scrap }

/// <summary>
/// Nonconformance report: a serial does not meet spec. While open it blocks sign-off of the operation
/// it was raised against. Material review then dispositions it exactly once.
/// </summary>
public class Ncr
{
    public int Id { get; private set; }
    public string Number { get; private set; } = null!;
    public int SerialId { get; private set; }
    public SerializedPart Serial { get; private set; } = null!;
    public int? WorkOrderId { get; private set; }
    public WorkOrder? WorkOrder { get; private set; }
    public int? WorkOrderOperationId { get; private set; }
    public WorkOrderOperation? WorkOrderOperation { get; private set; }
    public string Description { get; private set; } = null!;
    public string RaisedBy { get; private set; } = null!;
    public DateTimeOffset RaisedAt { get; private set; }
    public NcrStatus Status { get; private set; }
    public Disposition? Disposition { get; private set; }
    public string? DispositionedBy { get; private set; }
    public DateTimeOffset? DispositionedAt { get; private set; }
    public string? DispositionNotes { get; private set; }

    private Ncr() { }

    /// <param name="serial">The nonconforming serial. During a refurb this is often a child of the work order's serial.</param>
    public static Ncr Raise(string number, SerializedPart serial, WorkOrder? workOrder, WorkOrderOperation? operation,
        string description, string raisedBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(description)) throw new DomainException("NCR requires a description");
        if (string.IsNullOrWhiteSpace(raisedBy)) throw new DomainException("NCR requires the name of who raised it");
        if (serial.Status == SerialStatus.Scrapped) throw new DomainException($"{serial.SerialNumber} is scrapped");
        if (operation is not null)
        {
            if (workOrder is null || !workOrder.Operations.Contains(operation))
                throw new DomainException("Operation does not belong to the given work order");
            if (operation.Status == OperationStatus.Complete)
                throw new DomainException($"Operation {operation.Sequence} is already signed off");
        }

        var ncr = new Ncr
        {
            Number = number,
            Serial = serial,
            SerialId = serial.Id,
            WorkOrder = workOrder,
            WorkOrderOperation = operation,
            Description = description.Trim(),
            RaisedBy = raisedBy.Trim(),
            RaisedAt = now,
            Status = NcrStatus.Open,
        };
        // Raising an NCR bumps the work order's version, so a sign-off racing with it fails instead of
        // slipping past the "open NCR blocks sign-off" rule.
        workOrder?.Ncrs.Add(ncr);
        workOrder?.Touch(now);
        return ncr;
    }

    /// <summary>
    /// Applies material review's decision. Rework inserts a rework step ahead of the blocked operation;
    /// scrap retires the serial (and cancels its own work order). Removing a scrapped part from its parent
    /// needs the genealogy, so the caller does that.
    /// </summary>
    public void ApplyDisposition(Disposition disposition, string by, string? notes, DateTimeOffset now)
    {
        if (Status != NcrStatus.Open) throw new DomainException($"{Number} was already dispositioned {Disposition}");
        if (string.IsNullOrWhiteSpace(by)) throw new DomainException("Disposition requires a name");

        switch (disposition)
        {
            case Domain.Disposition.Rework:
                if (WorkOrder is null || WorkOrderOperation is null)
                    throw new DomainException("Rework needs an NCR raised against a work order operation");
                WorkOrder.AddRework(WorkOrderOperation, $"Rework per {Number}: {notes ?? Description}", now);
                break;
            case Domain.Disposition.Scrap:
                Serial.Scrap();
                if (WorkOrder is not null && WorkOrder.Serial == Serial) WorkOrder.Cancel(now);
                break;
        }

        Status = NcrStatus.Closed;
        Disposition = disposition;
        DispositionedBy = by.Trim();
        DispositionedAt = now;
        DispositionNotes = notes;
    }
}
