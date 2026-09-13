using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tawaka.Domain.Payroll;

namespace Tawaka.Infrastructure.Persistence.Configurations;

public sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> builder)
    {
        builder.ToTable("PayrollRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.CalculatedBy).HasMaxLength(100);
        builder.Property(r => r.ReviewedBy).HasMaxLength(100);
        builder.Property(r => r.ApprovedBy).HasMaxLength(100);
        builder.Property(r => r.FinalisedBy).HasMaxLength(100);
        builder.Property(r => r.PaidBy).HasMaxLength(100);
        builder.Property(r => r.LockedBy).HasMaxLength(100);
        builder.Property(r => r.ReopenedBy).HasMaxLength(100);
        builder.Property(r => r.ReopenReason).HasMaxLength(1000);
        builder.Property(r => r.EngineVersion).HasMaxLength(40);
        builder.Property(r => r.Notes).HasMaxLength(2000);
        builder.Property(r => r.CreatedBy).HasMaxLength(100);
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);

        builder.HasOne(r => r.PayrollPeriod).WithMany()
            .HasForeignKey(r => r.PayrollPeriodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Employees).WithOne(e => e.PayrollRun!)
            .HasForeignKey(e => e.PayrollRunId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.PayrollPeriodId, r.RunNumber }).IsUnique();
        builder.HasIndex(r => r.Status);
    }
}

