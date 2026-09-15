using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tawaka.Domain.Calendars;
using Tawaka.Domain.Leave;
using Tawaka.Domain.Loans;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Time;

namespace Tawaka.Infrastructure.Persistence.Configurations;

// ---- Time and attendance -------------------------------------------------------------------

public sealed class TimesheetConfiguration : IEntityTypeConfiguration<Timesheet>
{
    public void Configure(EntityTypeBuilder<Timesheet> builder)
    {
        builder.ToTable("Timesheets");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.SubmittedBy).HasMaxLength(100);
        builder.Property(t => t.ApprovedBy).HasMaxLength(100);
        builder.Property(t => t.DecisionReason).HasMaxLength(1000);
        builder.Property(t => t.CorrectionReason).HasMaxLength(1000);
        builder.Property(t => t.Notes).HasMaxLength(2000);
        builder.Property(t => t.CreatedBy).HasMaxLength(100);
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);

        builder.HasMany(t => t.Entries).WithOne(e => e.Timesheet!)
            .HasForeignKey(e => e.TimesheetId).OnDelete(DeleteBehavior.Cascade);

        // One live timesheet per employee and period. Corrections are excluded from the constraint
        // because a correction deliberately sits alongside the original it supersedes.
        builder.HasIndex(t => new { t.EmployeeId, t.PayrollPeriodId, t.CorrectsTimesheetId });
        builder.HasIndex(t => new { t.CompanyId, t.ApprovalStatus });
    }
}

public sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.ToTable("TimeEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ProjectName).HasMaxLength(300);
        builder.Property(e => e.ProjectSiteName).HasMaxLength(300);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        // Hours and days are quantities, not money, but they are stored as scaled integers for
        // the same reason: SQLite keeps decimal as text, which sorts and sums wrongly.
        builder.Property(e => e.OrdinaryHours)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
        builder.Property(e => e.DaysWorked)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));

        builder.HasMany(e => e.OvertimeLines).WithOne()
            .HasForeignKey(l => l.TimeEntryId).OnDelete(DeleteBehavior.Cascade);

        // One entry per date per timesheet: a second row for the same day double-counts it.
        builder.HasIndex(e => new { e.TimesheetId, e.WorkDate }).IsUnique();
    }
}

public sealed class TimeEntryOvertimeLineConfiguration
    : IEntityTypeConfiguration<TimeEntryOvertimeLine>
{
    public void Configure(EntityTypeBuilder<TimeEntryOvertimeLine> builder)
    {
        builder.ToTable("TimeEntryOvertimeLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.OvertimeCategoryCode).HasMaxLength(40).IsRequired();
        builder.Property(l => l.Notes).HasMaxLength(500);
        builder.Property(l => l.Hours)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));

        builder.HasIndex(l => new { l.TimeEntryId, l.OvertimeCategoryCode }).IsUnique();
    }
}

// ---- Leave ---------------------------------------------------------------------------------

public sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> builder)
    {
        builder.ToTable("LeaveTypes");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(40).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.EntitlementSource).HasMaxLength(500);
        builder.Property(t => t.ComplianceQuestion).HasMaxLength(20);
        builder.Property(t => t.CreatedBy).HasMaxLength(100);
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);

        foreach (var property in new[]
                 {
                     nameof(LeaveType.EntitlementDays),
                     nameof(LeaveType.StatutoryEntitlementDays),
                     nameof(LeaveType.MaximumCarryForwardDays)
                 })
        {
            builder.Property<decimal?>(property)
                .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        }

        builder.HasIndex(t => new { t.CompanyId, t.Code }).IsUnique();
    }
}

public sealed class LeaveEntitlementConfiguration : IEntityTypeConfiguration<LeaveEntitlement>
{
    public void Configure(EntityTypeBuilder<LeaveEntitlement> builder)
    {
        builder.ToTable("LeaveEntitlements");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.CreatedBy).HasMaxLength(100);
        builder.Property(e => e.ModifiedBy).HasMaxLength(100);

