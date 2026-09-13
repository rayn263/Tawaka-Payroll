using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Results;
using Tawaka.Payroll.Engine.Rounding;

namespace Tawaka.Payroll.Engine.Inputs;

/// <summary>One earning as it enters the calculation, with its statutory treatment attached.</summary>
public sealed record EarningInput
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required Money Amount { get; init; }

    /// <summary>Hours or days, where the amount was derived from a quantity.</summary>
    public decimal? Quantity { get; init; }

    public Money? Rate { get; init; }

    public bool IsTaxable { get; init; } = true;
    public bool IsNssaApplicable { get; init; }
    public bool IsIncludedInGross { get; init; } = true;
    public bool IsEmployerLevyBase { get; init; } = true;
    public bool IsExemptUpToLimit { get; init; }
    public bool IsReimbursive { get; init; }
    public bool IsBasic { get; init; }
    public bool IsOvertime { get; init; }
    public bool IsBonus { get; init; }

    /// <summary>The grade of this earning's statutory treatment, carried into the trace.</summary>
    public VerificationStatus TreatmentVerification { get; init; } = VerificationStatus.Unverified;

    public string? Source { get; init; }
}

/// <summary>One non-statutory deduction entering the calculation.</summary>
public sealed record DeductionInput
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required Money Amount { get; init; }
    public bool ReducesTaxableIncome { get; init; }
    public bool AppliesBeforeTax { get; init; }
    public int Priority { get; init; } = 100;
    public VerificationStatus TreatmentVerification { get; init; } = VerificationStatus.Unverified;
}

/// <summary>Attendance for the period, where the employment type requires it.</summary>
public sealed record TimesheetInput
{
    public decimal DaysWorked { get; init; }
    public decimal HoursWorked { get; init; }
    public decimal OvertimeHours { get; init; }
    public decimal SundayHours { get; init; }
    public decimal PublicHolidayHours { get; init; }
    public decimal DaysEngagedInMonth { get; init; }
    public bool IsApproved { get; init; }
}

/// <summary>The employee's statutory position, as at the pay date.</summary>
public sealed record StatutoryProfileInput
{
    public string? TaxNumber { get; init; }
    public string? NssaNumber { get; init; }
    public bool IsPayeExempt { get; init; }
    public bool? NssaEligibilityOverride { get; init; }
    public int? Age { get; init; }
    public bool IsElderlyCreditEligible { get; init; }
    public bool IsDisabledCreditEligible { get; init; }
    public bool IsBlindCreditEligible { get; init; }
    public bool MedicalAidCreditApplies { get; init; }
    public bool IsNecMember { get; init; }
}

/// <summary>How a project or department shares this employee's cost.</summary>
public sealed record CostAllocationInput(
    Guid? ProjectId, string? ProjectName, Guid? ProjectSiteId, Guid? DepartmentId,
    string? DepartmentName, decimal Percent);

/// <summary>The statutory rules resolved for this calculation, plus anything that failed to resolve.</summary>
public sealed record ResolvedRules
{
    public TaxRule? PayeTable { get; init; }
    public AidsLevyRule? AidsLevy { get; init; }
    public NssaRule? Nssa { get; init; }
    public NssaEligibilityRule? NssaEligibility { get; init; }
    public IReadOnlyList<TaxCreditRule> TaxCredits { get; init; } = Array.Empty<TaxCreditRule>();
    public TaxExemptionRule? BonusExemption { get; init; }
    public ApwcsRule? Apwcs { get; init; }
    public IReadOnlyList<EmployerLevyRule> EmployerLevies { get; init; } = Array.Empty<EmployerLevyRule>();
    public CurrencyTaxStrategyRule? CurrencyStrategy { get; init; }

    /// <summary>Rules that were required but could not be resolved, with the reason.</summary>
    public IReadOnlyList<UnresolvedItem> Unresolved { get; init; } = Array.Empty<UnresolvedItem>();

    public UnresolvedItem? FindUnresolved(StatutoryRuleType ruleType) =>
        Unresolved.FirstOrDefault(u => u.RuleType == ruleType);
}

/// <summary>
/// Everything the engine needs to calculate one employee's pay for one period, and nothing else.
/// <para>
/// The snapshot is immutable and self-contained: it holds the resolved contract, earnings,
/// deductions, timesheet, statutory profile, the resolved rule versions and any exchange rates.
/// Because the engine reads nothing but this, the same snapshot always produces the same result —
/// which is what makes a payroll reproducible years later and a failure diagnosable at all.
/// </para>
/// </summary>
public sealed record PayrollInputSnapshot
{
    public required Guid CompanyId { get; init; }
    public required Guid EmployeeId { get; init; }
    public required string EmployeeNumber { get; init; }
    public required string EmployeeName { get; init; }

    public required Guid PayrollPeriodId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required DateOnly PayDate { get; init; }
    public required PeriodBasis PeriodBasis { get; init; }

    public required PayrollMode Mode { get; init; }

    /// <summary>The contract version that applied on the pay date — not necessarily the current one.</summary>
    public required Guid ContractId { get; init; }
    public required int ContractVersion { get; init; }
    public required CurrencyCode PayrollCurrency { get; init; }
    public required EarningsBasis EarningsBasis { get; init; }
    public required PaymentFrequency PaymentFrequency { get; init; }
    public required string EmploymentTypeCode { get; init; }

    public Money? ContractRate { get; init; }
    public decimal StandardHoursPerDay { get; init; } = 8m;
    public decimal StandardDaysPerWeek { get; init; } = 5m;

    public IReadOnlyList<EarningInput> Earnings { get; init; } = Array.Empty<EarningInput>();
    public IReadOnlyList<DeductionInput> Deductions { get; init; } = Array.Empty<DeductionInput>();
    public TimesheetInput? Timesheet { get; init; }
    public StatutoryProfileInput StatutoryProfile { get; init; } = new();
    public IReadOnlyList<CostAllocationInput> CostAllocations { get; init; } =
        Array.Empty<CostAllocationInput>();

    public required ResolvedRules Rules { get; init; }

    /// <summary>Exchange rates frozen into this calculation, keyed "FROM>TO".</summary>
    public IReadOnlyDictionary<string, Currencies.ConversionRecord> ExchangeRates { get; init; } =
        new Dictionary<string, Currencies.ConversionRecord>();

    /// <summary>Bonus exemption already used this tax year, so the limit cannot be claimed twice.</summary>
    public Money? BonusExemptionUsedYearToDate { get; init; }

    public RoundingPolicy RoundingPolicy { get; init; } = RoundingPolicy.Default;

    /// <summary>The currencies present in this employee's earnings.</summary>
    public IReadOnlyList<CurrencyCode> EarningCurrencies =>
        Earnings.Select(e => e.Amount.Currency).Distinct().ToList();

    public bool IsMultiCurrency => EarningCurrencies.Count > 1;
}
