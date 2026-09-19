using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Inputs;

namespace Tawaka.Payroll.Engine.Tests;

/// <summary>
/// The seed statutory rules from COMPLIANCE_SPEC.md, as the engine receives
/// them after resolution.
/// <para>
/// Graded Verified here <b>only so that engine behaviour can be tested</b>. In the running system
/// these same rules are Supported or Unverified and the live payroll gate blocks them. Verification
/// status and implementation status are separate concerns.
/// </para>
/// </summary>
public static class SeedRules
{
    public static TaxRule UsdMonthlyTable(
        VerificationStatus status = VerificationStatus.Verified,
        TaxBracketApplication application = TaxBracketApplication.ProgressiveLadder)
    {
        var rule = new TaxRule
        {
            RuleId = "PAYE-USD-2026-MONTHLY",
            Name = "PAYE USD monthly table 2026",
            Currency = "USD",
            TaxYear = 2026,
            PeriodBasis = PeriodBasis.Monthly,
            BracketApplication = application,
            CalculationMethod = CalculationMethod.PeriodTable,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveTo = new DateOnly(2026, 12, 31),
            VerificationStatus = status,
            Source = new RuleSource { Source = "Compliance specification §1.3 (seed)" }
        };

        AddBands(rule, (1, 0m, 100m, 0m), (2, 100m, 300m, 0.20m), (3, 300m, 3000m, 0.25m),
            (4, 3000m, null, 0.40m));
        return rule;
    }

    public static TaxRule ZwgAnnualTable(VerificationStatus status = VerificationStatus.Verified)
    {
        var rule = new TaxRule
        {
            RuleId = "PAYE-ZWG-2026-ANNUAL",
            Name = "PAYE ZiG annual table 2026",
            Currency = "ZWG",
            TaxYear = 2026,
            PeriodBasis = PeriodBasis.Annual,
            CalculationMethod = CalculationMethod.PeriodTable,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = status,
            Source = new RuleSource { Source = "Compliance specification §1.4 (seed)" }
        };

        AddBands(rule, (1, 0m, 33600m, 0m), (2, 33600m, 100800m, 0.20m),
            (3, 100800m, 1008000m, 0.25m), (4, 1008000m, null, 0.40m));
        return rule;
    }

    /// <summary>A ZiG monthly table derived for tests only — never derived by the engine.</summary>
    public static TaxRule ZwgMonthlyTable(VerificationStatus status = VerificationStatus.Verified)
    {
        var rule = new TaxRule
        {
            RuleId = "PAYE-ZWG-2026-MONTHLY",
            Name = "PAYE ZiG monthly table 2026",
            Currency = "ZWG",
            TaxYear = 2026,
            PeriodBasis = PeriodBasis.Monthly,
            CalculationMethod = CalculationMethod.PeriodTable,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = status,
            Source = new RuleSource { Source = "Test fixture" }
        };

        AddBands(rule, (1, 0m, 2800m, 0m), (2, 2800m, 8400m, 0.20m), (3, 8400m, 84000m, 0.25m),
            (4, 84000m, null, 0.40m));
        return rule;
    }

    public static AidsLevyRule AidsLevy(
        VerificationStatus status = VerificationStatus.Verified,
        AidsLevyBase basis = AidsLevyBase.TaxAfterCredits) => new()
        {
            RuleId = "AIDS-LEVY-2026",
            Name = "AIDS Levy 3% of tax after credits",
            Rate = 0.03m,
            Base = basis,
            CalculationMethod = CalculationMethod.PercentageOfBase,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = status,
            Source = new RuleSource { Source = "Compliance specification §4" }
        };

    public static NssaRule Nssa(
        CurrencyCode currency,
        VerificationStatus status = VerificationStatus.Verified,
        CeilingApplicationMethod ceilingApplication = CeilingApplicationMethod.NotDetermined,
        decimal ceiling = 700m) => new()
        {
            RuleId = "NSSA-POBS-2026",
            Name = "NSSA POBS 2026",
            Currency = currency.Value,
            EmployeeRate = 0.045m,
            EmployerRate = 0.045m,
            CeilingAmount = ceiling,
            CeilingPeriodBasis = PeriodBasis.Monthly,
            CeilingApplication = ceilingApplication,
            EarningsBasis = NssaEarningsBasis.BasicOnly,
            MinimumAge = 16,
            MaximumAge = 64,
            MinimumDaysInMonth = 18,
            CalculationMethod = CalculationMethod.CappedPercentage,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = status,
            Source = new RuleSource { Source = "Compliance specification §5" }
        };

