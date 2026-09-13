namespace Tawaka.Domain.Statutory;

/// <summary>
/// How well established a statutory rule is. The engine refuses to use anything below
/// <see cref="Verified"/> for live payroll (ADR-012).
/// </summary>
public enum VerificationStatus
{
    /// <summary>Insufficient evidence, or sources conflict. Blocks live payroll.</summary>
    Unverified = 0,

    /// <summary>Credible sources agree but the official document has not been read. Blocks live payroll.</summary>
    Supported = 1,

    /// <summary>Confirmed against the official source, which is recorded on the rule.</summary>
    Verified = 2,

    /// <summary>Deliberately switched off; never resolved for any purpose.</summary>
    Disabled = 3
}

public static class VerificationStatusExtensions
{
    /// <summary>Only a Verified rule may be used to produce a live payroll.</summary>
    public static bool IsUsableInLivePayroll(this VerificationStatus status) =>
        status == VerificationStatus.Verified;

    public static bool IsUsableInDevelopment(this VerificationStatus status) =>
        status != VerificationStatus.Disabled;
}

public enum StatutoryRuleType
{
    PayeTable = 0,
    AidsLevy = 1,
    NssaPobs = 2,
    NssaEligibility = 3,
    Apwcs = 4,
    EmployerLevy = 5,
    TaxCredit = 6,
    TaxExemption = 7,
    CurrencyTaxStrategy = 8
}

/// <summary>
/// Pay-period basis of a rule. ZIMRA publishes a table for each; the engine selects strictly on
/// the employee's payment frequency and never derives one basis from another (ADR-013).
/// </summary>
public enum PeriodBasis
{
    Daily = 0,
    Weekly = 1,
    Fortnightly = 2,
    Monthly = 3,
    Annual = 4
}

public enum CalculationMethod
{
    /// <summary>Progressive table applied to the period's earnings.</summary>
    PeriodTable = 0,

    /// <summary>Cumulative annual-equivalent reconciliation (Final Deduction System).</summary>
    CumulativeAnnual = 1,

    PercentageOfBase = 2,
    CappedPercentage = 3,
    FlatAmount = 4,
    Strategy = 5
}

/// <summary>
/// The form a PAYE table is expressed in. ZIMRA publishes tables as
/// "income x rate less a fixed deduction"; the bands themselves also describe a progressive
/// ladder. Both give the same answer when the deduction column is correct, and the published form
/// is preferred so results reconcile with the authority's own figures (ADR-013).
/// </summary>
public enum TaxBracketApplication
{
    /// <summary>Each band applied to its own slice of income, then summed.</summary>
    ProgressiveLadder = 0,

    /// <summary>Income x the band's rate, less the band's published fixed deduction.</summary>
    RateLessFixedDeduction = 1
}

/// <summary>Whether the AIDS Levy is charged on tax before or after credits.</summary>
public enum AidsLevyBase
{
    TaxAfterCredits = 0,
    TaxBeforeCredits = 1
}

/// <summary>What NSSA counts as insurable earnings.</summary>
public enum NssaEarningsBasis
{
    BasicOnly = 0,
    BasicPlusRegularAllowances = 1
}

/// <summary>
/// How a monthly insurable-earnings ceiling is applied to non-monthly payroll. Unresolved
/// (compliance spec Q22), so the default deliberately blocks rather than guessing.
/// </summary>
public enum CeilingApplicationMethod
{
    NotDetermined = 0,
    ProRataByPeriodLength = 1,
    MonthlyAccumulation = 2,
    PerPayRun = 3
}

public enum ApwcsBase
{
    BasicEarnings = 0,
    GrossEarnings = 1
}

public enum EmployerLevyType
{
    Zimdef = 0,
    StandardsDevelopmentFund = 1,
    Nec = 2,
    Other = 3
}

public enum LevyBase
{
    GrossWageBill = 0,
    LeviableWageBill = 1,
    BasicEarnings = 2
}

public enum TaxCreditType
{
    Elderly = 0,
    Blind = 1,
    Disabled = 2,
    MedicalAidContribution = 3,
    MedicalExpenseShortfall = 4
}

public enum TaxExemptionType
{
    AnnualBonus = 0,
    RetrenchmentPackage = 1,
    Other = 2
}

/// <summary>
/// Methodology for taxing remuneration paid in more than one currency. Never inferred by the
/// engine: the strategy is a dated rule requiring advisor sign-off (compliance spec §2).
/// </summary>
public enum CurrencyTaxStrategy
{
    SingleCurrency = 0,
    AggregateInPrimaryCurrency = 1,
    SeparatePerCurrency = 2
}
