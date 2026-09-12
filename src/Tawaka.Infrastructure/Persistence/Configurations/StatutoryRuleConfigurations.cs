using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tawaka.Domain.Statutory;

namespace Tawaka.Infrastructure.Persistence.Configurations;

public sealed class StatutoryRuleConfiguration : IEntityTypeConfiguration<StatutoryRule>
{
    public void Configure(EntityTypeBuilder<StatutoryRule> builder)
    {
        builder.ToTable("StatutoryRules");
        builder.UseTptMappingStrategy();
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RuleId).HasMaxLength(80).IsRequired();
        builder.HasIndex(r => r.RuleId).IsUnique();
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Jurisdiction).HasMaxLength(10).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3);
        builder.Property(r => r.Notes).HasMaxLength(2000);
        builder.Property(r => r.CreatedBy).HasMaxLength(100);
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);

        // Source provenance is mandatory metadata, owned by the rule.
        builder.OwnsOne(r => r.Source, source =>
        {
            source.Property(s => s.Source).HasColumnName("Source").HasMaxLength(300);
            source.Property(s => s.SourceReference).HasColumnName("SourceReference").HasMaxLength(500);
            source.Property(s => s.SourceDate).HasColumnName("SourceDate");
            source.Property(s => s.VerifiedBy).HasColumnName("VerifiedBy").HasMaxLength(100);
            source.Property(s => s.VerifiedAt).HasColumnName("VerifiedAt");
        });

        builder.HasIndex(r => new { r.EffectiveFrom, r.EffectiveTo });
        builder.HasIndex(r => r.VerificationStatus);
    }
}

public sealed class TaxRuleConfiguration : IEntityTypeConfiguration<TaxRule>
{
    public void Configure(EntityTypeBuilder<TaxRule> builder)
    {
        builder.ToTable("TaxRules");
        builder.HasMany(r => r.Brackets)
            .WithOne(b => b.TaxRule!)
            .HasForeignKey(b => b.TaxRuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TaxBracketConfiguration : IEntityTypeConfiguration<TaxBracket>
{
    public void Configure(EntityTypeBuilder<TaxBracket> builder)
    {
        builder.ToTable("TaxBrackets");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.LowerBound)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money)).IsRequired();
        builder.Property(b => b.UpperBound)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        builder.Property(b => b.Rate)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Rate)).IsRequired();

        // Null until the official "less" column is obtained from the published table (spec Q26).
        builder.Property(b => b.FixedDeduction)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));

        builder.HasIndex(b => new { b.TaxRuleId, b.Sequence }).IsUnique();
    }
}

public sealed class AidsLevyRuleConfiguration : IEntityTypeConfiguration<AidsLevyRule>
{
    public void Configure(EntityTypeBuilder<AidsLevyRule> builder)
    {
        builder.ToTable("AidsLevyRules");
        builder.Property(r => r.Rate)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Rate)).IsRequired();
    }
}

public sealed class NssaRuleConfiguration : IEntityTypeConfiguration<NssaRule>
{
    public void Configure(EntityTypeBuilder<NssaRule> builder)
    {
        builder.ToTable("NssaRules");
        builder.Property(r => r.EmployeeRate)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Rate)).IsRequired();
        builder.Property(r => r.EmployerRate)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Rate)).IsRequired();
        builder.Property(r => r.CeilingAmount)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money)).IsRequired();
        builder.Property(r => r.GrossUpTriggerMultiplier)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Rate));
    }
}

public sealed class NssaEligibilityRuleConfiguration : IEntityTypeConfiguration<NssaEligibilityRule>
{
    public void Configure(EntityTypeBuilder<NssaEligibilityRule> builder)
    {
        builder.ToTable("NssaEligibilityRules");
        builder.Property(r => r.EmploymentTypeCode).HasMaxLength(40).IsRequired();
        builder.Property(r => r.Condition).HasMaxLength(500);
    }
}

public sealed class ApwcsRuleConfiguration : IEntityTypeConfiguration<ApwcsRule>
{
    public void Configure(EntityTypeBuilder<ApwcsRule> builder)
    {
        builder.ToTable("ApwcsRules");
        builder.Property(r => r.IndustryClassification).HasMaxLength(200);
        builder.Property(r => r.IndustryCode).HasMaxLength(40);
        builder.Property(r => r.Rate)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Rate)).IsRequired();
        builder.Property(r => r.CeilingAmount)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
    }
}

public sealed class EmployerLevyRuleConfiguration : IEntityTypeConfiguration<EmployerLevyRule>
{
    public void Configure(EntityTypeBuilder<EmployerLevyRule> builder)
    {
        builder.ToTable("EmployerLevyRules");
        builder.Property(r => r.EmployerPortionRate)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Rate)).IsRequired();
        builder.Property(r => r.EmployeePortionRate)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Rate)).IsRequired();
    }
}

public sealed class TaxCreditRuleConfiguration : IEntityTypeConfiguration<TaxCreditRule>
{
    public void Configure(EntityTypeBuilder<TaxCreditRule> builder)
    {
        builder.ToTable("TaxCreditRules");
        builder.Property(r => r.Amount)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money)).IsRequired();
        builder.Property(r => r.PercentageOfQualifyingAmount)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Rate));
        builder.Property(r => r.AnnualCap)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
    }
}

public sealed class TaxExemptionRuleConfiguration : IEntityTypeConfiguration<TaxExemptionRule>
{
    public void Configure(EntityTypeBuilder<TaxExemptionRule> builder)
    {
        builder.ToTable("TaxExemptionRules");
        builder.Property(r => r.LimitAmount)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Money)).IsRequired();
    }
}

public sealed class CurrencyTaxStrategyRuleConfiguration
    : IEntityTypeConfiguration<CurrencyTaxStrategyRule>
{
    public void Configure(EntityTypeBuilder<CurrencyTaxStrategyRule> builder)
    {
        builder.ToTable("CurrencyTaxStrategyRules");
        builder.Property(r => r.PrimaryCurrency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.AdvisorReference).HasMaxLength(200);
    }
}
