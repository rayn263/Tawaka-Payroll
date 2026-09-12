using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Infrastructure.Interceptors;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

public class PeriodLockTests
{
    private static PayrollPeriod September() => new()
    {
        Code = "2026-09",
        Name = "September 2026",
        Frequency = PeriodBasis.Monthly,
        StartDate = new DateOnly(2026, 9, 1),
        EndDate = new DateOnly(2026, 9, 30),
        PayDate = new DateOnly(2026, 9, 30),
        TaxYear = 2026,
        Status = PayrollPeriodStatus.Open
    };

    [Fact]
    public void An_open_period_can_be_edited()
    {
        using var db = new TestDatabase();
        var period = September();
        db.Context.PayrollPeriods.Add(period);
        db.Context.SaveChanges();

        period.Name = "September 2026 (revised)";
        db.Context.SaveChanges();

        Assert.Equal("September 2026 (revised)", db.Context.PayrollPeriods.AsNoTracking().Single().Name);
    }

    [Fact]
    public void Locking_a_period_is_a_permitted_transition()
    {
        using var db = new TestDatabase();
        var period = September();
        db.Context.PayrollPeriods.Add(period);
        db.Context.SaveChanges();

        period.Status = PayrollPeriodStatus.Locked;
        period.LockedAt = db.Clock.Now;
        period.LockedBy = db.User.UserId;
        db.Context.SaveChanges();

        Assert.True(db.Context.PayrollPeriods.AsNoTracking().Single().IsLocked);
    }

    /// <summary>
    /// TC-30: the write is rejected at the data layer, not merely hidden in the interface.
    /// </summary>
    [Fact]
    public void Modifying_a_locked_period_is_rejected_by_the_data_layer()
    {
        using var db = new TestDatabase();
        var period = September();
        period.Status = PayrollPeriodStatus.Locked;
        db.Context.PayrollPeriods.Add(period);
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();

        var locked = db.Context.PayrollPeriods.Single();
        locked.Name = "Tampered";

        var ex = Assert.Throws<PeriodLockedException>(() => db.Context.SaveChanges());
        Assert.Equal(nameof(PayrollPeriod), ex.EntityName);
        Assert.Contains("Payroll.Reopen", ex.Message);
    }

    [Fact]
    public void Deleting_a_locked_period_is_rejected()
    {
        using var db = new TestDatabase();
        var period = September();
        period.Status = PayrollPeriodStatus.Locked;
        db.Context.PayrollPeriods.Add(period);
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();

        db.Context.PayrollPeriods.Remove(db.Context.PayrollPeriods.Single());

        Assert.Throws<PeriodLockedException>(() => db.Context.SaveChanges());
    }

    [Fact]
    public void The_locked_record_is_unchanged_after_a_rejected_write()
    {
        using var db = new TestDatabase();
        var period = September();
        period.Status = PayrollPeriodStatus.Locked;
        db.Context.PayrollPeriods.Add(period);
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();

        var locked = db.Context.PayrollPeriods.Single();
        locked.Name = "Tampered";
        Assert.Throws<PeriodLockedException>(() => db.Context.SaveChanges());
        db.Context.ChangeTracker.Clear();

        Assert.Equal("September 2026", db.Context.PayrollPeriods.AsNoTracking().Single().Name);
    }

    [Fact]
    public void An_authorised_reopen_can_write_through_the_lock()
    {
        using var db = new TestDatabase();
        var period = September();
        period.Status = PayrollPeriodStatus.Locked;
        db.Context.PayrollPeriods.Add(period);
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();

        var locked = db.Context.PayrollPeriods.Single();
        using (db.LockOverride.Begin("Correcting an overtime entry — approved by R. Nyakuhwa"))
        {
            locked.Status = PayrollPeriodStatus.Open;
            db.Context.SaveChanges();
        }

        Assert.False(db.Context.PayrollPeriods.AsNoTracking().Single().IsLocked);
    }

    [Fact]
    public void Reopening_requires_a_reason()
    {
        using var db = new TestDatabase();

        Assert.Throws<ArgumentException>(() => db.LockOverride.Begin("  "));
    }

    [Fact]
    public void The_override_stops_applying_once_the_scope_ends()
    {
        using var db = new TestDatabase();
        var period = September();
        period.Status = PayrollPeriodStatus.Locked;
        db.Context.PayrollPeriods.Add(period);
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();

        using (db.LockOverride.Begin("Authorised correction"))
        {
            Assert.True(db.LockOverride.IsActive);
        }

        Assert.False(db.LockOverride.IsActive);

        var locked = db.Context.PayrollPeriods.Single();
        locked.Name = "Tampered again";
        Assert.Throws<PeriodLockedException>(() => db.Context.SaveChanges());
    }

    [Fact]
    public void A_rejected_write_is_still_recorded_in_the_audit_trail_ordering()
    {
        // The lock interceptor runs before the audit interceptor, so a rejected write neither
        // persists data nor leaves a misleading "successful change" entry.
        using var db = new TestDatabase();
        var period = September();
        period.Status = PayrollPeriodStatus.Locked;
        db.Context.PayrollPeriods.Add(period);
        db.Context.SaveChanges();
        var auditCountBefore = db.Context.AuditLogs.AsNoTracking().Count();
        db.Context.ChangeTracker.Clear();

        var locked = db.Context.PayrollPeriods.Single();
        locked.Name = "Tampered";
        Assert.Throws<PeriodLockedException>(() => db.Context.SaveChanges());
        db.Context.ChangeTracker.Clear();

        Assert.Equal(auditCountBefore, db.Context.AuditLogs.AsNoTracking().Count());
    }
}