public sealed class PayrollRunEmployeeConfiguration : IEntityTypeConfiguration<PayrollRunEmployee>
{
    public void Configure(EntityTypeBuilder<PayrollRunEmployee> builder)
    {
        builder.ToTable("PayrollRunEmployees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EmployeeNumber).HasMaxLength(40).IsRequired();
        builder.Property(e => e.EmployeeName).HasMaxLength(300).IsRequired();
        builder.Property(e => e.EmploymentTypeCode).HasMaxLength(40);
        builder.Property(e => e.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(e => e.ExclusionReason).HasMaxLength(500);
        builder.Property(e => e.CreatedBy).HasMaxLength(100);
        builder.Property(e => e.ModifiedBy).HasMaxLength(100);

        // Every money column persists as a scaled integer, so totals sum exactly.
        foreach (var property in new[]
                 {
                     nameof(PayrollRunEmployee.GrossEarningsAmount),
                     nameof(PayrollRunEmployee.TaxableIncomeAmount),
                     nameof(PayrollRunEmployee.NssaInsurableEarningsAmount),
                     nameof(PayrollRunEmployee.NssaEmployeeAmount),
                     nameof(PayrollRunEmployee.PayeBeforeCreditsAmount),
                     nameof(PayrollRunEmployee.TaxCreditsAmount),
                     nameof(PayrollRunEmployee.PayeAfterCreditsAmount),
                     nameof(PayrollRunEmployee.AidsLevyAmount),
                     nameof(PayrollRunEmployee.TotalStatutoryDeductionsAmount),
                     nameof(PayrollRunEmployee.TotalOtherDeductionsAmount),
                     nameof(PayrollRunEmployee.TotalDeductionsAmount),
                     nameof(PayrollRunEmployee.NetPayAmount),
                     nameof(PayrollRunEmployee.NssaEmployerAmount),
                     nameof(PayrollRunEmployee.ApwcsAmount),
                     nameof(PayrollRunEmployee.TotalEmployerCostAmount)
                 })
        {
            builder.Property<decimal?>(property)
                .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        }

        builder.HasMany(e => e.EarningLines).WithOne()
            .HasForeignKey(l => l.PayrollRunEmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.DeductionLines).WithOne()
            .HasForeignKey(l => l.PayrollRunEmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.EmployerCostLines).WithOne()
            .HasForeignKey(l => l.PayrollRunEmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.TraceEntries).WithOne()
            .HasForeignKey(t => t.PayrollRunEmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.UnresolvedItems).WithOne()
            .HasForeignKey(u => u.PayrollRunEmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.CostAllocations).WithOne()
            .HasForeignKey(a => a.PayrollRunEmployeeId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.PayrollRunId, e.EmployeeId }).IsUnique();
    }
}

public sealed class PayrollLineConfigurations :
    IEntityTypeConfiguration<PayrollEarningLine>,
    IEntityTypeConfiguration<PayrollDeductionLine>,
    IEntityTypeConfiguration<PayrollEmployerCostLine>,
    IEntityTypeConfiguration<PayrollCalculationTraceEntry>,
    IEntityTypeConfiguration<PayrollUnresolvedItem>,
    IEntityTypeConfiguration<PayrollCostAllocation>
{
    public void Configure(EntityTypeBuilder<PayrollEarningLine> builder)
    {
        builder.ToTable("PayrollEarningLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Code).HasMaxLength(40).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(150).IsRequired();
        builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.OriginalCurrencyCode).HasMaxLength(3);
        builder.Property(l => l.ExchangeRateSource).HasMaxLength(200);
        Money(builder, l => l.Amount);
        Money(builder, l => l.TaxableAmount);
        Money(builder, l => l.ExemptAmount);
        Money(builder, l => l.NssaApplicableAmount);
        builder.Property(l => l.Quantity)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        builder.Property(l => l.RateAmount)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        builder.Property(l => l.OriginalAmount)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        builder.Property(l => l.ExchangeRateUsed)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Rate));
    }

    public void Configure(EntityTypeBuilder<PayrollDeductionLine> builder)
    {
        builder.ToTable("PayrollDeductionLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Code).HasMaxLength(40).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(150).IsRequired();
        builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsRequired();
        Money(builder, l => l.Amount);
    }

    public void Configure(EntityTypeBuilder<PayrollEmployerCostLine> builder)
    {
        builder.ToTable("PayrollEmployerCostLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Code).HasMaxLength(40).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(150).IsRequired();
        builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.RuleId).HasMaxLength(80);
        Money(builder, l => l.Amount);
        Money(builder, l => l.BaseAmount);
        builder.Property(l => l.RateApplied)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Rate));
    }

    public void Configure(EntityTypeBuilder<PayrollCalculationTraceEntry> builder)
    {
        builder.ToTable("PayrollCalculationTraces");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Stage).HasMaxLength(60).IsRequired();
        builder.Property(t => t.ItemKey).HasMaxLength(120).IsRequired();
        builder.Property(t => t.RuleId).HasMaxLength(80);
        builder.Property(t => t.RuleType).HasMaxLength(60);
        builder.Property(t => t.VerificationStatus).HasMaxLength(40);
        builder.Property(t => t.RuleSource).HasMaxLength(400);
        builder.Property(t => t.Inputs).HasMaxLength(4000);
        builder.Property(t => t.Steps).HasMaxLength(4000);
        builder.Property(t => t.OutputCurrency).HasMaxLength(3);
        builder.Property(t => t.RoundingApplied).HasMaxLength(120);
        builder.Property(t => t.Explanation).HasMaxLength(2000);
        builder.Property(t => t.Conversion).HasMaxLength(1000);
        builder.Property(t => t.RawValue)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Rate));
        builder.Property(t => t.OutputAmount)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        builder.HasIndex(t => new { t.PayrollRunEmployeeId, t.Sequence });
    }

    public void Configure(EntityTypeBuilder<PayrollUnresolvedItem> builder)
    {
        builder.ToTable("PayrollUnresolvedItems");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Code).HasMaxLength(80).IsRequired();
        builder.Property(u => u.ItemKey).HasMaxLength(120).IsRequired();
        builder.Property(u => u.Message).HasMaxLength(2000).IsRequired();
        builder.Property(u => u.RuleType).HasMaxLength(60);
        builder.Property(u => u.RuleId).HasMaxLength(80);
        builder.Property(u => u.VerificationStatus).HasMaxLength(40);
        builder.Property(u => u.ComplianceQuestion).HasMaxLength(40);
        builder.Property(u => u.Remedy).HasMaxLength(2000);
    }

    public void Configure(EntityTypeBuilder<PayrollCostAllocation> builder)
    {
        builder.ToTable("PayrollCostAllocations");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.ProjectName).HasMaxLength(200);
        builder.Property(a => a.DepartmentName).HasMaxLength(150);
        builder.Property(a => a.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(a => a.Percent)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
        builder.Property(a => a.AllocatedCostAmount)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
        builder.HasIndex(a => a.ProjectId);
    }

    private static void Money<T>(
        EntityTypeBuilder<T> builder,
        System.Linq.Expressions.Expression<Func<T, decimal>> property) where T : class =>
        builder.Property(property).HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
}
