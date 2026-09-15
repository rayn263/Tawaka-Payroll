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

/// <summary>
/// The multiplier that applies to one category of overtime, as a dated and graded rule.
/// <para>
/// Overtime rates in Zimbabwe come from the Labour Act and from NEC collective bargaining
/// agreements, which are Statutory Instruments. They are therefore statutory rules like any other:
/// versioned, dated, sourced and graded. Nothing in this codebase assumes 1.5× — an unverified or
/// missing overtime rule leaves the figure unresolved with its compliance question attached, the
/// same as a missing PAYE table.
/// </para>
/// </summary>
public class OvertimeRule : StatutoryRule
{
    public override StatutoryRuleType RuleType => StatutoryRuleType.Overtime;

    /// <summary>Stable category code, e.g. "OT_WEEKDAY", "OT_SUNDAY", "OT_PUBLIC_HOLIDAY".</summary>
    public string CategoryCode { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// Multiple of the ordinary hourly rate. Nullable on purpose: a rule may exist, and be known to
    /// exist, without its value having been read from an authoritative source. A null multiplier
    /// refuses; it does not mean 1.0.
    /// </summary>
    public decimal? Multiplier { get; set; }

    /// <summary>
    /// Hours per period beyond which this category applies, where the rule is threshold-based.
    /// Null where the category is defined by the day rather than by a threshold.
    /// </summary>
    public decimal? ThresholdHours { get; set; }

    /// <summary>True where the category is taxable and NSSA treatment follows ordinary overtime.</summary>
    public bool IsTaxable { get; set; } = true;

    public bool IsNssaApplicable { get; set; }

    /// <summary>The employment types this rate applies to, or null for all of them.</summary>
    public string? EmploymentTypeCode { get; set; }

    public bool HasUsableMultiplier => Multiplier is > 0m;
}

/// <summary>
/// How a periodic salary converts into an hourly or daily rate.
/// <para>
/// This looks like arithmetic and is not. Dividing a monthly salary by "the number of working days
/// in a month" requires a convention — 22 days? 26? 30? — and the answer changes what an employee
/// loses for a day of unpaid leave and gains for an hour of overtime. In Zimbabwe the convention
/// comes from the Labour Act and from NEC collective bargaining agreements, so it is a dated,
/// sourced, graded rule like any other, and an unverified one blocks live payroll rather than
/// quietly picking a divisor (Q33).
/// </para>
/// </summary>
public class PayDivisorRule : StatutoryRule
{
    public override StatutoryRuleType RuleType => StatutoryRuleType.PayDivisor;

    /// <summary>Which pay frequency this divisor converts from.</summary>
    public PeriodBasis SalaryBasis { get; set; } = PeriodBasis.Monthly;

    /// <summary>Working days in the period. Null means undetermined, not zero.</summary>
    public decimal? DaysInPeriod { get; set; }

    /// <summary>Ordinary working hours in the period. Null means undetermined, not zero.</summary>
    public decimal? HoursInPeriod { get; set; }

    /// <summary>The employment types this convention applies to, or null for all.</summary>
    public string? EmploymentTypeCode { get; set; }

    public bool HasDailyDivisor => DaysInPeriod is > 0m;

    public bool HasHourlyDivisor => HoursInPeriod is > 0m;
}
