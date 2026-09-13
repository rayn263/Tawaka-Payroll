using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Inputs;
using Tawaka.Payroll.Engine.Results;
using Xunit;

namespace Tawaka.Payroll.Engine.Tests;

/// <summary>
/// The engine's refusal behaviour. The rule these tests protect: <b>a missing or unverified
/// statutory rule is not a zero deduction.</b> Substituting 0.00 would produce a payslip that
/// looks complete and is wrong, and would hide the compliance question behind a plausible number.
/// </summary>
public class UnresolvedRuleTests
{
    private static readonly PayrollCalculator Engine = new();

    /// <summary>TC-34 and the Q22 refusal: a weekly run with an undetermined ceiling application.</summary>
    [Fact]
    public void Weekly_payroll_refuses_when_the_nssa_ceiling_application_is_undetermined()
    {
        var rules = SeedRules.For(CurrencyCode.Usd, PeriodBasis.Weekly, "HourlyPaid") with
        {
            Nssa = SeedRules.Nssa(CurrencyCode.Usd,
                ceilingApplication: CeilingApplicationMethod.NotDetermined)
        };

        var result = Engine.Calculate(SnapshotBuilder.Usd()
            .WithPeriod(PeriodBasis.Weekly)
            .OfType("HourlyPaid")
            .Basic(150m)
            .WithRules(rules)
            .Build());

        Assert.Equal(PayrollResultStatus.Blocked, result.Status);

        var unresolved = result.Unresolved.Single(u =>
            u.Code == UnresolvedCodes.NssaCeilingApplicationUnresolved);
        Assert.Equal("Q22", unresolved.ComplianceQuestion);
        Assert.Contains("weekly", unresolved.Message, StringComparison.OrdinalIgnoreCase);

        // Critically: NOT zero. The figure is absent, and says why.
        Assert.Null(result.NssaEmployee);
        Assert.Null(result.NetPay);
    }

    /// <summary>
    /// Monthly payroll is unaffected: a monthly ceiling applied to a monthly period needs no
    /// conversion rule, so an undetermined application method must not block it.
    /// </summary>
    [Fact]
    public void Monthly_payroll_is_not_blocked_by_an_undetermined_ceiling_application()
    {
        var rules = SeedRules.For(CurrencyCode.Usd, PeriodBasis.Monthly, "Permanent") with
        {
            Nssa = SeedRules.Nssa(CurrencyCode.Usd,
                ceilingApplication: CeilingApplicationMethod.NotDetermined)
        };

        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(850m).WithRules(rules).Build());

