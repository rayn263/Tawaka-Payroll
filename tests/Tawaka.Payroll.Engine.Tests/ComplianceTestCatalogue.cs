using Tawaka.Application.Statutory;
using Tawaka.Domain.Common;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Payroll.Engine.Tests;

/// <summary>
/// The 34 test cases from ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md §24, kept together so the whole
/// compliance surface is visible in one test run.
/// <para>
/// Cases split two ways, as required. BEHAVIOUR cases assert engine behaviour and pass
/// independently of any statutory rate. SEED cases assert arithmetic against the clearly labelled
/// temporary seed rules; their expected values live in <see cref="SeedExpectations"/> and are
/// recomputed from the authoritative rule dataset once verified 2026 tables are loaded.
/// </para>
/// <para>
/// Cases that need the calculation engine (Milestone 3) are present and explicitly skipped rather
/// than omitted, so the outstanding work is counted in every test run instead of being forgotten.
/// </para>
/// </summary>
public class ComplianceTestCatalogue
{
    private const string NeedsEngine =
        "Pending Milestone 3 (calculation engine). Expected values recorded in SeedExpectations.";

    private const string NeedsEmployees =
        "Pending Milestone 2 (employee and contract model).";

    private const string NeedsObligations =
        "Pending Milestone 4 (statutory obligation register).";

    private static readonly DateOnly PayDate = new(2026, 9, 30);

    // ---- Implemented now -------------------------------------------------------------------

