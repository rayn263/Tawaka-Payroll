using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tawaka.Domain.Companies;

namespace Tawaka.Infrastructure.Persistence.Configurations;

public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("Companies");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.TradingName).HasMaxLength(200);
        builder.Property(c => c.RegistrationNumber).HasMaxLength(60);
        builder.Property(c => c.AddressLine1).HasMaxLength(200);
        builder.Property(c => c.AddressLine2).HasMaxLength(200);
        builder.Property(c => c.City).HasMaxLength(100);
        builder.Property(c => c.Country).HasMaxLength(100);
        builder.Property(c => c.Phone).HasMaxLength(60);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.Website).HasMaxLength(200);
        builder.Property(c => c.LogoPath).HasMaxLength(500);
        builder.Property(c => c.TaxNumber).HasMaxLength(60);
        builder.Property(c => c.PayeReference).HasMaxLength(60);
        builder.Property(c => c.NssaEmployerNumber).HasMaxLength(60);
        builder.Property(c => c.ZimdefNumber).HasMaxLength(60);
        builder.Property(c => c.StandardsDevelopmentFundNumber).HasMaxLength(60);
        builder.Property(c => c.NecCode).HasMaxLength(60);
        builder.Property(c => c.NecMembershipNumber).HasMaxLength(60);
        builder.Property(c => c.IndustryClassification).HasMaxLength(200);
        builder.Property(c => c.ApwcsIndustryCode).HasMaxLength(40);
        builder.Property(c => c.DefaultPayrollCurrency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.DefaultReportingCurrency).HasMaxLength(3).IsRequired();
        builder.Property(c => c.CreatedBy).HasMaxLength(100);
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);

        builder.HasMany(c => c.Currencies).WithOne(cc => cc.Company!)
            .HasForeignKey(cc => cc.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.BankAccounts).WithOne(b => b.Company!)
            .HasForeignKey(b => b.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CompanyCurrencyConfiguration : IEntityTypeConfiguration<CompanyCurrency>
{
    public void Configure(EntityTypeBuilder<CompanyCurrency> builder)
    {
        builder.ToTable("CompanyCurrencies");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(c => c.DisplayCodeOverride).HasMaxLength(10);
        builder.Property(c => c.CreatedBy).HasMaxLength(100);
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(c => new { c.CompanyId, c.CurrencyCode }).IsUnique();
    }
}

public sealed class CompanyBankAccountConfiguration : IEntityTypeConfiguration<CompanyBankAccount>
{
    public void Configure(EntityTypeBuilder<CompanyBankAccount> builder)
    {
        builder.ToTable("CompanyBankAccounts");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.AccountName).HasMaxLength(200).IsRequired();
        builder.Property(b => b.BankName).HasMaxLength(200).IsRequired();
        builder.Property(b => b.BranchName).HasMaxLength(200);
        builder.Property(b => b.BranchCode).HasMaxLength(40);
        builder.Property(b => b.AccountNumber).HasMaxLength(60).IsRequired();
        builder.Property(b => b.SwiftCode).HasMaxLength(20);
        builder.Property(b => b.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(b => b.Notes).HasMaxLength(1000);
        builder.Property(b => b.CreatedBy).HasMaxLength(100);
        builder.Property(b => b.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(b => new { b.CompanyId, b.AccountNumber, b.CurrencyCode }).IsUnique();
    }
}
