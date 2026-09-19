using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Results;
using Xunit;

namespace Tawaka.Payroll.Engine.Tests;

/// <summary>
/// Financial invariants — properties that must hold for <b>every</b> calculation, whatever the
/// inputs. These are the safety net: a future change that breaks one of these breaks payroll,
/// regardless of whether any individual worked example still passes.
/// </summary>
public class InvariantTests
{
    private static readonly PayrollCalculator Engine = new();

    /// <summary>A spread of salaries, including the band boundaries and rounding edges.</summary>
    public static TheoryData<decimal> Salaries => new()
    {
        0.01m, 1m, 99.99m, 100m, 100.01m, 250m, 299.99m, 300m, 300.005m, 300.01m,
        700m, 850m, 1000.005m, 2999.99m, 3000m, 3000.01m, 5000m, 12345.67m, 250000m
    };

    /// <summary>Gross less every employee deduction must equal net pay, exactly.</summary>
    [Theory]
    [MemberData(nameof(Salaries))]
    public void Gross_less_deductions_equals_net_pay(decimal salary)
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(salary).Build());

        Assert.Equal(PayrollResultStatus.Calculated, result.Status);

        var deductions = result.Deductions.Sum(d => d.Amount.Amount);
        Assert.Equal(result.GrossEarnings!.Value.Amount - deductions, result.NetPay!.Value.Amount);
        Assert.Equal(deductions, result.TotalDeductions!.Value.Amount);
    }

    /// <summary>The payslip totals must equal the sum of the lines that make them up.</summary>
    [Theory]
    [MemberData(nameof(Salaries))]
    public void Totals_reconcile_to_their_lines(decimal salary)
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd()
            .Basic(salary)
            .Allowance("HOUSING", "Housing Allowance", 100m)
            .Overtime(50m)
            .Build());

        var grossLines = result.Earnings.Where(e => e.IsIncludedInGross).Sum(e => e.Amount.Amount);
        Assert.Equal(grossLines, result.GrossEarnings!.Value.Amount);

        var statutory = result.Deductions.Where(d => d.IsStatutory).Sum(d => d.Amount.Amount);
        var other = result.Deductions.Where(d => !d.IsStatutory).Sum(d => d.Amount.Amount);
        Assert.Equal(statutory, result.TotalStatutoryDeductions!.Value.Amount);
        Assert.Equal(other, result.TotalOtherDeductions!.Value.Amount);
    }

    /// <summary>
    /// A recovery larger than the pay it is recovered from must not produce a negative net pay.
    /// An employee who finishes the month owing their employer money is not a payroll outcome, it
    /// is a recovery that should have been reduced — so the engine refuses the figure and says so
    /// rather than publishing a negative one.
    /// </summary>
    [Theory]
    [InlineData(850, 900)]
    [InlineData(850, 850)]
    [InlineData(1200, 5000)]
    public void Deductions_larger_than_the_pay_leave_no_net_pay_rather_than_a_negative_one(
        decimal salary, decimal recovery)
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd()
            .Basic(salary)
            .Deduction("LOAN", "Loan recovery", recovery)
            .Build());

        Assert.Null(result.NetPay);
        Assert.NotEqual(PayrollResultStatus.Calculated, result.Status);

        var unresolved = Assert.Single(
            result.Unresolved, u => u.Code == UnresolvedCodes.DeductionsExceedEarnings);

        Assert.Equal("NetPay", unresolved.ItemKey);
        Assert.Contains("exceed gross earnings", unresolved.Message);
        Assert.Contains("Loan recovery", unresolved.Message);
        Assert.False(string.IsNullOrWhiteSpace(unresolved.Remedy));

        // The figures that are known are still stated: the officer needs them to fix the recovery.
        Assert.NotNull(result.GrossEarnings);
        Assert.NotNull(result.TotalDeductions);
    }

    /// <summary>
    /// The counterpart: a recovery that fits leaves a net pay, and it is the arithmetic and not a
    /// floor at zero.
    /// </summary>
    [Fact]
    public void A_recovery_that_fits_is_deducted_in_full()
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd()
            .Basic(850m)
            .Deduction("LOAN", "Loan recovery", 100m)
            .Build());

        Assert.Equal(PayrollResultStatus.Calculated, result.Status);
        Assert.Contains(result.Deductions, d => d.Code == "LOAN" && d.Amount.Amount == 100m);
        Assert.Equal(
            result.GrossEarnings!.Value.Amount - result.TotalDeductions!.Value.Amount,
            result.NetPay!.Value.Amount);
        Assert.True(result.NetPay!.Value.Amount > 0m);
    }

    /// <summary>Statutory deductions can never exceed the earnings they are charged on.</summary>
    [Theory]
    [MemberData(nameof(Salaries))]
    public void Statutory_deductions_never_exceed_gross_earnings(decimal salary)
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(salary).Build());

        Assert.True(result.TotalStatutoryDeductions!.Value <= result.GrossEarnings!.Value,
            $"Statutory deductions {result.TotalStatutoryDeductions} exceeded gross " +
            $"{result.GrossEarnings} at a salary of {salary}.");
    }

    /// <summary>Net pay is never negative from statutory deductions alone.</summary>
    [Theory]
    [MemberData(nameof(Salaries))]
    public void Net_pay_is_never_negative_from_statutory_deductions(decimal salary)
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(salary).Build());

        Assert.False(result.NetPay!.Value.IsNegative);
    }

    /// <summary>Insurable earnings never exceed the applicable ceiling.</summary>
    [Theory]
    [MemberData(nameof(Salaries))]
    public void Insurable_earnings_never_exceed_the_ceiling(decimal salary)
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(salary).Build());

        Assert.True(result.NssaInsurableEarnings!.Value.Amount <= 700m);
    }

    /// <summary>Employee and employer NSSA are equal under the seeded rule, and both are capped.</summary>
    [Theory]
    [MemberData(nameof(Salaries))]
    public void Employee_and_employer_nssa_match_under_an_equal_rate_rule(decimal salary)
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(salary).Build());

        Assert.Equal(result.NssaEmployee, result.NssaEmployer);
        Assert.True(result.NssaEmployee!.Value.Amount <= 31.50m);
    }

    /// <summary>Tax rises monotonically with income: more pay never means less tax.</summary>
    [Fact]
    public void Paye_never_decreases_as_income_rises()
    {
        decimal? previous = null;

        for (var salary = 50m; salary <= 6000m; salary += 25m)
        {
            var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(salary).Build());
            var paye = result.PayeAfterCredits!.Value.Amount;

            if (previous is { } last)
            {
                Assert.True(paye >= last,
                    $"PAYE fell from {last} to {paye} as salary rose to {salary}.");
            }

            previous = paye;
        }
    }

    /// <summary>Net pay rises monotonically too — no band boundary makes an employee worse off.</summary>
    [Fact]
    public void Net_pay_never_decreases_as_income_rises()
    {
        decimal? previous = null;

        for (var salary = 50m; salary <= 6000m; salary += 25m)
        {
            var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(salary).Build());
            var net = result.NetPay!.Value.Amount;

            if (previous is { } last)
            {
                Assert.True(net >= last,
                    $"Net pay fell from {last} to {net} as salary rose to {salary}.");
            }

            previous = net;
        }
    }

    /// <summary>The AIDS Levy is always the configured percentage of tax after credits.</summary>
    [Theory]
    [MemberData(nameof(Salaries))]
    public void The_aids_levy_is_always_three_percent_of_tax_after_credits(decimal salary)
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(salary).Build());

        var expected = Math.Round(result.PayeAfterCredits!.Value.Amount * 0.03m, 2,
            MidpointRounding.AwayFromZero);
        Assert.Equal(expected, result.AidsLevy!.Value.Amount);
    }

    /// <summary>Total employer cost is gross plus employer contributions, never less than gross.</summary>
    [Theory]
    [MemberData(nameof(Salaries))]
    public void Total_employer_cost_is_gross_plus_employer_contributions(decimal salary)
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(salary).Build());

        var employerCosts = result.EmployerCosts.Sum(c => c.Amount.Amount);
        Assert.Equal(result.GrossEarnings!.Value.Amount + employerCosts,
            result.TotalEmployerCost!.Value.Amount);
        Assert.True(result.TotalEmployerCost!.Value >= result.GrossEarnings!.Value);
    }

    /// <summary>Employer contributions never reduce the employee's net pay.</summary>
    [Fact]
    public void Employer_contributions_are_not_deducted_from_the_employee()
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(850m).Build());

        var deductionCodes = result.Deductions.Select(d => d.Code).ToList();
        Assert.DoesNotContain("NSSA_POBS_ER", deductionCodes);
        Assert.DoesNotContain("APWCS", deductionCodes);
    }

    /// <summary>Every monetary figure carries the employee's payroll currency.</summary>
    [Theory]
    [InlineData("USD")]
    [InlineData("ZWG")]
    public void Every_figure_is_denominated_in_the_payroll_currency(string currencyCode)
    {
        var currency = new CurrencyCode(currencyCode);
        var builder = currency == CurrencyCode.Usd ? SnapshotBuilder.Usd() : SnapshotBuilder.Zwg();
        var result = Engine.Calculate(builder.Basic(1000m).Overtime(100m).Build());

        foreach (var money in new[]
                 {
                     result.GrossEarnings, result.TaxableIncome, result.NssaEmployee,
                     result.PayeAfterCredits, result.AidsLevy, result.NetPay,
                     result.TotalEmployerCost
                 })
        {
            Assert.Equal(currency, money!.Value.Currency);
        }
    }

    /// <summary>USD and ZiG results are never combined, even by accident.</summary>
    [Fact]
    public void Usd_and_zig_results_cannot_be_added()
    {
        var usd = Engine.Calculate(SnapshotBuilder.Usd().Basic(850m).Build());
        var zig = Engine.Calculate(SnapshotBuilder.Zwg().Basic(12500m).Build());

        Assert.Throws<CurrencyMismatchException>(() =>
            usd.NetPay!.Value + zig.NetPay!.Value);
    }

    /// <summary>
    /// TC-26: the rule version is recorded on the result, so a later rule change cannot silently
    /// reinterpret a historical payroll.
    /// </summary>
    [Fact]
    public void The_rule_versions_used_are_recorded_on_the_result()
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(850m).Build());

        Assert.Contains("PAYE-USD-2026-MONTHLY", result.RuleSnapshot["PayeTable"]);
        Assert.Contains("AIDS-LEVY-2026", result.RuleSnapshot["AidsLevy"]);
        Assert.Contains("NSSA-POBS-2026", result.RuleSnapshot["Nssa"]);
        Assert.Equal(PayrollCalculator.Version, result.EngineVersion);
    }

    /// <summary>The same snapshot always produces the same result — the engine has no hidden state.</summary>
    [Fact]
    public void The_engine_is_deterministic()
    {
        var snapshot = SnapshotBuilder.Usd()
            .Basic(850m).Allowance("HOUSING", "Housing Allowance", 100m).Overtime(75m).Build();

        var first = Engine.Calculate(snapshot);
        var second = Engine.Calculate(snapshot);

        Assert.Equal(first.NetPay, second.NetPay);
        Assert.Equal(first.PayeAfterCredits, second.PayeAfterCredits);
        Assert.Equal(first.Trace.Entries.Count, second.Trace.Entries.Count);
    }

    /// <summary>Rounding boundaries: half a cent must round away from zero, consistently.</summary>
    [Theory]
    [InlineData(300.02, 25)]
    [InlineData(1043.50, 25)]
    [InlineData(1000.10, 25)]
    public void Rounding_is_applied_once_and_away_from_zero(decimal taxable, decimal ratePercent)
    {
        var policy = Tawaka.Payroll.Engine.Rounding.RoundingPolicy.Default;
        var raw = taxable * ratePercent / 100m;

        Assert.Equal(decimal.Round(raw, 2, MidpointRounding.AwayFromZero), policy.Round(raw));
    }

    [Fact]
    public void Every_significant_figure_has_a_trace_entry()
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(850m).Overtime(75m).Build());

        foreach (var item in new[]
                 {
                     "Gross earnings", "NSSA insurable earnings", "NSSA employee", "Taxable income",
                     "PAYE", "AIDS Levy", "Net pay", "Total employer cost"
                 })
        {
            Assert.True(result.Trace.ForItem(item) is not null, $"No trace entry for '{item}'.");
        }
    }

    /// <summary>A traced figure always names the rule that produced it, with its grade.</summary>
    [Fact]
    public void Statutory_figures_cite_their_rule_and_verification_grade()
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(850m).Build());

        var paye = result.Trace.ForItem("PAYE")!;
        Assert.Equal("PAYE-USD-2026-MONTHLY", paye.RuleId);
        Assert.Equal(StatutoryRuleType.PayeTable, paye.RuleType);
        Assert.NotNull(paye.VerificationStatus);
        Assert.NotEmpty(paye.Steps);
    }
}
