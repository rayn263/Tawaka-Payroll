using Tawaka.Domain.Common;
using Tawaka.Domain.Currencies;
using Xunit;

namespace Tawaka.Domain.Tests;

public class CurrencyConversionTests
{
    private static ExchangeRate Rate(decimal rate = 26.50m) => new()
    {
        FromCurrency = "USD",
        ToCurrency = "ZWG",
        Rate = rate,
        RateType = RateType.Interbank,
        Source = "RBZ interbank",
        RateDate = new DateOnly(2026, 9, 30),
        EffectiveFrom = new DateOnly(2026, 9, 30)
    };

    [Fact]
    public void Conversion_retains_the_original_amount_and_currency()
    {
        var conversion = Rate().Convert(Money.Usd(800m), ConversionPurpose.TaxBaseAggregation);

        Assert.Equal(Money.Usd(800m), conversion.OriginalAmount);
        Assert.Equal(CurrencyCode.Usd, conversion.OriginalCurrency);
        Assert.Equal(21200m, conversion.ConvertedAmount.Amount);
        Assert.Equal(CurrencyCode.Zwg, conversion.ConvertedCurrency);
    }

    [Fact]
    public void Conversion_records_full_provenance()
    {
        var conversion = Rate().Convert(Money.Usd(100m), ConversionPurpose.TaxCredit);

        Assert.Equal(26.50m, conversion.Rate);
        Assert.Equal(new DateOnly(2026, 9, 30), conversion.RateDate);
        Assert.Equal("RBZ interbank", conversion.RateSource);
        Assert.Equal(RateType.Interbank, conversion.RateType);
        Assert.Equal(ConversionPurpose.TaxCredit, conversion.Purpose);
    }

    [Fact]
    public void Converting_the_wrong_currency_throws()
    {
        Assert.Throws<CurrencyMismatchException>(() =>
            Rate().Convert(Money.Zwg(100m), ConversionPurpose.Reporting));
    }

    [Fact]
    public void Changing_the_stored_rate_does_not_alter_a_completed_conversion()
    {
        var rate = Rate();
        var conversion = rate.Convert(Money.Usd(100m), ConversionPurpose.TaxBaseAggregation);

        rate.Rate = 28.00m;

        Assert.Equal(26.50m, conversion.Rate);
        Assert.Equal(2650m, conversion.ConvertedAmount.Amount);
    }

    [Fact]
    public void Rate_applies_only_within_its_effective_period()
    {
        var rate = Rate();
        rate.EffectiveFrom = new DateOnly(2026, 9, 1);
        rate.EffectiveTo = new DateOnly(2026, 9, 30);

        Assert.True(rate.AppliesOn(new DateOnly(2026, 9, 15)));
        Assert.False(rate.AppliesOn(new DateOnly(2026, 10, 1)));
    }
}
