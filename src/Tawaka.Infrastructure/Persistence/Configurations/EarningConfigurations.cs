using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tawaka.Domain.Earnings;

namespace Tawaka.Infrastructure.Persistence.Configurations;

public sealed class EarningTypeConfiguration : IEntityTypeConfiguration<EarningType>
{
    public void Configure(EntityTypeBuilder<EarningType> builder)
    {
        builder.ToTable("EarningTypes");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(40).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(150).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.TreatmentSource).HasMaxLength(300);
        builder.Property(t => t.TreatmentNotes).HasMaxLength(2000);
        builder.Property(t => t.DefaultMultiplier)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Rate));
        builder.Property(t => t.GlAccountCode).HasMaxLength(40);
        builder.Property(t => t.CreatedBy).HasMaxLength(100);
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(t => new { t.CompanyId, t.Code }).IsUnique();
        builder.HasIndex(t => t.TreatmentVerificationStatus);
    }
}

public sealed class DeductionTypeConfiguration : IEntityTypeConfiguration<DeductionType>
{
    public void Configure(EntityTypeBuilder<DeductionType> builder)
    {
        builder.ToTable("DeductionTypes");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(40).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(150).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.TreatmentSource).HasMaxLength(300);
        builder.Property(t => t.TreatmentNotes).HasMaxLength(2000);
        builder.Property(t => t.LimitAmount)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        builder.Property(t => t.LimitCurrency).HasMaxLength(3);
        builder.Property(t => t.GlAccountCode).HasMaxLength(40);
        builder.Property(t => t.CreatedBy).HasMaxLength(100);
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(t => new { t.CompanyId, t.Code }).IsUnique();
    }
}

public sealed class EmployeeRecurringEarningConfiguration
    : IEntityTypeConfiguration<EmployeeRecurringEarning>
{
    public void Configure(EntityTypeBuilder<EmployeeRecurringEarning> builder)
    {
        builder.ToTable("EmployeeRecurringEarnings");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Amount)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money)).IsRequired();
        builder.Property(e => e.Value)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Rate));
        builder.Property(e => e.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(1000);
        builder.Property(e => e.CreatedBy).HasMaxLength(100);
        builder.Property(e => e.ModifiedBy).HasMaxLength(100);
        builder.HasOne(e => e.EarningType).WithMany()
            .HasForeignKey(e => e.EarningTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.EmployeeId, e.EarningTypeId, e.EffectiveFrom });
    }
}

public sealed class EmployeeRecurringDeductionConfiguration
    : IEntityTypeConfiguration<EmployeeRecurringDeduction>
{
    public void Configure(EntityTypeBuilder<EmployeeRecurringDeduction> builder)
    {
        builder.ToTable("EmployeeRecurringDeductions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Amount)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money)).IsRequired();
        builder.Property(d => d.Value)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Rate));
        builder.Property(d => d.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(d => d.Notes).HasMaxLength(1000);
        builder.Property(d => d.CreatedBy).HasMaxLength(100);
        builder.Property(d => d.ModifiedBy).HasMaxLength(100);
        builder.HasOne(d => d.DeductionType).WithMany()
            .HasForeignKey(d => d.DeductionTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.EmployeeId, d.DeductionTypeId, d.EffectiveFrom });
    }
}
