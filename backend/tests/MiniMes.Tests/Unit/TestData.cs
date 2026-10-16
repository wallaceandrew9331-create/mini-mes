using MiniMes.Api.Domain;

namespace MiniMes.Tests.Unit;

/// <summary>In-memory domain objects for unit tests; no database involved.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    public static SerializedPart Serial(string serialNumber = "EN-9001", int revisionId = 1) => new()
    {
        SerialNumber = serialNumber,
        PartRevisionId = revisionId,
        Status = SerialStatus.Serviceable,
    };

    public static Routing Routing(int revisionId = 1, WorkOrderType type = WorkOrderType.Build, int operations = 3) => new()
    {
        PartRevisionId = revisionId,
        Type = type,
        Name = "Test routing",
        // Deliberately out of order: the traveler must sort by sequence.
        Operations = Enumerable.Range(1, operations).Reverse()
            .Select(i => new RoutingOperation { Sequence = i * 10, Name = $"Op {i * 10}", StationId = i })
            .ToList(),
    };

    public static WorkOrder Released(SerializedPart? serial = null, int operations = 3) =>
        WorkOrder.Release("WO-1", serial ?? Serial(), Routing(operations: operations), T0);
}