        // Nullable on purpose: an entitlement that has not been established is not zero days.
        builder.Property(e => e.EntitlementDays)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));

        builder.HasMany(e => e.Transactions).WithOne()
            .HasForeignKey(t => t.LeaveEntitlementId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.LeaveType).WithMany()
            .HasForeignKey(e => e.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.EmployeeId, e.LeaveTypeId, e.LeaveYearStart }).IsUnique();
    }
}

public sealed class LeaveTransactionConfiguration : IEntityTypeConfiguration<LeaveTransaction>
{
    public void Configure(EntityTypeBuilder<LeaveTransaction> builder)
    {
        builder.ToTable("LeaveTransactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Reason).HasMaxLength(1000);
        builder.Property(t => t.ReversalReason).HasMaxLength(1000);
        builder.Property(t => t.CreatedBy).HasMaxLength(100);
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);
        builder.Property(t => t.Days)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));

        builder.HasIndex(t => t.LeaveEntitlementId);
        builder.HasIndex(t => t.LeaveRequestId);
    }
}

public sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("LeaveRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Reason).HasMaxLength(1000);
        builder.Property(r => r.DecisionReason).HasMaxLength(1000);
        builder.Property(r => r.CorrectionReason).HasMaxLength(1000);
        builder.Property(r => r.SubmittedBy).HasMaxLength(100);
        builder.Property(r => r.ApprovedBy).HasMaxLength(100);
        builder.Property(r => r.CreatedBy).HasMaxLength(100);
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);
        builder.Property(r => r.Days)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));

        builder.HasOne(r => r.LeaveType).WithMany()
            .HasForeignKey(r => r.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.EmployeeId, r.StartDate, r.EndDate });
        builder.HasIndex(r => new { r.CompanyId, r.ApprovalStatus });
    }
}

// ---- Holiday calendar ----------------------------------------------------------------------

public sealed class HolidayCalendarConfiguration : IEntityTypeConfiguration<HolidayCalendar>
{
    public void Configure(EntityTypeBuilder<HolidayCalendar> builder)
    {
        builder.ToTable("HolidayCalendars");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(40).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.CreatedBy).HasMaxLength(100);
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);

        builder.HasMany(c => c.Holidays).WithOne(h => h.Calendar!)
            .HasForeignKey(h => h.HolidayCalendarId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique();
    }
}

public sealed class PublicHolidayConfiguration : IEntityTypeConfiguration<PublicHoliday>
{
    public void Configure(EntityTypeBuilder<PublicHoliday> builder)
    {
        builder.ToTable("PublicHolidays");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Name).HasMaxLength(200).IsRequired();
        builder.Property(h => h.Source).HasMaxLength(500);
        builder.Property(h => h.Notes).HasMaxLength(1000);
        builder.Property(h => h.CreatedBy).HasMaxLength(100);
        builder.Property(h => h.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(h => new { h.HolidayCalendarId, h.Date });
    }
}

// ---- Loans and advances --------------------------------------------------------------------

