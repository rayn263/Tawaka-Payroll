using Tawaka.Application.Abstractions;
using Tawaka.Domain.Statutory;

namespace Tawaka.Payroll.Engine.Tests;

/// <summary>In-memory rule source so resolution logic is testable without a database.</summary>
public sealed class TestRuleSource : IStatutoryRuleSource
{
    private readonly List<StatutoryRule> _rules = new();

    public TestRuleSource Add(params StatutoryRule[] rules)
    {
        _rules.AddRange(rules);
        return this;
    }

    public IReadOnlyList<StatutoryRule> GetRules(StatutoryRuleType ruleType) =>
        _rules.Where(r => r.RuleType == ruleType).ToList();
}

public static class TestRules
{
    public static TaxRule PayeTable(
        string ruleId,
        string currency,
        PeriodBasis basis,
        VerificationStatus status,
        int year = 2026,
        DateOnly? from = null,
        DateOnly? to = null) => new()
        {
            RuleId = ruleId,
            Name = $"PAYE {currency} {basis} {year}",
            Currency = currency,
            TaxYear = year,
            PeriodBasis = basis,
            CalculationMethod = CalculationMethod.PeriodTable,
            EffectiveFrom = from ?? new DateOnly(year, 1, 1),
            EffectiveTo = to ?? new DateOnly(year, 12, 31),
            VerificationStatus = status,
            Source = new RuleSource { Source = "test" }
        };

    public static NssaRule Nssa(
        VerificationStatus status,
        CeilingApplicationMethod ceilingApplication = CeilingApplicationMethod.MonthlyAccumulation,
        string ruleId = "NSSA-TEST") => new()
        {
            RuleId = ruleId,
            Name = "NSSA POBS test",
            Currency = "USD",
            CalculationMethod = CalculationMethod.CappedPercentage,
            EmployeeRate = 0.045m,
            EmployerRate = 0.045m,
            CeilingAmount = 700m,
            CeilingPeriodBasis = PeriodBasis.Monthly,
            CeilingApplication = ceilingApplication,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = status,
            Source = new RuleSource { Source = "test" }
        };

    public static ApwcsRule Apwcs(VerificationStatus status, string industryCode = "CON") => new()
    {
        RuleId = "APWCS-TEST",
        Name = "APWCS test",
        Currency = "USD",
        CalculationMethod = CalculationMethod.PercentageOfBase,
        IndustryCode = industryCode,
        IndustryClassification = "Construction",
        Rate = 0.025m,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        VerificationStatus = status,
        Source = new RuleSource { Source = "test" }
    };

    public static CurrencyTaxStrategyRule Strategy(
        VerificationStatus status,
        bool approvedByAdvisor,
        CurrencyTaxStrategy strategy = CurrencyTaxStrategy.AggregateInPrimaryCurrency,
        Domain.Currencies.RateDeterminationRule rateRule =
            Domain.Currencies.RateDeterminationRule.PayDate) => new()
        {
            RuleId = "CURRENCY-STRATEGY-TEST",
            Name = "Multi-currency strategy test",
            CalculationMethod = CalculationMethod.Strategy,
            Strategy = strategy,
            PrimaryCurrency = "USD",
            RateDetermination = rateRule,
            ApprovedByAdvisor = approvedByAdvisor,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = status,
            Source = new RuleSource { Source = "test" }
        };
}
