namespace Tawaka.Domain.Statutory;

/// <summary>AIDS Levy: a percentage of income tax, charged on the configured base.</summary>
public class AidsLevyRule : StatutoryRule
{
    public override StatutoryRuleType RuleType => StatutoryRuleType.AidsLevy;

    /// <summary>Fraction, e.g. 0.03 for 3%.</summary>
    public decimal Rate { get; set; }

    public AidsLevyBase Base { get; set; } = AidsLevyBase.TaxAfterCredits;
}

/// <summary>NSSA Pension and Other Benefits Scheme contribution rule.</summary>
public class NssaRule : StatutoryRule
{
    public override StatutoryRuleType RuleType => StatutoryRuleType.NssaPobs;

    public decimal EmployeeRate { get; set; }

    public decimal EmployerRate { get; set; }

    /// <summary>Insurable earnings ceiling, denominated in <see cref="StatutoryRule.Currency"/>.</summary>
    public decimal CeilingAmount { get; set; }

    /// <summary>The basis the ceiling is expressed in (the published ceiling is monthly).</summary>
    public PeriodBasis CeilingPeriodBasis { get; set; } = PeriodBasis.Monthly;

    /// <summary>How the ceiling applies to non-monthly payroll. Unresolved — see spec Q22.</summary>
    public CeilingApplicationMethod CeilingApplication { get; set; } =
        CeilingApplicationMethod.NotDetermined;

    public NssaEarningsBasis EarningsBasis { get; set; } = NssaEarningsBasis.BasicOnly;

    /// <summary>
    /// Whether the SI 393/93 s.12 gross-up applies, and at what multiple of basic pay it triggers.
    /// Sources give two different thresholds (1.0 and 2.0), so it ships disabled — spec Q4a.
    /// </summary>
    public bool GrossUpEnabled { get; set; }

    public decimal? GrossUpTriggerMultiplier { get; set; }

    public int MinimumAge { get; set; } = 16;

    public int? MaximumAge { get; set; } = 64;

    /// <summary>Minimum engaged days in a month before a casual worker contributes.</summary>
    public int? MinimumDaysInMonth { get; set; }
}

/// <summary>Whether a given employment type is covered by NSSA.</summary>
public class NssaEligibilityRule : StatutoryRule
{
    public override StatutoryRuleType RuleType => StatutoryRuleType.NssaEligibility;

    public string EmploymentTypeCode { get; set; } = string.Empty;

    public bool IsEligible { get; set; }

    public string? Condition { get; set; }
}

/// <summary>Accident Prevention and Workers Compensation Scheme — an employer-only cost.</summary>
public class ApwcsRule : StatutoryRule
{
    public override StatutoryRuleType RuleType => StatutoryRuleType.Apwcs;

    public string IndustryClassification { get; set; } = string.Empty;

    public string IndustryCode { get; set; } = string.Empty;

    public decimal Rate { get; set; }

    public ApwcsBase Base { get; set; } = ApwcsBase.BasicEarnings;

    public decimal? CeilingAmount { get; set; }
}

/// <summary>ZIMDEF, Standards Development Fund, NEC and similar levies.</summary>
public class EmployerLevyRule : StatutoryRule
{
    public override StatutoryRuleType RuleType => StatutoryRuleType.EmployerLevy;

    public EmployerLevyType LevyType { get; set; }

    public LevyBase Base { get; set; } = LevyBase.GrossWageBill;

    public decimal EmployerPortionRate { get; set; }

    /// <summary>Non-zero only where the levy is genuinely shared, such as some NEC levies.</summary>
    public decimal EmployeePortionRate { get; set; }

    /// <summary>Deadlines differ by authority (PAYE and NSSA the 10th, ZIMDEF the 15th).</summary>
    public int DueDayOfFollowingMonth { get; set; } = 10;
}

/// <summary>An employee tax credit.</summary>
public class TaxCreditRule : StatutoryRule
{
    public override StatutoryRuleType RuleType => StatutoryRuleType.TaxCredit;

    public TaxCreditType CreditType { get; set; }

    public decimal Amount { get; set; }

    public PeriodBasis AmountPeriodBasis { get; set; } = PeriodBasis.Monthly;

    /// <summary>For proportional credits such as medical. Sources conflict (50% or 100%) — spec Q24.</summary>
    public decimal? PercentageOfQualifyingAmount { get; set; }

    public decimal? AnnualCap { get; set; }
}

/// <summary>An exemption limit, such as the annual bonus exemption.</summary>
public class TaxExemptionRule : StatutoryRule
{
    public override StatutoryRuleType RuleType => StatutoryRuleType.TaxExemption;

    public TaxExemptionType ExemptionType { get; set; }

    public decimal LimitAmount { get; set; }

    public PeriodBasis LimitPeriodBasis { get; set; } = PeriodBasis.Annual;
}

/// <summary>
/// The methodology for taxing multi-currency remuneration. Requires explicit advisor sign-off
/// before it can be used, independently of its verification status.
/// </summary>
public class CurrencyTaxStrategyRule : StatutoryRule
{
    public override StatutoryRuleType RuleType => StatutoryRuleType.CurrencyTaxStrategy;

    public CurrencyTaxStrategy Strategy { get; set; } = CurrencyTaxStrategy.SingleCurrency;

    public string PrimaryCurrency { get; set; } = "USD";

    public Currencies.RateDeterminationRule RateDetermination { get; set; } =
        Currencies.RateDeterminationRule.NotDetermined;

    public Currencies.RateType RateType { get; set; } = Currencies.RateType.Interbank;

    public bool ApprovedByAdvisor { get; set; }

    public string? AdvisorReference { get; set; }
}
