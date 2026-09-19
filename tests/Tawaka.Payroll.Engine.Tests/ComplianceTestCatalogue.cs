using Tawaka.Application.Statutory;
using Tawaka.Domain.Common;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Payroll.Engine.Tests;

/// <summary>
/// The 34 test cases from COMPLIANCE_SPEC.md §24, kept together so the whole
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
    private const string NeedsEmployees =
        "Pending: needs the loans and advances module (Milestone 5).";

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

    // ---- Implemented elsewhere ---------------------------------------------------------------
    //
    // Milestone 3 activated most of the catalogue against the real engine. Rather than duplicate
    // the assertions here, each case is listed with where it now lives:
    //
    //   PayrollCalculationTests      TC-01 TC-02 TC-03 TC-04 TC-05 TC-06 TC-09 TC-10 TC-11
    //                                TC-12 TC-13 TC-14 TC-14b TC-15 TC-19 TC-21 TC-22 TC-23
    //   UnresolvedRuleTests          TC-07 TC-08 TC-16 TC-17 TC-34
    //   InvariantTests               TC-24 TC-26, plus the financial invariants
    //   PayrollRunTests (infra)      TC-25 TC-27 TC-30, historical contract and tax-rule
    //                                reproducibility, and the approval gates
    //   PeriodLockTests (infra)      TC-30
    //   StatutoryObligationTests     TC-28 TC-29 TC-29b, and the full obligation state machine
    //   CasualEngagementAndAdvance   TC-20 TC-33
    //   PayslipTests / ReportTests   payslip reconciliation, zero vs unresolved, currency
    //                                separation in every report

    // ---- Still pending -----------------------------------------------------------------------

    /// <summary>
    /// TC-18: whether a part-timer's NSSA ceiling is pro-rated by hours is not established. The
    /// engine pro-rates by <em>period length</em> only; an FTE-based pro-ration would need a rule
    /// that does not exist yet.
    /// </summary>
    [Fact(Skip = "Pending compliance question Q4a/Q22: FTE-based ceiling treatment is unestablished.")]
    public void TC_18_Part_time_ceiling_treatment() { }

    // TC-20 and TC-33 are implemented in Tawaka.Infrastructure.Tests
    // (CasualEngagementAndAdvanceTests), where the timesheets and loans they depend on exist.
    // They needed Milestone 5's approved inputs before they could be run against the real engine.

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
