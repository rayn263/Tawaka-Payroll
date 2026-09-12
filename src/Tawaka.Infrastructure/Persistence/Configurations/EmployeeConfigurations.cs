using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tawaka.Domain.Employees;

namespace Tawaka.Infrastructure.Persistence.Configurations;

public sealed class EmploymentTypeConfiguration : IEntityTypeConfiguration<EmploymentType>
{
    public void Configure(EntityTypeBuilder<EmploymentType> builder)
    {
        builder.ToTable("EmploymentTypes");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Code).HasMaxLength(40).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.CreatedBy).HasMaxLength(100);
        builder.Property(t => t.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(t => new { t.CompanyId, t.Code }).IsUnique();
    }
}

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EmployeeNumber).HasMaxLength(40).IsRequired();
        builder.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.MiddleName).HasMaxLength(100);
        builder.Property(e => e.LastName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.NationalId).HasMaxLength(40);
        builder.Property(e => e.PassportNumber).HasMaxLength(40);
        builder.Property(e => e.MaritalStatus).HasMaxLength(40);
        builder.Property(e => e.Nationality).HasMaxLength(60);
        builder.Property(e => e.Phone).HasMaxLength(60);
        builder.Property(e => e.AlternatePhone).HasMaxLength(60);
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.AddressLine1).HasMaxLength(200);
        builder.Property(e => e.AddressLine2).HasMaxLength(200);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.PhotoPath).HasMaxLength(500);
        builder.Property(e => e.TerminationReason).HasMaxLength(1000);
        builder.Property(e => e.Notes).HasMaxLength(4000);
        builder.Property(e => e.CreatedBy).HasMaxLength(100);
        builder.Property(e => e.ModifiedBy).HasMaxLength(100);

        // Employee numbers are unique within a company; national IDs where captured.
        builder.HasIndex(e => new { e.CompanyId, e.EmployeeNumber }).IsUnique();
        builder.HasIndex(e => new { e.CompanyId, e.NationalId })
            .IsUnique()
            .HasFilter("[NationalId] IS NOT NULL");
        builder.HasIndex(e => e.Status);

        builder.HasMany(e => e.Contracts).WithOne(c => c.Employee!)
            .HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.PaymentAccounts).WithOne(a => a.Employee!)
            .HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.StatutoryProfile).WithOne(p => p.Employee!)
            .HasForeignKey<EmployeeStatutoryProfile>(p => p.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class EmployeeContractConfiguration : IEntityTypeConfiguration<EmployeeContract>
{
    public void Configure(EntityTypeBuilder<EmployeeContract> builder)
    {
        builder.ToTable("EmployeeContracts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ContractReference).HasMaxLength(60);
        builder.Property(c => c.PayrollCurrency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.CommissionStructure).HasMaxLength(4000);
        builder.Property(c => c.ChangeReason).HasMaxLength(1000);
        builder.Property(c => c.Notes).HasMaxLength(4000);
        builder.Property(c => c.CreatedBy).HasMaxLength(100);
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);

        foreach (var property in new[]
                 {
                     nameof(EmployeeContract.BasicSalary), nameof(EmployeeContract.HourlyRate),
                     nameof(EmployeeContract.DailyRate), nameof(EmployeeContract.WeeklyRate),
                     nameof(EmployeeContract.MonthlyRate), nameof(EmployeeContract.ProjectRate)
                 })
        {
            builder.Property<decimal?>(property)
                .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        }

        builder.Property(c => c.StandardHoursPerDay)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
        builder.Property(c => c.StandardDaysPerWeek)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));

        builder.HasOne(c => c.EmploymentType).WithMany()
            .HasForeignKey(c => c.EmploymentTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.EmployeeId, c.VersionNumber }).IsUnique();

        // At most one current contract per employee, enforced by the database rather than by
        // remembering to check in application code.
        builder.HasIndex(c => c.EmployeeId)
            .IsUnique()
            .HasFilter("[IsCurrent] = 1")
            .HasDatabaseName("IX_EmployeeContracts_OneCurrentPerEmployee");
    }
}