public sealed class EmployeeLoanConfiguration : IEntityTypeConfiguration<EmployeeLoan>
{
    public void Configure(EntityTypeBuilder<EmployeeLoan> builder)
    {
        builder.ToTable("EmployeeLoans");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.LoanNumber).HasMaxLength(40).IsRequired();
        builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.DisbursementReference).HasMaxLength(200);
        builder.Property(l => l.Purpose).HasMaxLength(500);
        builder.Property(l => l.Notes).HasMaxLength(2000);
        builder.Property(l => l.DecisionReason).HasMaxLength(1000);
        builder.Property(l => l.OverRecoveryApprovalReason).HasMaxLength(1000);
        builder.Property(l => l.OverRecoveryApprovedBy).HasMaxLength(100);
        builder.Property(l => l.SubmittedBy).HasMaxLength(100);
        builder.Property(l => l.ApprovedBy).HasMaxLength(100);
        builder.Property(l => l.CreatedBy).HasMaxLength(100);
        builder.Property(l => l.ModifiedBy).HasMaxLength(100);

        foreach (var property in new[]
                 {
                     nameof(EmployeeLoan.PrincipalAmount),
                     nameof(EmployeeLoan.InterestAmount),
                     nameof(EmployeeLoan.InstalmentAmount)
                 })
        {
            builder.Property<decimal>(property)
                .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
        }

        builder.Property(l => l.InterestRatePercent)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Rate));

        builder.HasMany(l => l.Instalments).WithOne(i => i.Loan!)
            .HasForeignKey(i => i.EmployeeLoanId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(l => l.Transactions).WithOne(t => t.Loan!)
            .HasForeignKey(t => t.EmployeeLoanId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.CompanyId, l.LoanNumber }).IsUnique();
        builder.HasIndex(l => new { l.EmployeeId, l.Status });
    }
}

public sealed class LoanInstalmentConfiguration : IEntityTypeConfiguration<LoanInstalment>
{
    public void Configure(EntityTypeBuilder<LoanInstalment> builder)
    {
        builder.ToTable("LoanInstalments");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.SkipReason).HasMaxLength(500);

        foreach (var property in new[]
                 {
                     nameof(LoanInstalment.Amount),
                     nameof(LoanInstalment.PrincipalPortion),
                     nameof(LoanInstalment.InterestPortion)
                 })
        {
            builder.Property<decimal>(property)
                .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
        }

        builder.HasIndex(i => new { i.EmployeeLoanId, i.InstalmentNumber }).IsUnique();
        builder.HasIndex(i => new { i.PayrollPeriodId, i.Status });
    }
}

public sealed class LoanTransactionConfiguration : IEntityTypeConfiguration<LoanTransaction>
{
    public void Configure(EntityTypeBuilder<LoanTransaction> builder)
    {
        builder.ToTable("LoanTransactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(t => t.Reference).HasMaxLength(200);
        builder.Property(t => t.Notes).HasMaxLength(2000);
        builder.Property(t => t.ReversalReason).HasMaxLength(1000);
        builder.Property(t => t.ReversedBy).HasMaxLength(100);
        builder.Property(t => t.CreatedBy).HasMaxLength(100);
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);
        builder.Property(t => t.Amount)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));

        builder.HasIndex(t => t.EmployeeLoanId);
        builder.HasIndex(t => t.PayrollRunId);
    }
}

// ---- The stored input snapshot -------------------------------------------------------------

public sealed class PayrollInputSnapshotRecordConfiguration
    : IEntityTypeConfiguration<PayrollInputSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<PayrollInputSnapshotRecord> builder)
    {
        builder.ToTable("PayrollInputSnapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SnapshotJson).IsRequired();
        builder.Property(s => s.SnapshotHash).HasMaxLength(64).IsRequired();
        builder.Property(s => s.EngineVersion).HasMaxLength(40).IsRequired();
        builder.Property(s => s.CreatedBy).HasMaxLength(100);
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);

        // One stored snapshot per calculated employee. A second would leave it ambiguous which one
        // the result came from, which defeats the point of storing it at all.
        builder.HasIndex(s => s.PayrollRunEmployeeId).IsUnique();
        builder.HasIndex(s => s.PayrollRunId);
    }
}

public sealed class PayrollRunInputSourceConfiguration
    : IEntityTypeConfiguration<PayrollRunInputSource>
{
    public void Configure(EntityTypeBuilder<PayrollRunInputSource> builder)
    {
        builder.ToTable("PayrollRunInputSources");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.InputType).HasMaxLength(40).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500).IsRequired();
        builder.Property(s => s.ApprovedBy).HasMaxLength(100);

        builder.HasIndex(s => new { s.PayrollRunId, s.InputType });

        // The question this table exists to answer: which runs consumed this input?
        builder.HasIndex(s => new { s.InputType, s.InputId });
    }
}
