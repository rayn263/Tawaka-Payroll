using Tawaka.Application.Statutory;
using Tawaka.Domain.Common;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Payroll.Engine.Tests;

/// <summary>
/// Behaviour tests for rule resolution. None of these depend on a statutory rate, so they remain
/// valid when verified 2026 rules replace the seed data.
/// </summary>
public class StatutoryRuleResolverTests
{
    private static readonly DateOnly PayDate = new(2026, 9, 30);

    private static StatutoryRuleQuery PayeQuery(
        PayrollMode mode = PayrollMode.Live,
        PeriodBasis basis = PeriodBasis.Monthly,
        string currency = "USD") => new()
        {
            RuleType = StatutoryRuleType.PayeTable,
            EffectiveDate = PayDate,
            Currency = new CurrencyCode(currency),
            PeriodBasis = basis,
            Mode = mode
        };

    [Fact]
    public void Verified_rule_resolves_in_live_mode()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Verified));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<TaxRule>(PayeQuery());

        Assert.True(resolution.Succeeded);
        Assert.Equal("PAYE-USD-2026-MONTHLY", resolution.Rule!.RuleId);
    }

    [Theory]
    [InlineData(VerificationStatus.Unverified)]
    [InlineData(VerificationStatus.Supported)]
    public void Unverified_rule_is_refused_in_live_mode(VerificationStatus status)
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly, status));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<TaxRule>(PayeQuery());

        Assert.False(resolution.Succeeded);
        Assert.Equal(RuleResolutionFailureReason.NotVerifiedForLiveMode, resolution.Failure!.Reason);
        Assert.Equal(
            "This statutory rule has not been verified and cannot be used for live payroll.",
            resolution.Failure.Message);
        Assert.Equal("PAYE-USD-2026-MONTHLY", resolution.Failure.RuleId);
        Assert.NotNull(resolution.Failure.Remedy);
    }

    [Theory]
    [InlineData(VerificationStatus.Unverified)]
    [InlineData(VerificationStatus.Supported)]
    public void Unverified_rule_resolves_in_development_mode(VerificationStatus status)
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly, status));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<TaxRule>(PayeQuery(PayrollMode.Development));

        Assert.True(resolution.Succeeded);
    }

    [Fact]
    public void Disabled_rule_is_refused_in_every_mode()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Disabled));
        var resolver = new StatutoryRuleResolver(source);

        Assert.Equal(RuleResolutionFailureReason.Disabled,
            resolver.Resolve<TaxRule>(PayeQuery(PayrollMode.Development)).Failure!.Reason);
        Assert.Equal(RuleResolutionFailureReason.Disabled,
            resolver.Resolve<TaxRule>(PayeQuery()).Failure!.Reason);
    }

    /// <summary>
    /// The central guarantee: an unverified rule is never substituted for a missing verified one,
    /// and the resolver never silently reaches back to an older verified version.
    /// </summary>
    [Fact]
    public void Does_not_fall_back_to_a_verified_rule_from_another_period()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2025-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Verified, 2025),
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Unverified, 2026));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<TaxRule>(PayeQuery());

        Assert.False(resolution.Succeeded);
        Assert.Equal("PAYE-USD-2026-MONTHLY", resolution.Failure!.RuleId);
    }

    [Fact]
    public void Does_not_substitute_another_currency()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Verified));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<TaxRule>(PayeQuery(currency: "ZWG"));

        Assert.False(resolution.Succeeded);
        Assert.Equal(RuleResolutionFailureReason.NotFound, resolution.Failure!.Reason);
    }

    /// <summary>TC-34: a weekly table is never derived from the monthly one (ADR-013).</summary>
    [Fact]
    public void Does_not_derive_a_weekly_table_from_the_monthly_table()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Verified));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<TaxRule>(PayeQuery(basis: PeriodBasis.Weekly));

        Assert.False(resolution.Succeeded);
        Assert.Equal(RuleResolutionFailureReason.NotFound, resolution.Failure!.Reason);
        Assert.Contains("never derived", resolution.Failure.Remedy!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>TC-26: historical payroll keeps the rule that applied at the time.</summary>
    [Fact]
    public void Resolves_the_rule_effective_on_the_pay_date_not_the_latest()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-USD-2026-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Verified, 2026),
            TestRules.PayeTable("PAYE-USD-2027-MONTHLY", "USD", PeriodBasis.Monthly,
                VerificationStatus.Verified, 2027));
        var resolver = new StatutoryRuleResolver(source);

        var september2026 = resolver.Resolve<TaxRule>(PayeQuery());
        var september2027 = resolver.Resolve<TaxRule>(PayeQuery() with
        {
            EffectiveDate = new DateOnly(2027, 9, 30)
        });

        Assert.Equal("PAYE-USD-2026-MONTHLY", september2026.Rule!.RuleId);
        Assert.Equal("PAYE-USD-2027-MONTHLY", september2027.Rule!.RuleId);
    }

    [Fact]
    public void Overlapping_active_rules_are_reported_rather_than_picked_between()
    {
        var source = new TestRuleSource().Add(
            TestRules.PayeTable("PAYE-A", "USD", PeriodBasis.Monthly, VerificationStatus.Verified),
            TestRules.PayeTable("PAYE-B", "USD", PeriodBasis.Monthly, VerificationStatus.Verified,
                from: new DateOnly(2026, 6, 1), to: null));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<TaxRule>(PayeQuery());

        Assert.False(resolution.Succeeded);
        Assert.Equal(RuleResolutionFailureReason.Ambiguous, resolution.Failure!.Reason);
        Assert.Contains("PAYE-A", resolution.Failure.Message);
        Assert.Contains("PAYE-B", resolution.Failure.Message);
    }

    [Fact]
    public void Missing_rule_reports_not_found_with_a_remedy()
    {
        var resolver = new StatutoryRuleResolver(new TestRuleSource());

        var resolution = resolver.Resolve<ApwcsRule>(new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.Apwcs,
            EffectiveDate = PayDate,
            Currency = CurrencyCode.Usd
        });

        Assert.False(resolution.Succeeded);
        Assert.Equal(RuleResolutionFailureReason.NotFound, resolution.Failure!.Reason);
        Assert.NotNull(resolution.Failure.Remedy);
    }

    /// <summary>
    /// A verified NSSA ceiling with no rule for applying it to non-monthly payroll is still not
    /// usable — spec Q22, where the candidate methods differ roughly fourfold.
    /// </summary>
    [Fact]
    public void Verified_nssa_rule_with_undetermined_ceiling_application_is_still_blocked()
    {
        var source = new TestRuleSource().Add(
            TestRules.Nssa(VerificationStatus.Verified, CeilingApplicationMethod.NotDetermined));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<NssaRule>(new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.NssaPobs,
            EffectiveDate = PayDate,
            Currency = CurrencyCode.Usd
        });

        Assert.False(resolution.Succeeded);
        Assert.Equal(RuleResolutionFailureReason.IncompleteConfiguration, resolution.Failure!.Reason);
        Assert.Contains("Q22", resolution.Failure.Remedy!);
    }

    /// <summary>Multi-currency tax cannot run on advisor sign-off alone being absent — spec Q1.</summary>
    [Fact]
    public void Multi_currency_strategy_without_advisor_approval_is_blocked()
    {
        var source = new TestRuleSource().Add(
            TestRules.Strategy(VerificationStatus.Verified, approvedByAdvisor: false));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<CurrencyTaxStrategyRule>(new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.CurrencyTaxStrategy,
            EffectiveDate = PayDate
        });

        Assert.False(resolution.Succeeded);
        Assert.Equal(RuleResolutionFailureReason.IncompleteConfiguration, resolution.Failure!.Reason);
        Assert.Contains("Q1", resolution.Failure.Remedy!);
    }

    [Fact]
    public void Single_currency_strategy_needs_no_advisor_approval()
    {
        var source = new TestRuleSource().Add(
            TestRules.Strategy(VerificationStatus.Verified, approvedByAdvisor: false,
                strategy: CurrencyTaxStrategy.SingleCurrency));
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<CurrencyTaxStrategyRule>(new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.CurrencyTaxStrategy,
            EffectiveDate = PayDate
        });

        Assert.True(resolution.Succeeded);
    }

    [Fact]
    public void Employee_circumstances_select_the_matching_eligibility_rule()
    {
        var source = new TestRuleSource().Add(
            new NssaEligibilityRule
            {
                RuleId = "NSSA-ELIG-CASUAL",
                Name = "Casual",
                EmploymentTypeCode = "Casual",
                IsEligible = true,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                VerificationStatus = VerificationStatus.Verified,
                Source = new RuleSource { Source = "test" }
            },
            new NssaEligibilityRule
            {
                RuleId = "NSSA-ELIG-DOMESTIC",
                Name = "Domestic",
                EmploymentTypeCode = "Domestic",
                IsEligible = false,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                VerificationStatus = VerificationStatus.Verified,
                Source = new RuleSource { Source = "test" }
            });
        var resolver = new StatutoryRuleResolver(source);

        var resolution = resolver.Resolve<NssaEligibilityRule>(new StatutoryRuleQuery
        {
            RuleType = StatutoryRuleType.NssaEligibility,
            EffectiveDate = PayDate,
            Employee = new EmployeeRuleContext { EmploymentTypeCode = "Domestic" }
        });

        Assert.True(resolution.Succeeded);
        Assert.False(resolution.Rule!.IsEligible);
    }
}