    /// <summary>TC-26: historical payroll keeps the rule version that applied at the time.</summary>
    [Fact]
    public void TC_26_Historical_payroll_after_a_rate_change_uses_the_original_rule()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Verified, 2026),
            TestRules.PayeTable("PAYE-USD-2027-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Verified, 2027));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<TaxRule>(new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.PayeTable,
            EffectiveDate = PayDate,
            Currency = CurrencyCode.Usd,
            PeriodBasis = PeriodBasis.Monthly
        });

        Assert.Equal("PAYE-USD-2026-MONTHLY", resolution.Rule!.RuleId);
    }

    /// <summary>TC-31: an unverified rule blocks live payroll and names itself.</summary>
    [Fact]
    public void TC_31_Unverified_rule_blocks_live_payroll()
    {
        var source = new TestRuleSource().Add(TestRules.Apwcs(VerificationStatus.Unverified));
        var gate = new LivePayrollGate(new StatutoryRuleResolver(source));

        var report = gate.Check(new[]
        {
            new StatutoryRuleQuery
            {
                RuleType = StatutoryRuleType.Apwcs,
                EffectiveDate = PayDate,
                Currency = CurrencyCode.Usd
            }
        });

        Assert.True(report.IsBlocked);
        Assert.Contains("LIVE PAYROLL BLOCKED", report.Render());
        Assert.Equal("APWCS-TEST", report.BlockingRules[0].RuleId);
    }

    /// <summary>TC-32: the same rule calculates in development mode.</summary>
    [Fact]
    public void TC_32_Unverified_rule_is_usable_in_development_mode()
    {
        var source = new TestRuleSource().Add(TestRules.Apwcs(VerificationStatus.Unverified));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<ApwcsRule>(new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.Apwcs,
            EffectiveDate = PayDate,
            Currency = CurrencyCode.Usd,
            Mode = PayrollMode.Development
        });

        Assert.True(resolution.Succeeded);
        Assert.False(resolution.Rule!.VerificationStatus.IsUsableInLivePayroll());
    }

    /// <summary>TC-34: a missing period table blocks; it is never derived from another basis.</summary>
    [Fact]
    public void TC_34_Missing_weekly_table_blocks_rather_than_deriving_from_monthly()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Verified));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<TaxRule>(new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.PayeTable,
            EffectiveDate = PayDate,
            Currency = CurrencyCode.Usd,
            PeriodBasis = PeriodBasis.Weekly
        });

        Assert.False(resolution.Succeeded);
    }

    // ---- Pending: statutory seed arithmetic (needs the calculation engine) -------------------

    [Fact(Skip = NeedsEngine)] public void TC_01_Usd_permanent_employee() { }
    [Fact(Skip = NeedsEngine)] public void TC_02_Zig_permanent_employee() { }
    [Fact(Skip = NeedsEngine)] public void TC_03_Usd_below_threshold() { }
    [Fact(Skip = NeedsEngine)] public void TC_04_Zig_below_threshold() { }
    [Fact(Skip = NeedsEngine)] public void TC_05_High_income_usd() { }
    [Fact(Skip = NeedsEngine)] public void TC_06_High_income_zig() { }
    [Fact(Skip = NeedsEngine)] public void TC_09_Overtime_excluded_from_nssa() { }
    [Fact(Skip = NeedsEngine)] public void TC_10_Annual_bonus_exemption() { }
    [Fact(Skip = NeedsEngine)] public void TC_11_Mixed_allowances_with_reimbursement() { }
    [Fact(Skip = NeedsEngine)] public void TC_12_Nssa_below_ceiling() { }
    [Fact(Skip = NeedsEngine)] public void TC_13_Nssa_ceiling_exceeded() { }
    [Fact(Skip = NeedsEngine)] public void TC_21_Elderly_tax_credit_reduces_aids_levy() { }
    [Fact(Skip = NeedsEngine)] public void TC_22_Zero_paye_still_shows_on_payslip() { }
    [Fact(Skip = NeedsEngine)] public void TC_23_Zero_nssa_for_employee_over_65() { }

    // ---- Pending: multi-currency (needs the engine and an approved strategy) -----------------

    [Fact(Skip = NeedsEngine)] public void TC_07_Usd_salary_plus_zig_allowance_is_blocked_in_live_mode() { }
    [Fact(Skip = NeedsEngine)] public void TC_08_Zig_salary_plus_usd_allowance() { }
    [Fact(Skip = NeedsEngine)] public void TC_24_Mixed_currency_totals_are_never_added() { }
    [Fact(Skip = NeedsEngine)] public void TC_25_Mixed_currency_run_creates_obligations_per_currency() { }
    [Fact(Skip = NeedsEngine)] public void TC_27_Exchange_rate_change_after_finalisation_changes_nothing() { }

    // ---- Pending: employment types and non-monthly payroll ----------------------------------

    [Fact(Skip = NeedsEmployees)] public void TC_14_Casual_under_18_days_pays_no_nssa() { }
    [Fact(Skip = NeedsEmployees)] public void TC_14b_Casual_at_20_days_pays_nssa() { }
    [Fact(Skip = NeedsEmployees)] public void TC_15_Project_employee_cost_attributed_to_project() { }
    [Fact(Skip = NeedsEmployees)] public void TC_16_Weekly_employee_uses_the_official_weekly_table() { }
    [Fact(Skip = NeedsEmployees)] public void TC_17_Fortnightly_employee_uses_the_official_table() { }
    [Fact(Skip = NeedsEmployees)] public void TC_18_Part_time_ceiling_is_not_pro_rated() { }
    [Fact(Skip = NeedsEmployees)] public void TC_19_Loan_deducted_post_tax() { }
    [Fact(Skip = NeedsEmployees)] public void TC_20_Advance_recovered_post_tax() { }
    [Fact(Skip = NeedsEmployees)] public void TC_33_Casual_six_week_threshold_warns_without_reclassifying() { }

    // ---- Pending: statutory obligations ------------------------------------------------------

    [Fact(Skip = NeedsObligations)] public void TC_28_Statutory_payment_outstanding() { }
    [Fact(Skip = NeedsObligations)] public void TC_29_Statutory_payment_completed() { }
    [Fact(Skip = NeedsObligations)] public void TC_29b_Partial_statutory_payment() { }

    // TC-30 (locked payroll modification) is implemented in Tawaka.Infrastructure.Tests, where the
    // lock interceptor it exercises actually lives.

    /// <summary>
    /// Guards the recorded seed expectations against transcription error: gross less deductions
    /// must equal the stated net pay for every case.
    /// </summary>
    [Fact]
    public void Seed_expectations_are_internally_consistent()
    {
        Assert.NotEmpty(SeedExpectations.UsdMonthly);

        foreach (var e in SeedExpectations.UsdMonthly)
        {
            var net = e.Gross - e.NssaEmployee - e.PayeAfterCredits - e.AidsLevy;
            Assert.Equal(e.NetPay, decimal.Round(net, 2));
        }
    }

    /// <summary>
    /// TC-21 exists to prove the AIDS Levy is charged on tax after credits (spec Q2): the levy
    /// must be exactly 3% of the post-credit tax, not of the pre-credit tax.
    /// </summary>
    [Fact]
    public void Seed_expectation_confirms_aids_levy_is_charged_after_credits()
    {
        var withCredit = SeedExpectations.UsdMonthly.Single(e => e.Case == "TC-21");
        var withoutCredit = SeedExpectations.UsdMonthly.Single(e => e.Case == "TC-01");

        Assert.Equal(withCredit.AidsLevy, decimal.Round(withCredit.PayeAfterCredits * 0.03m, 2));
        Assert.Equal(75.00m, withoutCredit.PayeAfterCredits - withCredit.PayeAfterCredits);
        Assert.True(withCredit.AidsLevy < withoutCredit.AidsLevy);
    }
}

