using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Tracing;
using Xunit;

namespace Tawaka.Payroll.Engine.Tests;

public class CalculationTraceTests
{
    [Fact]
    public void Trace_entry_records_rule_inputs_steps_rounding_and_output()
    {
        var rule = TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
            VerificationStatus.Supported);

        var entry = new TraceEntryBuilder("ComputePaye", "PAYE")
            .Sequence(9)
            .FromRule(rule)
            .Input("Taxable income", Money.Usd(1043.50m))
            .Step("200.00 x 20%", 40.0000m)
            .Step("743.50 x 25%", 185.8750m)
            .Raw(225.8750m)
            .Rounded(Money.Usd(225.88m), "2dp, away from zero")
            .Explain("Progressive table applied to taxable income.")
            .Build();

        Assert.Equal("PAYE", entry.ItemKey);
        Assert.Equal("PAYE-USD-2026-MONTHLY", entry.RuleId);
        Assert.Equal(VerificationStatus.Supported, entry.VerificationStatus);
        Assert.Equal(StatutoryRuleType.PayeTable, entry.RuleType);
        Assert.Equal(2, entry.Steps.Count);
        Assert.Equal(225.8750m, entry.RawValue);
        Assert.Equal(Money.Usd(225.88m), entry.Output);
        Assert.Equal("2dp, away from zero", entry.RoundingApplied);
        Assert.Equal("USD 1,043.50", entry.Inputs["Taxable income"]);
    }

    [Fact]
    public void Trace_flags_when_any_figure_came_from_an_unverified_rule()
    {
        var trace = new CalculationTrace();
        trace.Add(new TraceEntryBuilder("ComputeNssa", "NSSA employee")
            .FromRule(TestRules.Nssa(VerificationStatus.Verified))
            .Result(Money.Usd(31.50m))
            .Build());

        Assert.False(trace.ContainsUnverifiedRule);

        trace.Add(new TraceEntryBuilder("ComputePaye", "PAYE")
            .FromRule(TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Unverified))
            .Result(Money.Usd(225.88m))
            .Build());

        Assert.True(trace.ContainsUnverifiedRule);
    }

    [Fact]
    public void Trace_can_be_looked_up_by_item()
    {
        var trace = new CalculationTrace();
        trace.Add(new TraceEntryBuilder("ComputeNssa", "NSSA employee")
            .Result(Money.Usd(31.50m))
            .Build());

        Assert.NotNull(trace.ForItem("nssa employee"));
        Assert.Null(trace.ForItem("PAYE"));
    }

    [Fact]
    public void Conversion_provenance_is_carried_on_the_trace()
    {
        var rate = new Domain.Currencies.ExchangeRate
        {
            FromCurrency = "ZWG",
            ToCurrency = "USD",
            Rate = 0.0377m,
            Source = "RBZ interbank",
            RateDate = new DateOnly(2026, 9, 30),
            EffectiveFrom = new DateOnly(2026, 9, 30)
        };
        var conversion = rate.Convert(Money.Zwg(2000m),
            Domain.Currencies.ConversionPurpose.TaxBaseAggregation);

        var entry = new TraceEntryBuilder("DetermineTaxBase", "ZiG allowance converted")
            .Converted(Currencies.ConversionRecord.From(conversion))
            .Build();

        Assert.NotNull(entry.Conversion);
        Assert.Equal(2000m, entry.Conversion!.OriginalAmount);
        Assert.Equal("ZWG", entry.Conversion.OriginalCurrency);
        Assert.Equal("USD", entry.Conversion.ConvertedCurrency);
        Assert.Equal(0.0377m, entry.Conversion.Rate);
        Assert.Equal(new DateOnly(2026, 9, 30), entry.Conversion.RateDate);
        Assert.Equal("RBZ interbank", entry.Conversion.RateSource);
    }
}
