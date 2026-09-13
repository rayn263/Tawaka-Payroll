using Tawaka.Domain.Common;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Inputs;
using Tawaka.Payroll.Engine.Results;
using Xunit;

namespace Tawaka.Payroll.Engine.Tests;

/// <summary>
/// Compliance cases TC-01 to TC-13 and TC-21 to TC-23 from the specification, now executable
/// against the real engine. Expected values are those recorded in <see cref="SeedExpectations"/>
/// and verified by hand.
/// </summary>
public class PayrollCalculationTests
{
    private static readonly PayrollCalculator Engine = new();

    private static PayrollResult Run(PayrollInputSnapshot snapshot) => Engine.Calculate(snapshot);

    private static void AssertMatches(PayrollResult result, string caseName)
    {
        var expected = SeedExpectations.UsdMonthly.Single(e => e.Case == caseName);

        Assert.Equal(PayrollResultStatus.Calculated, result.Status);
        Assert.Equal(expected.Gross, result.GrossEarnings!.Value.Amount);
        Assert.Equal(expected.NssaEmployee, result.NssaEmployee!.Value.Amount);
        Assert.Equal(expected.TaxableIncome, result.TaxableIncome!.Value.Amount);
        Assert.Equal(expected.PayeAfterCredits, result.PayeAfterCredits!.Value.Amount);
        Assert.Equal(expected.AidsLevy, result.AidsLevy!.Value.Amount);
        Assert.Equal(expected.NetPay, result.NetPay!.Value.Amount);
    }

    /// <summary>TC-01: the baseline monthly USD employee.</summary>
    [Fact]
    public void TC_01_Usd_permanent_employee()
    {
        var result = Run(SnapshotBuilder.Usd()
            .Basic(850m)
            .Allowance("HOUSING", "Housing Allowance", 100m)
            .Allowance("TRANSPORT", "Transport Allowance", 50m)
            .Overtime(75m)
            .Build());

        AssertMatches(result, "TC-01");
        Assert.Equal(CurrencyCode.Usd, result.Currency);
        Assert.Equal(31.50m, result.NssaEmployer!.Value.Amount);
    }

    /// <summary>TC-02: a ZiG employee is taxed on the ZiG table; no USD figure appears anywhere.</summary>
    [Fact]
    public void TC_02_Zig_permanent_employee()
    {
        var result = Run(SnapshotBuilder.Zwg().Basic(12500m).Build());

        Assert.Equal(PayrollResultStatus.Calculated, result.Status);
        Assert.Equal(CurrencyCode.Zwg, result.Currency);
        Assert.Equal(12500m, result.GrossEarnings!.Value.Amount);
        Assert.Contains("PAYE-ZWG-2026-MONTHLY", result.RuleSnapshot["PayeTable"]);

        // Every monetary figure is denominated in ZiG.
        foreach (var line in result.Earnings)
        {
            Assert.Equal(CurrencyCode.Zwg, line.Amount.Currency);
        }

        Assert.Equal(CurrencyCode.Zwg, result.NetPay!.Value.Currency);
    }