        Assert.Equal(PayrollResultStatus.Calculated, result.Status);
        Assert.Equal(700m, result.NssaInsurableEarnings!.Value.Amount);
        Assert.Equal(31.50m, result.NssaEmployee!.Value.Amount);
    }

    /// <summary>With the application method set, the same weekly run calculates.</summary>
    [Fact]
    public void Weekly_payroll_calculates_once_the_ceiling_application_is_set()
    {
        var rules = SeedRules.For(CurrencyCode.Usd, PeriodBasis.Weekly, "HourlyPaid") with
        {
            PayeTable = WeeklyTable(),
            Nssa = SeedRules.Nssa(CurrencyCode.Usd,
                ceilingApplication: CeilingApplicationMethod.ProRataByPeriodLength)
        };

        var result = Engine.Calculate(SnapshotBuilder.Usd()
            .WithPeriod(PeriodBasis.Weekly)
            .OfType("HourlyPaid")
            .Basic(200m)
            .WithRules(rules)
            .Build());

        Assert.Equal(PayrollResultStatus.Calculated, result.Status);

        // The monthly USD 700 ceiling pro-rates to 700 x 12/52 = 161.538…
        var expectedCeiling = Math.Round(700m * 12m / 52m, 4);
        Assert.True(result.NssaInsurableEarnings!.Value.Amount <= expectedCeiling + 0.01m);
        Assert.Contains("pro-rated",
            result.Trace.ForItem("NSSA insurable earnings")!.Inputs["Ceiling basis"]);
    }

    [Fact]
    public void Fortnightly_payroll_pro_rates_the_ceiling_differently_from_weekly()
    {
        var rules = SeedRules.For(CurrencyCode.Usd, PeriodBasis.Fortnightly, "Permanent") with
        {
            PayeTable = FortnightlyTable(),
            Nssa = SeedRules.Nssa(CurrencyCode.Usd,
                ceilingApplication: CeilingApplicationMethod.ProRataByPeriodLength)
        };

        var result = Engine.Calculate(SnapshotBuilder.Usd()
            .WithPeriod(PeriodBasis.Fortnightly)
            .Basic(1000m)
            .WithRules(rules)
            .Build());

        // 700 x 12/26 = 323.0769…
        Assert.Equal(PayrollResultStatus.Calculated, result.Status);
        Assert.Equal(Math.Round(700m * 12m / 26m, 4),
            Math.Round(result.NssaInsurableEarnings!.Value.Amount, 4));
    }

    /// <summary>A missing PAYE table blocks; the engine does not fall back to another basis.</summary>
    [Fact]
    public void A_missing_paye_table_blocks_rather_than_producing_zero_tax()
    {
        var rules = SeedRules.For(CurrencyCode.Usd, PeriodBasis.Monthly, "Permanent") with
        {
            PayeTable = null
        };

        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(850m).WithRules(rules).Build());

        Assert.Equal(PayrollResultStatus.Blocked, result.Status);
        Assert.Contains(result.Unresolved, u => u.Code == UnresolvedCodes.PayeTableUnresolved);
        Assert.Null(result.PayeAfterCredits);
        Assert.Null(result.NetPay);
    }

    /// <summary>Q26: the published table form without its fixed-deduction column refuses.</summary>
    [Fact]
    public void A_published_form_table_without_its_fixed_deduction_column_refuses()
    {
        var rules = SeedRules.For(CurrencyCode.Usd, PeriodBasis.Monthly, "Permanent") with
        {
            PayeTable = SeedRules.UsdMonthlyTable(
                application: TaxBracketApplication.RateLessFixedDeduction)
        };

        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(850m).WithRules(rules).Build());

        Assert.Equal(PayrollResultStatus.Blocked, result.Status);
        var unresolved = result.Unresolved.Single(u =>
            u.Code == UnresolvedCodes.PayeFixedDeductionUnresolved);
        Assert.Equal("Q26", unresolved.ComplianceQuestion);
    }

    /// <summary>Q6: the APWCS rate is assigned by NSSA and is never estimated.</summary>
    [Fact]
    public void A_missing_apwcs_rate_blocks_rather_than_being_estimated()
    {
        var rules = SeedRules.For(CurrencyCode.Usd, PeriodBasis.Monthly, "Permanent") with
        {
            Apwcs = null
        };

        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(850m).WithRules(rules).Build());

        Assert.Equal(PayrollResultStatus.Blocked, result.Status);
        var unresolved = result.Unresolved.Single(u => u.Code == UnresolvedCodes.ApwcsRuleUnresolved);
        Assert.Equal("Q6", unresolved.ComplianceQuestion);
        Assert.Contains("WC50", unresolved.Remedy!);
        Assert.Null(result.Apwcs);
    }

    [Fact]
    public void A_missing_aids_levy_rule_blocks()
    {
        var rules = SeedRules.For(CurrencyCode.Usd, PeriodBasis.Monthly, "Permanent") with
        {
            AidsLevy = null
        };

        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(850m).WithRules(rules).Build());

        Assert.Equal(PayrollResultStatus.Blocked, result.Status);
        Assert.Contains(result.Unresolved, u => u.Code == UnresolvedCodes.AidsLevyRuleUnresolved);
        Assert.Null(result.AidsLevy);
    }

    [Fact]
    public void A_missing_nssa_eligibility_rule_blocks_rather_than_assuming_coverage()
    {
        var rules = SeedRules.For(CurrencyCode.Usd, PeriodBasis.Monthly, "Intern") with
        {
            NssaEligibility = null
        };

        var result = Engine.Calculate(SnapshotBuilder.Usd().OfType("Intern").Basic(400m)
            .WithRules(rules).Build());

        Assert.Equal(PayrollResultStatus.Blocked, result.Status);
        Assert.Contains(result.Unresolved, u => u.Code == UnresolvedCodes.NssaEligibilityUnresolved);
        Assert.Null(result.NssaEmployee);
    }

    /// <summary>TC-07: mixed-currency remuneration without an approved strategy refuses.</summary>
    [Fact]
    public void Mixed_currency_earnings_refuse_without_an_approved_strategy()
    {
        var result = Engine.Calculate(SnapshotBuilder.Usd()
            .Basic(800m)
            .AllowanceInCurrency("TRANSPORT", "Transport Allowance", 2000m, CurrencyCode.Zwg)
            .Build());

        Assert.Equal(PayrollResultStatus.Blocked, result.Status);
        var unresolved = result.Unresolved.Single(u =>
            u.Code == UnresolvedCodes.CurrencyStrategyUnresolved);
        Assert.Equal("Q1", unresolved.ComplianceQuestion);
    }

    /// <summary>
    /// In LIVE mode an unverified earning treatment blocks; in development it calculates, so the
    /// business can model payroll while the compliance work proceeds.
    /// </summary>
    [Fact]
    public void An_unverified_earning_treatment_blocks_live_but_not_development()
    {
        PayrollInputSnapshot Build(bool live)
        {
            var builder = SnapshotBuilder.Usd()
                .Basic(850m)
                .Allowance("RISK", "Risk Allowance", 100m,
                    treatment: VerificationStatus.Unverified);
            return (live ? builder.InLiveMode() : builder).Build();
        }

        var development = Engine.Calculate(Build(live: false));
        var live = Engine.Calculate(Build(live: true));

        Assert.Equal(PayrollResultStatus.Calculated, development.Status);
        Assert.Equal(PayrollResultStatus.Blocked, live.Status);
        Assert.Contains(live.Unresolved, u => u.Code == UnresolvedCodes.EarningTreatmentUnverified);
    }

    /// <summary>Every refusal names the rule and what would clear it.</summary>
    [Fact]
    public void Every_unresolved_item_explains_itself()
    {
        var rules = SeedRules.For(CurrencyCode.Usd, PeriodBasis.Monthly, "Permanent") with
        {
            PayeTable = null, Apwcs = null
        };

        var result = Engine.Calculate(SnapshotBuilder.Usd().Basic(850m).WithRules(rules).Build());

        Assert.NotEmpty(result.Unresolved);
        foreach (var unresolved in result.Unresolved)
        {
            Assert.False(string.IsNullOrWhiteSpace(unresolved.Code));
            Assert.False(string.IsNullOrWhiteSpace(unresolved.Message));
            Assert.False(string.IsNullOrWhiteSpace(unresolved.Remedy));
        }
    }

    private static TaxRule WeeklyTable()
    {
        var rule = SeedRules.UsdMonthlyTable();
        rule.RuleId = "PAYE-USD-2026-WEEKLY";
        rule.PeriodBasis = PeriodBasis.Weekly;
        rule.Brackets.Clear();
        foreach (var (sequence, lower, upper, rate) in new (int, decimal, decimal?, decimal)[]
                 {
                     (1, 0m, 23m, 0m), (2, 23m, 69m, 0.20m), (3, 69m, 692m, 0.25m),
                     (4, 692m, null, 0.40m)
                 })
        {
            rule.Brackets.Add(new TaxBracket
            {
                Sequence = sequence, LowerBound = lower, UpperBound = upper, Rate = rate
            });
        }

        return rule;
    }

    private static TaxRule FortnightlyTable()
    {
        var rule = WeeklyTable();
        rule.RuleId = "PAYE-USD-2026-FORTNIGHTLY";
        rule.PeriodBasis = PeriodBasis.Fortnightly;
        return rule;
    }
}
