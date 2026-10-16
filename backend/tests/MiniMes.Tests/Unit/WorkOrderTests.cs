using MiniMes.Api.Domain;
using static MiniMes.Tests.Unit.TestData;

namespace MiniMes.Tests.Unit;

public class WorkOrderTests
{
    [Fact]
    public void Release_copies_routing_into_traveler_in_sequence_order()
    {
        var serial = Serial();
        var wo = WorkOrder.Release("WO-1", serial, Routing(), T0);

        Assert.Equal([10, 20, 30], wo.OrderedOperations().Select(o => o.Sequence));
        Assert.All(wo.Operations, o => Assert.Equal(OperationStatus.Pending, o.Status));
        Assert.Equal(WorkOrderStatus.Released, wo.Status);
        Assert.Equal(SerialStatus.InBuild, serial.Status);
    }

    [Fact]
    public void Refurb_release_puts_serial_in_refurb()
    {
        var serial = Serial();
        WorkOrder.Release("WO-1", serial, Routing(type: WorkOrderType.Refurb), T0);
        Assert.Equal(SerialStatus.InRefurb, serial.Status);
    }

    [Fact]
    public void Release_rejects_routing_for_another_revision()
    {
        var ex = Assert.Throws<DomainException>(() => WorkOrder.Release("WO-1", Serial(revisionId: 1), Routing(revisionId: 2), T0));
        Assert.Contains("different part revision", ex.Message);
    }

    [Fact]
    public void Operations_must_be_signed_off_in_order()
    {
        var wo = Released();
        var second = wo.OrderedOperations().ElementAt(1);

        var ex = Assert.Throws<DomainException>(() => wo.SignOff(second, "J. Doe", null, T0));
        Assert.Contains("Operation 10", ex.Message);
    }

    [Fact]
    public void Operation_cannot_be_signed_off_twice()
    {
        var wo = Released();
        var first = wo.CurrentOperation!;
        wo.SignOff(first, "J. Doe", null, T0);

        Assert.Throws<DomainException>(() => wo.SignOff(first, "J. Doe", null, T0));
    }

    [Fact]
    public void Sign_off_records_who_and_when_and_starts_the_work_order()
    {
        var wo = Released();
        var signOff = wo.SignOff(wo.CurrentOperation!, "  J. Doe ", "torque verified", T0.AddHours(2));

        Assert.Equal("J. Doe", signOff.SignedBy);
        Assert.Equal(T0.AddHours(2), signOff.SignedAt);
        Assert.Equal(WorkOrderStatus.InProgress, wo.Status);
        Assert.Equal(20, wo.CurrentOperation!.Sequence);
    }

    [Fact]
    public void Signing_off_last_operation_completes_work_order_and_serial()
    {
        var serial = Serial();
        var wo = Released(serial);
        while (wo.CurrentOperation is { } op) wo.SignOff(op, "J. Doe", null, T0);

        Assert.Equal(WorkOrderStatus.Complete, wo.Status);
        Assert.Equal(T0, wo.CompletedAt);
        Assert.Equal(SerialStatus.Serviceable, serial.Status);
    }

    [Fact]
    public void Open_ncr_blocks_sign_off_of_its_operation()
    {
        var wo = Released();
        var op = wo.CurrentOperation!;
        Ncr.Raise("NCR-1", wo.Serial, wo, op, "Scratch on sealing surface", "QA", T0);

        var ex = Assert.Throws<DomainException>(() => wo.SignOff(op, "J. Doe", null, T0));
        Assert.Contains("open NCR", ex.Message);
    }

    [Fact]
    public void Held_work_order_cannot_be_signed_off_until_resumed()
    {
        var wo = Released();
        wo.Hold("Awaiting parts", T0);

        Assert.Throws<DomainException>(() => wo.SignOff(wo.CurrentOperation!, "J. Doe", null, T0));

        wo.Resume(T0.AddDays(1));
        Assert.Equal(WorkOrderStatus.Released, wo.Status);
        wo.SignOff(wo.CurrentOperation!, "J. Doe", null, T0.AddDays(1));
    }

    [Fact]
    public void State_changes_bump_updated_at_so_the_row_version_changes()
    {
        var wo = Released();
        wo.SignOff(wo.CurrentOperation!, "J. Doe", null, T0.AddMinutes(5));
        Assert.Equal(T0.AddMinutes(5), wo.UpdatedAt);

        Ncr.Raise("NCR-1", wo.Serial, wo, wo.CurrentOperation, "Dent", "QA", T0.AddMinutes(9));
        Assert.Equal(T0.AddMinutes(9), wo.UpdatedAt);
    }
}