public sealed class EmployeeStatutoryProfileConfiguration
    : IEntityTypeConfiguration<EmployeeStatutoryProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeStatutoryProfile> builder)
    {
        builder.ToTable("EmployeeStatutoryProfiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TaxNumber).HasMaxLength(60);
        builder.Property(p => p.PayeExemptionReason).HasMaxLength(500);
        builder.Property(p => p.NssaNumber).HasMaxLength(60);
        builder.Property(p => p.NssaEligibilityOverrideReason).HasMaxLength(500);
        builder.Property(p => p.NecMembershipNumber).HasMaxLength(60);
        builder.Property(p => p.PensionSchemeNumber).HasMaxLength(60);
        builder.Property(p => p.CreatedBy).HasMaxLength(100);
        builder.Property(p => p.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(p => p.EmployeeId).IsUnique();
    }
}

public sealed class EmployeePaymentAccountConfiguration
    : IEntityTypeConfiguration<EmployeePaymentAccount>
{
    public void Configure(EntityTypeBuilder<EmployeePaymentAccount> builder)
    {
        builder.ToTable("EmployeePaymentAccounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(a => a.AccountName).HasMaxLength(200);
        builder.Property(a => a.BankName).HasMaxLength(200);
        builder.Property(a => a.BranchName).HasMaxLength(200);
        builder.Property(a => a.BranchCode).HasMaxLength(40);
        builder.Property(a => a.AccountNumber).HasMaxLength(60);
        builder.Property(a => a.MobileMoneyProvider).HasMaxLength(100);
        builder.Property(a => a.MobileMoneyNumber).HasMaxLength(60);
        builder.Property(a => a.AllocationValue)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        builder.Property(a => a.Notes).HasMaxLength(1000);
        builder.Property(a => a.CreatedBy).HasMaxLength(100);
        builder.Property(a => a.ModifiedBy).HasMaxLength(100);

        // One primary account per employee per currency.
        builder.HasIndex(a => new { a.EmployeeId, a.CurrencyCode })
            .IsUnique()
            .HasFilter("[IsPrimary] = 1")
            .HasDatabaseName("IX_EmployeePaymentAccounts_OnePrimaryPerCurrency");
    }
}

public sealed class EmployeeProjectAssignmentConfiguration
    : IEntityTypeConfiguration<EmployeeProjectAssignment>
{
    public void Configure(EntityTypeBuilder<EmployeeProjectAssignment> builder)
    {
        builder.ToTable("EmployeeProjectAssignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.AllocationPercent)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money));
        builder.Property(a => a.RateOverrideAmount)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        builder.Property(a => a.RateOverrideCurrency).HasMaxLength(3);
        builder.Property(a => a.CreatedBy).HasMaxLength(100);
        builder.Property(a => a.ModifiedBy).HasMaxLength(100);

        builder.HasOne(a => a.Employee).WithMany()
            .HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.Project).WithMany()
            .HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.EmployeeId, a.ProjectId, a.StartDate });
    }
}

public sealed class EmployeeSupportingConfiguration :
    IEntityTypeConfiguration<EmployeeStatusHistory>,
    IEntityTypeConfiguration<EmployeeNextOfKin>,
    IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeStatusHistory> builder)
    {
        builder.ToTable("EmployeeStatusHistory");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Reason).HasMaxLength(1000);
        builder.Property(h => h.ChangedBy).HasMaxLength(100).IsRequired();
        builder.HasIndex(h => new { h.EmployeeId, h.EffectiveDate });
    }

    public void Configure(EntityTypeBuilder<EmployeeNextOfKin> builder)
    {
        builder.ToTable("EmployeeNextOfKin");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.FullName).HasMaxLength(200).IsRequired();
        builder.Property(k => k.Relationship).HasMaxLength(60);
        builder.Property(k => k.Phone).HasMaxLength(60);
        builder.Property(k => k.Address).HasMaxLength(300);
        builder.Property(k => k.CreatedBy).HasMaxLength(100);
        builder.Property(k => k.ModifiedBy).HasMaxLength(100);
    }

    public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
    {
        builder.ToTable("EmployeeDocuments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.DocumentType).HasMaxLength(60).IsRequired();
        builder.Property(d => d.Title).HasMaxLength(200).IsRequired();
        builder.Property(d => d.FilePath).HasMaxLength(500).IsRequired();
        builder.Property(d => d.FileHash).HasMaxLength(128);
        builder.Property(d => d.Notes).HasMaxLength(1000);
        builder.Property(d => d.CreatedBy).HasMaxLength(100);
        builder.Property(d => d.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(d => d.EmployeeId);
    }
}
