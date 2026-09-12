using Tawaka.Application.Statutory;
using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Payroll.Engine.Tests;

public class LivePayrollGateTests
{
    private static readonly DateOnly PayDate = new(2026, 9, 30);

    private static IEnumerable<StatutoryRuleQuery> RequiredRules() => new[]
    {
        new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.PayeTable,
            EffectiveDate = PayDate,
            Currency = CurrencyCode.Usd,
            PeriodBasis = PeriodBasis.Monthly
        },
        new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.NssaPobs,
            EffectiveDate = PayDate,
            Currency = CurrencyCode.Usd
        },
        new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.Apwcs,
            EffectiveDate = PayDate,
            Currency = CurrencyCode.Usd
        }
    };

    [Fact]
    public void Gate_reports_every_blocking_rule_not_just_the_first()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Unverified),
            TestRules.Nssa(VerificationStatus.Supported));
        var gate = new LivePayrollGate(new StatutoryRuleResolver(source));

        var report = gate.Check(RequiredRules());

        Assert.True(report.IsBlocked);
        // PAYE unverified, NSSA unverified, APWCS missing entirely.
        Assert.Equal(3, report.BlockingRules.Count);
    }

    [Fact]
    public void Block_message_names_the_exact_rules()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Unverified),
            TestRules.Nssa(VerificationStatus.Verified),
            TestRules.Apwcs(VerificationStatus.Verified));
        var gate = new LivePayrollGate(new StatutoryRuleResolver(source));

        var rendered = gate.Check(RequiredRules()).Render();

        Assert.Contains("LIVE PAYROLL BLOCKED", rendered);
        Assert.Contains(
            "One or more statutory rules required for this payroll have not been verified.",
            rendered);
        Assert.Contains("PAYE-USD-2026-MONTHLY", rendered);
        Assert.Contains("To clear:", rendered);
    }

    [Fact]
    public void Gate_clears_when_every_required_rule_is_verified()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Verified),
            TestRules.Nssa(VerificationStatus.Verified),
            TestRules.Apwcs(VerificationStatus.Verified));
        var gate = new LivePayrollGate(new StatutoryRuleResolver(source));

        var report = gate.Check(RequiredRules());

        Assert.False(report.IsBlocked);
        Assert.Empty(report.BlockingRules);
    }

    /// <summary>
    /// A missing APWCS rate blocks rather than defaulting to an industry average: the rate is
    /// assigned to this employer by NSSA and cannot be inferred (spec Q6).
    /// </summary>
    [Fact]
    public void Missing_apwcs_rate_blocks_rather_than_defaulting()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Verified),
            TestRules.Nssa(VerificationStatus.Verified));
        var gate = new LivePayrollGate(new StatutoryRuleResolver(source));

        var report = gate.Check(RequiredRules());

        Assert.True(report.IsBlocked);
        Assert.Single(report.BlockingRules);
        Assert.Contains("Apwcs", report.BlockingRules[0].Description);
    }

    [Fact]
    public void Gate_always_evaluates_in_live_mode_even_if_the_query_says_otherwise()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Unverified));
        var gate = new LivePayrollGate(new StatutoryRuleResolver(source));

        var developmentQuery = new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.PayeTable,
            EffectiveDate = PayDate,
            Currency = CurrencyCode.Usd,
            PeriodBasis = PeriodBasis.Monthly,
            Mode = Domain.Payroll.PayrollMode.Development
        };

        Assert.True(gate.Check(new[] { developmentQuery }).IsBlocked);
    }
}