    public static NssaEligibilityRule Eligibility(
        string employmentTypeCode, bool eligible = true,
        VerificationStatus status = VerificationStatus.Verified) => new()
        {
            RuleId = $"NSSA-ELIG-{employmentTypeCode.ToUpperInvariant()}-2026",
            Name = $"NSSA eligibility — {employmentTypeCode}",
            EmploymentTypeCode = employmentTypeCode,
            IsEligible = eligible,
            CalculationMethod = CalculationMethod.FlatAmount,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = status,
            Source = new RuleSource { Source = "Compliance specification §5.3" }
        };

    public static TaxCreditRule Credit(
        TaxCreditType type, decimal amount = 75m, string currency = "USD") => new()
        {
            RuleId = $"CREDIT-{type.ToString().ToUpperInvariant()}-2026",
            Name = $"{type} persons' credit 2026",
            Currency = currency,
            CreditType = type,
            Amount = amount,
            AmountPeriodBasis = PeriodBasis.Monthly,
            AnnualCap = amount * 12m,
            CalculationMethod = CalculationMethod.FlatAmount,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = VerificationStatus.Verified,
            Source = new RuleSource { Source = "Compliance specification §18" }
        };

    public static TaxExemptionRule BonusExemption(decimal limit = 700m, string currency = "USD") => new()
    {
        RuleId = "EXEMPT-BONUS-2026-USD",
        Name = "Annual bonus exemption 2026",
        Currency = currency,
        ExemptionType = TaxExemptionType.AnnualBonus,
        LimitAmount = limit,
        LimitPeriodBasis = PeriodBasis.Annual,
        CalculationMethod = CalculationMethod.FlatAmount,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        VerificationStatus = VerificationStatus.Verified,
        Source = new RuleSource { Source = "Finance Act No. 2 of 2024" }
    };

    public static ApwcsRule Apwcs(decimal rate = 0.025m, string currency = "USD") => new()
    {
        RuleId = "APWCS-CONSTRUCTION-2026",
        Name = "APWCS construction rate",
        Currency = currency,
        IndustryClassification = "Construction",
        IndustryCode = "CON",
        Rate = rate,
        Base = ApwcsBase.BasicEarnings,
        CalculationMethod = CalculationMethod.PercentageOfBase,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        VerificationStatus = VerificationStatus.Verified,
        Source = new RuleSource { Source = "NSSA assessment (test fixture)" }
    };

    /// <summary>The full rule set an ordinary monthly calculation needs.</summary>
    public static ResolvedRules For(
        CurrencyCode currency, PeriodBasis basis, string employmentTypeCode) => new()
        {
            PayeTable = currency == CurrencyCode.Usd
                ? UsdMonthlyTable()
                : basis == PeriodBasis.Annual ? ZwgAnnualTable() : ZwgMonthlyTable(),
            AidsLevy = AidsLevy(),
            Nssa = Nssa(currency, ceilingApplication: CeilingApplicationMethod.ProRataByPeriodLength),
            NssaEligibility = Eligibility(employmentTypeCode),
            TaxCredits = new[]
            {
                Credit(TaxCreditType.Elderly, currency: currency.Value),
                Credit(TaxCreditType.Disabled, currency: currency.Value),
                Credit(TaxCreditType.Blind, currency: currency.Value)
            },
            BonusExemption = BonusExemption(currency: currency.Value),
            Apwcs = Apwcs(currency: currency.Value)
        };

    private static void AddBands(
        TaxRule rule, params (int Sequence, decimal Lower, decimal? Upper, decimal Rate)[] bands)
    {
        foreach (var (sequence, lower, upper, rate) in bands)
        {
            rule.Brackets.Add(new TaxBracket
            {
                Sequence = sequence, LowerBound = lower, UpperBound = upper, Rate = rate
            });
        }
    }
}
