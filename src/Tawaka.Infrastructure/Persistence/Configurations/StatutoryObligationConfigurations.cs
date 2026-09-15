using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory.Obligations;

namespace Tawaka.Infrastructure.Persistence.Configurations;

public sealed class StatutoryObligationConfiguration : IEntityTypeConfiguration<StatutoryObligation>
{
    public void Configure(EntityTypeBuilder<StatutoryObligation> builder)
    {
        builder.ToTable("StatutoryObligations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(o => o.ApprovedBy).HasMaxLength(100);
        builder.Property(o => o.Notes).HasMaxLength(2000);
        builder.Property(o => o.CreatedBy).HasMaxLength(100);
        builder.Property(o => o.ModifiedBy).HasMaxLength(100);

        foreach (var property in new[]
                 {
                     nameof(StatutoryObligation.CalculatedAmount),
                     nameof(StatutoryObligation.DeductedAmount),
                     nameof(StatutoryObligation.ApprovedAmount)
                 })
        {
            builder.Property<decimal>(property)
                .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
        }

        builder.HasMany(o => o.Payments).WithOne(p => p.Obligation!)
            .HasForeignKey(p => p.StatutoryObligationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(o => o.Lines).WithOne()
            .HasForeignKey(l => l.StatutoryObligationId).OnDelete(DeleteBehavior.Cascade);

        // One obligation per run, type and currency: a second would double-count the liability.
        builder.HasIndex(o => new { o.PayrollRunId, o.ObligationType, o.CurrencyCode }).IsUnique();
        builder.HasIndex(o => new { o.CompanyId, o.DueDate });
    }
}

public sealed class StatutoryObligationLineConfiguration
    : IEntityTypeConfiguration<StatutoryObligationLine>
{
    public void Configure(EntityTypeBuilder<StatutoryObligationLine> builder)
    {
        builder.ToTable("StatutoryObligationLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.EmployeeNumber).HasMaxLength(40).IsRequired();
        builder.Property(l => l.EmployeeName).HasMaxLength(300).IsRequired();
        builder.Property(l => l.TaxNumber).HasMaxLength(60);
        builder.Property(l => l.NssaNumber).HasMaxLength(60);
        builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(l => l.Amount)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
        builder.HasIndex(l => l.StatutoryObligationId);
    }
}

public sealed class StatutoryPaymentConfiguration : IEntityTypeConfiguration<StatutoryPayment>
{
    public void Configure(EntityTypeBuilder<StatutoryPayment> builder)
    {
        builder.ToTable("StatutoryPayments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(p => p.PaymentReference).HasMaxLength(120).IsRequired();
        builder.Property(p => p.AuthorityReceiptNumber).HasMaxLength(120);
        builder.Property(p => p.ReceiptFilePath).HasMaxLength(500);
        builder.Property(p => p.ReceiptFileHash).HasMaxLength(128);
        builder.Property(p => p.Notes).HasMaxLength(2000);
        builder.Property(p => p.ReversalReason).HasMaxLength(1000);
        builder.Property(p => p.ReversedBy).HasMaxLength(100);
        builder.Property(p => p.CreatedBy).HasMaxLength(100);
        builder.Property(p => p.ModifiedBy).HasMaxLength(100);
        builder.Property(p => p.Amount)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
        builder.Property(p => p.PenaltyOrInterestIncluded)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
        builder.HasIndex(p => new { p.StatutoryObligationId, p.PaymentDate });
    }
}

public sealed class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    public void Configure(EntityTypeBuilder<Payslip> builder)
    {
        builder.ToTable("Payslips");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PayslipNumber).HasMaxLength(60).IsRequired();
        builder.Property(p => p.GeneratedBy).HasMaxLength(100).IsRequired();
        builder.Property(p => p.IssuedBy).HasMaxLength(100);
        builder.Property(p => p.SupersedeReason).HasMaxLength(1000);
        builder.Property(p => p.FilePath).HasMaxLength(500);
        builder.Property(p => p.ContentHash).HasMaxLength(128);
        builder.Property(p => p.TemplateVersion).HasMaxLength(40);
        builder.Property(p => p.CreatedBy).HasMaxLength(100);
        builder.Property(p => p.ModifiedBy).HasMaxLength(100);

        builder.HasOne(p => p.PayrollRunEmployee).WithMany()
            .HasForeignKey(p => p.PayrollRunEmployeeId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.PayslipNumber, p.Revision }).IsUnique();
        builder.HasIndex(p => p.PayrollRunEmployeeId);
    }
}