/// <summary>
/// Expected results for the seed arithmetic cases, computed from the seed tables in the compliance
/// specification and verified by hand.
/// <para>
/// These are deliberately data rather than constants buried in assertions: when verified 2026
/// tables are loaded, this table is regenerated from the authoritative rule dataset and the tests
/// that consume it update with it.
/// </para>
/// </summary>
public static class SeedExpectations
{
    public sealed record Expectation(
        string Case,
        decimal Gross,
        decimal NssaEmployee,
        decimal TaxableIncome,
        decimal PayeAfterCredits,
        decimal AidsLevy,
        decimal NetPay,
        string Note);

    /// <summary>Seed basis: USD monthly 0/20/25/40 bands, NSSA 4.5% capped at USD 700, levy 3%.</summary>
    public static readonly IReadOnlyList<Expectation> UsdMonthly = new[]
    {
        new Expectation("TC-01", 1075.00m, 31.50m, 1043.50m, 225.88m, 6.78m, 810.84m,
            "Baseline: basic 850 + housing 100 + transport 50 + overtime 75."),
        new Expectation("TC-03", 90.00m, 4.05m, 85.95m, 0.00m, 0.00m, 85.95m,
            "Below the tax-free threshold; statutory lines still shown at zero."),
        new Expectation("TC-05", 5000.00m, 31.50m, 4968.50m, 1502.40m, 45.07m, 3421.03m,
            "Top 40% band reached; NSSA capped at the ceiling."),
        new Expectation("TC-09", 800.00m, 27.00m, 773.00m, 158.25m, 4.75m, 610.00m,
            "Basic 600 + overtime 200: PAYE on 800, NSSA on 600 only."),
        new Expectation("TC-10", 1850.00m, 31.50m, 1118.50m, 244.63m, 7.34m, 1566.53m,
            "Basic 850 + bonus 1000, first 700 of bonus exempt, bonus excluded from NSSA."),
        new Expectation("TC-12", 500.00m, 22.50m, 477.50m, 84.38m, 2.53m, 390.59m,
            "Below the NSSA ceiling: 4.5% of actual earnings."),
        new Expectation("TC-13", 2000.00m, 31.50m, 1968.50m, 457.13m, 13.71m, 1497.66m,
            "NSSA capped at 31.50, not 90.00."),
        new Expectation("TC-21", 1075.00m, 31.50m, 1043.50m, 150.88m, 4.53m, 888.09m,
            "As TC-01 with the USD 75 elderly credit: levy falls to 4.53, proving it is charged " +
            "on tax AFTER credits.")
    };

}
