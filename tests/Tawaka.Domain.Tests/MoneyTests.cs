using Tawaka.Domain.Common;
using Xunit;

namespace Tawaka.Domain.Tests;

/// <summary>
/// Behaviour tests: these hold regardless of any statutory rate, so they stay valid when the
/// verified 2026 tables replace the seed data.
/// </summary>
public class MoneyTests
{
    [Fact]
    public void Adding_same_currency_sums_amounts()
    {
        var result = Money.Usd(850m) + Money.Usd(225m);
        Assert.Equal(1075m, result.Amount);
        Assert.Equal(CurrencyCode.Usd, result.Currency);
    }

    [Fact]
    public void Adding_usd_to_zig_throws_rather_than_producing_a_number()
    {
        var ex = Assert.Throws<CurrencyMismatchException>(() => Money.Usd(850m) + Money.Zwg(12500m));
        Assert.Equal(CurrencyCode.Usd, ex.Left);
        Assert.Equal(CurrencyCode.Zwg, ex.Right);
    }

    [Fact]
    public void Subtracting_across_currencies_throws()
    {
        Assert.Throws<CurrencyMismatchException>(() => Money.Usd(100m) - Money.Zwg(100m));
    }

    [Fact]
    public void Comparing_across_currencies_throws()
    {
        Assert.Throws<CurrencyMismatchException>(() => Money.Usd(100m) > Money.Zwg(100m));
    }

    [Fact]
    public void Summing_across_currencies_throws()
    {
        Assert.Throws<CurrencyMismatchException>(() =>
            Money.Sum(CurrencyCode.Usd, new[] { Money.Usd(10m), Money.Zwg(10m) }));
    }

    [Fact]
    public void Min_applies_a_ceiling_within_one_currency()
    {
        // The shape of an NSSA ceiling: insurable earnings capped at the configured maximum.
        var insurable = Money.Min(Money.Usd(950m), Money.Usd(700m));
        Assert.Equal(700m, insurable.Amount);
    }

    [Theory]
    [InlineData(225.875, 225.88)]
    [InlineData(6.77625, 6.78)]
    [InlineData(-1.005, -1.01)]
    public void Rounds_away_from_zero_at_two_places(decimal raw, decimal expected)
    {
        Assert.Equal(expected, Money.Usd(raw).Round().Amount);
    }

    [Fact]
    public void Multiplication_keeps_full_precision_until_rounded()
    {
        var raw = Money.Usd(743.50m) * 0.25m;
        Assert.Equal(185.8750m, raw.Amount);
        Assert.Equal(185.88m, raw.Round().Amount);
    }

    [Fact]
    public void Money_requires_a_currency()
    {
        Assert.Throws<ArgumentException>(() => new Money(100m, default));
    }

    [Fact]
    public void Equality_distinguishes_currency()
    {
        Assert.NotEqual(Money.Usd(100m), Money.Zwg(100m));
        Assert.Equal(Money.Usd(100m), Money.Usd(100m));
    }

    [Fact]
    public void ToString_always_names_the_currency()
    {
        Assert.Equal("USD 1,075.00", Money.Usd(1075m).ToString());
        Assert.Equal("ZWG 12,500.00", Money.Zwg(12500m).ToString());
    }
}

public class CurrencyCodeTests
{
    [Theory]
    [InlineData("usd", "USD")]
    [InlineData(" zwg ", "ZWG")]
    public void Normalises_case_and_whitespace(string input, string expected)
    {
        Assert.Equal(expected, new CurrencyCode(input).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("US")]
    [InlineData("USDX")]
    [InlineData("U1D")]
    public void Rejects_invalid_codes(string input)
    {
        Assert.Throws<ArgumentException>(() => new CurrencyCode(input));
    }

    [Fact]
    public void Default_value_is_flagged_as_empty()
    {
        Assert.True(default(CurrencyCode).IsEmpty);
    }
}

public class DateRangeTests
{
    [Fact]
    public void Contains_is_inclusive_of_both_bounds()
    {
        var range = new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Assert.True(range.Contains(new DateOnly(2026, 1, 1)));
        Assert.True(range.Contains(new DateOnly(2026, 12, 31)));
        Assert.False(range.Contains(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Open_ended_range_has_no_upper_bound()
    {
        var range = new DateRange(new DateOnly(2026, 1, 1), null);
        Assert.True(range.Contains(new DateOnly(2099, 1, 1)));
    }

    [Fact]
    public void Overlap_detection_catches_conflicting_rule_versions()
    {
        var existing = new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var overlapping = new DateRange(new DateOnly(2026, 6, 1), null);
        var following = new DateRange(new DateOnly(2027, 1, 1), null);

        Assert.True(existing.Overlaps(overlapping));
        Assert.False(existing.Overlaps(following));
    }

    [Fact]
    public void Effective_to_before_effective_from_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new DateRange(new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1)));
    }
}