    /// <summary>TC-03: below the threshold, PAYE is a legitimate zero — not an unresolved figure.</summary>
    [Fact]
    public void TC_03_Usd_below_threshold_produces_a_real_zero()
    {
        var result = Run(SnapshotBuilder.Usd().Basic(90m).Build());

        AssertMatches(result, "TC-03");
        Assert.Equal(Money.Usd(0m), result.PayeAfterCredits);
        Assert.Equal(Money.Usd(0m), result.AidsLevy);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void TC_04_Zig_below_threshold()
    {
        var result = Run(SnapshotBuilder.Zwg().Basic(2500m).Build());

        Assert.Equal(PayrollResultStatus.Calculated, result.Status);
        Assert.Equal(0m, result.PayeAfterCredits!.Value.Amount);
        Assert.Empty(result.Unresolved);
    }

    /// <summary>TC-05: the top band, with NSSA capped.</summary>
    [Fact]
    public void TC_05_High_income_usd()
    {
        var result = Run(SnapshotBuilder.Usd().Basic(5000m).Build());

        AssertMatches(result, "TC-05");
        Assert.Equal(700m, result.NssaInsurableEarnings!.Value.Amount);
    }

    [Fact]
    public void TC_06_High_income_zig_reaches_the_top_band()
    {
        var result = Run(SnapshotBuilder.Zwg().Basic(150000m).Build());

        Assert.Equal(PayrollResultStatus.Calculated, result.Status);
        Assert.True(result.PayeAfterCredits!.Value.Amount > 0m);
        Assert.Equal(700m, result.NssaInsurableEarnings!.Value.Amount);
    }

    /// <summary>TC-09: overtime is taxed but excluded from insurable earnings.</summary>
    [Fact]
    public void TC_09_Overtime_is_taxed_but_excluded_from_nssa()
    {
        var result = Run(SnapshotBuilder.Usd().Basic(600m).Overtime(200m).Build());

        AssertMatches(result, "TC-09");
        Assert.Equal(600m, result.NssaInsurableEarnings!.Value.Amount);
        Assert.Equal(800m, result.GrossEarnings!.Value.Amount);
    }

    /// <summary>TC-10: the bonus exemption applies, and the bonus is outside insurable earnings.</summary>
    [Fact]
    public void TC_10_Annual_bonus_exemption()
    {
        var result = Run(SnapshotBuilder.Usd().Basic(850m).Bonus(1000m).Build());

        AssertMatches(result, "TC-10");

        var bonus = result.Earnings.Single(e => e.Code == "BONUS_ANNUAL");
        Assert.Equal(700m, bonus.ExemptAmount.Amount);
        Assert.Equal(300m, bonus.TaxableAmount.Amount);
        Assert.Equal(700m, result.NssaInsurableEarnings!.Value.Amount);
    }

    /// <summary>The exemption is tracked across the year, so it cannot be claimed twice.</summary>
    [Fact]
    public void The_bonus_exemption_cannot_be_claimed_twice_in_one_tax_year()
    {
        var result = Run(SnapshotBuilder.Usd()
            .Basic(850m)
            .Bonus(1000m)
            .BonusExemptionUsed(700m)
            .Build());

        var bonus = result.Earnings.Single(e => e.Code == "BONUS_ANNUAL");
        Assert.Equal(0m, bonus.ExemptAmount.Amount);
        Assert.Equal(1000m, bonus.TaxableAmount.Amount);
    }

    /// <summary>TC-11: a vouched reimbursement is exempt and outside gross pay.</summary>
    [Fact]
    public void TC_11_Reimbursement_is_exempt_and_outside_gross()
    {
        var result = Run(SnapshotBuilder.Usd()
            .Basic(700m)
            .Allowance("TRANSPORT", "Transport Allowance", 100m)
            .Reimbursement(80m)
            .Build());

        Assert.Equal(800m, result.GrossEarnings!.Value.Amount);
        Assert.Contains(result.Earnings, e => e.Code == "REIMB" && !e.IsIncludedInGross);
    }

    [Fact]
    public void TC_12_Nssa_below_the_ceiling_is_proportional()
    {
        var result = Run(SnapshotBuilder.Usd().Basic(500m).Build());

        AssertMatches(result, "TC-12");
        Assert.Equal(500m, result.NssaInsurableEarnings!.Value.Amount);
    }

    /// <summary>TC-13: above the ceiling, NSSA is capped — not 4.5% of actual earnings.</summary>
    [Fact]
    public void TC_13_Nssa_ceiling_binds()
    {
        var result = Run(SnapshotBuilder.Usd().Basic(2000m).Build());

        AssertMatches(result, "TC-13");
        Assert.Equal(31.50m, result.NssaEmployee!.Value.Amount);
        Assert.NotEqual(90m, result.NssaEmployee!.Value.Amount);
    }

    /// <summary>
    /// TC-21: the elderly credit reduces tax, and the AIDS Levy falls with it — which is what
    /// proves the levy is charged on tax after credits (compliance question Q2).
    /// </summary>
    [Fact]
    public void TC_21_Tax_credit_reduces_the_aids_levy()
    {
        var withCredit = Run(SnapshotBuilder.Usd()
            .Basic(850m)
            .Allowance("HOUSING", "Housing Allowance", 100m)
            .Allowance("TRANSPORT", "Transport Allowance", 50m)
            .Overtime(75m)
            .WithProfile(p => p with { Age = 57, IsElderlyCreditEligible = true })
            .Build());

        AssertMatches(withCredit, "TC-21");
        Assert.Equal(75m, withCredit.TaxCredits!.Value.Amount);
        Assert.Equal(225.88m, withCredit.PayeBeforeCredits!.Value.Amount);
        Assert.Equal(150.88m, withCredit.PayeAfterCredits!.Value.Amount);
        Assert.Equal(4.53m, withCredit.AidsLevy!.Value.Amount);
    }

    /// <summary>Credits cannot reduce tax below zero, and the excess is never refunded.</summary>
    [Fact]
    public void Tax_credits_are_capped_at_the_tax_chargeable()
    {
        var result = Run(SnapshotBuilder.Usd()
            .Basic(250m)
            .WithProfile(p => p with
            {
                Age = 57, IsElderlyCreditEligible = true, IsDisabledCreditEligible = true
            })
            .Build());

        Assert.True(result.TaxCredits!.Value.Amount <= result.PayeBeforeCredits!.Value.Amount);
        Assert.Equal(0m, result.PayeAfterCredits!.Value.Amount);
        Assert.Equal(0m, result.AidsLevy!.Value.Amount);
        Assert.False(result.NetPay!.Value.IsNegative);
    }

    [Fact]
    public void TC_22_Zero_paye_is_a_calculated_result_not_a_missing_one()
    {
        var result = Run(SnapshotBuilder.Usd().Basic(95m).Build());

        Assert.Equal(PayrollResultStatus.Calculated, result.Status);
        Assert.NotNull(result.PayeAfterCredits);
        Assert.Equal(0m, result.PayeAfterCredits!.Value.Amount);
        Assert.Empty(result.Unresolved);
    }

    /// <summary>TC-23: contributions cease above the maximum age — a legitimate zero.</summary>
    [Fact]
    public void TC_23_Employee_over_65_pays_no_nssa()
    {
        var result = Run(SnapshotBuilder.Usd()
            .Basic(850m)
            .WithProfile(p => p with { Age = 66 })
            .Build());

        Assert.Equal(PayrollResultStatus.Calculated, result.Status);
        Assert.Equal(0m, result.NssaEmployee!.Value.Amount);
        Assert.Equal(0m, result.NssaEmployer!.Value.Amount);
        Assert.Empty(result.Unresolved);

        var trace = result.Trace.ForItem("NSSA employee");
        Assert.Contains("cease", trace!.Explanation!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>TC-14: a casual engaged for fewer than 18 days in the month does not contribute.</summary>
    [Fact]
    public void TC_14_Casual_under_18_days_pays_no_nssa()
    {
        var result = Run(SnapshotBuilder.Usd()
            .OfType("Casual")
            .Basic(300m)
            .WithTimesheet(new TimesheetInput { DaysWorked = 12m, DaysEngagedInMonth = 12m, IsApproved = true })
            .Build());

        Assert.Equal(PayrollResultStatus.Calculated, result.Status);
        Assert.Equal(0m, result.NssaEmployee!.Value.Amount);
        Assert.Contains("18-day", result.Trace.ForItem("NSSA employee")!.Explanation!);
    }

    [Fact]
    public void TC_14b_Casual_at_20_days_contributes()
    {
        var result = Run(SnapshotBuilder.Usd()
            .OfType("Casual")
            .Basic(500m)
            .WithTimesheet(new TimesheetInput { DaysWorked = 20m, DaysEngagedInMonth = 20m, IsApproved = true })
            .Build());

        Assert.Equal(22.50m, result.NssaEmployee!.Value.Amount);
    }

    /// <summary>An employment type the scheme does not cover produces a genuine zero.</summary>
    [Fact]
    public void An_excluded_employment_type_produces_zero_nssa()
    {
        var rules = SeedRules.For(CurrencyCode.Usd, PeriodBasis.Monthly, "Domestic") with
        {
            NssaEligibility = SeedRules.Eligibility("Domestic", eligible: false)
        };

        var result = Run(SnapshotBuilder.Usd().OfType("Domestic").Basic(400m).WithRules(rules).Build());

        Assert.Equal(0m, result.NssaEmployee!.Value.Amount);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void TC_19_Loans_are_deducted_after_tax()
    {
        var result = Run(SnapshotBuilder.Usd()
            .Basic(850m)
            .Deduction("LOAN", "Loan Repayment", 100m)
            .Build());

        var withoutLoan = Run(SnapshotBuilder.Usd().Basic(850m).Build());

        // The loan reduces net pay but not the tax.
        Assert.Equal(withoutLoan.TaxableIncome, result.TaxableIncome);
        Assert.Equal(withoutLoan.PayeAfterCredits, result.PayeAfterCredits);
        Assert.Equal(withoutLoan.NetPay!.Value.Amount - 100m, result.NetPay!.Value.Amount);
    }

    [Fact]
    public void A_pre_tax_deduction_reduces_taxable_income()
    {
        var result = Run(SnapshotBuilder.Usd()
            .Basic(850m)
            .Deduction("PENSION", "Pension Contribution", 50m, reducesTaxable: true)
            .Build());

        var without = Run(SnapshotBuilder.Usd().Basic(850m).Build());

        Assert.Equal(without.TaxableIncome!.Value.Amount - 50m, result.TaxableIncome!.Value.Amount);
    }

    /// <summary>TC-15: employer cost is apportioned across the projects worked on.</summary>
    [Fact]
    public void TC_15_Employer_cost_is_allocated_across_projects()
    {
        var result = Run(SnapshotBuilder.Usd()
            .Basic(1000m)
            .AllocateTo("Nyanga Shop Renovation", 60m)
            .AllocateTo("Harare Depot", 40m)
            .Build());

        Assert.Equal(2, result.CostAllocations.Count);

        var total = result.TotalEmployerCost!.Value.Amount;
        var allocated = result.CostAllocations.Sum(a => a.AllocatedCost.Amount);
        Assert.Equal(total, allocated);
        Assert.Equal(Math.Round(total * 0.6m, 2), result.CostAllocations[0].AllocatedCost.Amount);
    }
}
