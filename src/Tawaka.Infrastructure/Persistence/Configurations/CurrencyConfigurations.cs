using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tawaka.Domain.Currencies;

namespace Tawaka.Infrastructure.Persistence.Configurations;

public sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(3).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.DisplayCode).HasMaxLength(10).IsRequired();
        builder.Property(c => c.Symbol).HasMaxLength(10);
        builder.Property(c => c.CreatedBy).HasMaxLength(100);
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);
    }
}

public sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("ExchangeRates");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.FromCurrency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.ToCurrency).HasMaxLength(3).IsRequired();

        // Rates are stored as exact scaled integers, never as SQLite TEXT decimals.
        builder.Property(r => r.Rate)
            .HasConversion(new ScaledDecimalConverter(MoneyScales.Rate))
            .IsRequired();

        builder.Property(r => r.Source).HasMaxLength(200).IsRequired();
        builder.Property(r => r.SourceReference).HasMaxLength(500);
        builder.Property(r => r.CreatedBy).HasMaxLength(100);
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);

        builder.HasIndex(r => new { r.FromCurrency, r.ToCurrency, r.RateType, r.RateDate })
            .IsUnique();
    }
}
