using MiniMes.Api.Domain;
using static MiniMes.Tests.Unit.TestData;

namespace MiniMes.Tests.Unit;

public class NcrTests
{
    [Fact]
    public void Rework_inserts_a_rework_step_ahead_of_the_blocked_operation()
    {
        var wo = Released();
        wo.SignOff(wo.CurrentOperation!, "J. Doe", null, T0);
        var blocked = wo.CurrentOperation!; // op 20
        var ncr = Ncr.Raise("NCR-1", wo.Serial, wo, blocked, "Bolt torque low", "QA", T0);

        ncr.ApplyDisposition(Disposition.Rework, "MRB", "Re-torque", T0.AddHours(1));

        var rework = wo.CurrentOperation!;
        Assert.True(rework.IsRework);
        Assert.Equal(20, rework.Sequence);
        Assert.Contains("NCR-1", rework.Name);
        Assert.Equal(NcrStatus.Closed, ncr.Status);

        wo.SignOff(rework, "J. Doe", null, T0.AddHours(2));
        Assert.Same(blocked, wo.CurrentOperation);
        wo.SignOff(blocked, "J. Doe", null, T0.AddHours(3)); // NCR closed, so no longer blocked
    }

    [Fact]
    public void Use_as_is_unblocks_the_operation_without_extra_steps()
    {
        var wo = Released();
        var op = wo.CurrentOperation!;
        var ncr = Ncr.Raise("NCR-1", wo.Serial, wo, op, "Cosmetic scratch", "QA", T0);

        ncr.ApplyDisposition(Disposition.UseAsIs, "MRB", "Cosmetic only", T0);

        Assert.Equal(3, wo.Operations.Count);
        wo.SignOff(op, "J. Doe", null, T0);
    }

    [Fact]
    public void Scrapping_the_work_order_serial_cancels_the_work_order()
    {
        var wo = Released();
        var ncr = Ncr.Raise("NCR-1", wo.Serial, wo, wo.CurrentOperation, "Cracked weld", "QA", T0);

        ncr.ApplyDisposition(Disposition.Scrap, "MRB", null, T0);

        Assert.Equal(SerialStatus.Scrapped, wo.Serial.Status);
        Assert.Equal(WorkOrderStatus.Cancelled, wo.Status);
    }

    [Fact]
    public void Scrapping_a_child_part_leaves_the_parent_work_order_open()
    {
        var wo = Released(Serial("EN-9001"));
        var valve = Serial("MOV-9001");
        var ncr = Ncr.Raise("NCR-1", valve, wo, wo.CurrentOperation, "Seat leak", "QA", T0);

        ncr.ApplyDisposition(Disposition.Scrap, "MRB", null, T0);

        Assert.Equal(SerialStatus.Scrapped, valve.Status);
        Assert.Equal(WorkOrderStatus.Released, wo.Status);
    }

    [Fact]
    public void Disposition_happens_exactly_once()
    {
        var ncr = Ncr.Raise("NCR-1", Serial(), null, null, "Dent", "QA", T0);
        ncr.ApplyDisposition(Disposition.UseAsIs, "MRB", null, T0);

        Assert.Throws<DomainException>(() => ncr.ApplyDisposition(Disposition.Scrap, "MRB", null, T0));
        Assert.Equal(Disposition.UseAsIs, ncr.Disposition);
    }

    [Fact]
    public void Rework_requires_an_operation_to_rework()
    {
        var ncr = Ncr.Raise("NCR-1", Serial(), null, null, "Dent", "QA", T0);
        Assert.Throws<DomainException>(() => ncr.ApplyDisposition(Disposition.Rework, "MRB", null, T0));
    }

    [Fact]
    public void Cannot_raise_against_a_signed_off_operation()
    {
        var wo = Released();
        var op = wo.CurrentOperation!;
        wo.SignOff(op, "J. Doe", null, T0);

        Assert.Throws<DomainException>(() => Ncr.Raise("NCR-1", wo.Serial, wo, op, "Late find", "QA", T0));
    }
}
