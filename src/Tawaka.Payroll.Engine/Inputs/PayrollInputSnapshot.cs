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

/// <summary>
/// Hours claimed in one overtime category, with the dated rule that prices them.
/// <para>
/// The multiplier travels with the hours. It is nullable because an overtime rule can be known to
/// exist without its rate having been read from an authoritative source, and a null multiplier
/// must refuse rather than default to 1.0 — the same discipline as every other statutory figure.
/// </para>
/// </summary>
public sealed record OvertimeInput
{
    public required string CategoryCode { get; init; }
    public required string CategoryName { get; init; }
    public required decimal Hours { get; init; }

    public decimal? Multiplier { get; init; }
    public string? RuleId { get; init; }
    public VerificationStatus RuleVerification { get; init; } = VerificationStatus.Unverified;

    public bool IsTaxable { get; init; } = true;
    public bool IsNssaApplicable { get; init; }
    public string? Source { get; init; }
}

/// <summary>
/// One approved absence falling in this period, and whether it is paid.
/// <para>
/// Leave reaches payroll as days and a paid/unpaid flag, never as an amount. What an unpaid day
/// costs is arithmetic, and arithmetic happens in the engine.
/// </para>
/// </summary>
public sealed record LeaveEffectInput
{
    public required Guid LeaveRequestId { get; init; }
    public required string LeaveTypeCode { get; init; }
    public required string LeaveTypeName { get; init; }
    public required decimal Days { get; init; }
    public required bool IsPaid { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
}

/// <summary>
/// One approved loan instalment to recover from this payroll.
/// <para>
/// The amount is decided by the loan's schedule and capped by its outstanding balance before it
/// ever reaches here. The snapshot carries the balance it was capped against so the deduction
/// stays explicable years later.
/// </para>
/// </summary>
public sealed record LoanDeductionInput
{
    public required Guid LoanId { get; init; }
    public required string LoanNumber { get; init; }
    public required string LoanTypeCode { get; init; }
    public required Money Amount { get; init; }
    public Guid? InstalmentId { get; init; }
    public int? InstalmentNumber { get; init; }
    public required Money OutstandingBefore { get; init; }
    public bool WasCappedAtOutstanding { get; init; }
    public bool OverRecoveryApproved { get; init; }
}

/// <summary>
/// The working calendar this calculation used, frozen into the snapshot.
/// <para>
/// A payroll that priced a public holiday must be able to say which calendar told it that the day
/// was a holiday. Recording the calendar identity and the dates it contributed means a later edit
/// to the calendar cannot change what a completed run believed.
/// </para>
/// </summary>
public sealed record HolidayCalendarInput
{
    public required Guid CalendarId { get; init; }
    public required string CalendarCode { get; init; }
    public required string CalendarName { get; init; }
    public IReadOnlyList<DateOnly> HolidayDatesInPeriod { get; init; } = Array.Empty<DateOnly>();
}

/// <summary>
/// A pointer to one approved input this calculation consumed.
/// <para>
/// This is what makes "which approved records contributed to this run" answerable exactly, rather
/// than by re-running the same query later and hoping the data has not moved.
/// </para>
/// </summary>
public sealed record ApprovedInputReference(
    string InputType, Guid InputId, string Description,
    string? ApprovedBy, DateTimeOffset? ApprovedAt);

/// <summary>Attendance for the period, where the employment type requires it.</summary>
public sealed record TimesheetInput
{
    /// <summary>The approved timesheet this came from, so the run can name its source.</summary>
    public Guid? TimesheetId { get; init; }

    public decimal DaysWorked { get; init; }
    public decimal HoursWorked { get; init; }
    public decimal OvertimeHours { get; init; }
    public decimal SundayHours { get; init; }
    public decimal PublicHolidayHours { get; init; }
    public decimal DaysEngagedInMonth { get; init; }
    public bool IsApproved { get; init; }

    /// <summary>Overtime kept apart by category, each with its own rule and multiplier.</summary>
    public IReadOnlyList<OvertimeInput> Overtime { get; init; } = Array.Empty<OvertimeInput>();

    /// <summary>The individual time entries that produced these totals.</summary>
    public IReadOnlyList<Guid> TimeEntryIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Hours and days per project, for cost allocation that follows actual work.</summary>
    public IReadOnlyList<TimeAllocationInput> Allocations { get; init; } =
        Array.Empty<TimeAllocationInput>();
}

/// <summary>Time booked to one project or site within the period.</summary>
public sealed record TimeAllocationInput(
    Guid? ProjectId, string? ProjectName, Guid? ProjectSiteId, string? ProjectSiteName,
    decimal Hours, decimal Days);

/// <summary>
/// How long a casual worker has actually been engaged, over a rolling window.
/// <para>
/// This exists so a human notices. Labour Act s.12(3) deems a worker engaged beyond the threshold
/// to be on a contract without limit of time; the deeming happens whether or not the software sees
/// it. The software's job is to warn — never to reclassify, which would be the system making a
/// legal determination it has no standing to make (ADR-014, spec §11).
/// </para>
/// </summary>
public sealed record CasualEngagementInput
{
    public required int DaysEngagedInWindow { get; init; }
    public required DateOnly WindowStart { get; init; }
    public required DateOnly WindowEnd { get; init; }

    /// <summary>Days at which the deeming applies, from the employment type's configuration.</summary>
    public required int DeemedAtDays { get; init; }

    /// <summary>
    /// Warn before the line is crossed, so the business can decide rather than discover. Five
    /// sixths of the threshold, matching the specification's "warn at 5 weeks of 6".
    /// </summary>
    public int WarnAtDays => (int)Math.Floor(DeemedAtDays * 5m / 6m);

    public bool IsApproaching => DaysEngagedInWindow >= WarnAtDays;

    public bool HasPassed => DaysEngagedInWindow >= DeemedAtDays;

    public string Describe()
    {
        var weeks = DaysEngagedInWindow / 6;
        var days = DaysEngagedInWindow % 6;
        return days == 0 ? $"{weeks} week(s)" : $"{weeks} week(s) {days} day(s)";
    }
}

/// <summary>
/// An input that existed but was not consumed, and why. Usually: it is not approved.
/// </summary>
public sealed record SkippedInput(string InputType, Guid InputId, string Reason);

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
    string? DepartmentName, decimal Percent, string? ProjectSiteName = null);

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

    /// <summary>The overtime rules resolved for the categories this employee actually claimed.</summary>
    public IReadOnlyList<OvertimeRule> Overtime { get; init; } = Array.Empty<OvertimeRule>();

    /// <summary>How this employee's salary converts to a daily and hourly rate (Q33).</summary>
    public PayDivisorRule? PayDivisor { get; init; }

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

    /// <summary>Approved absences falling in this period.</summary>
    public IReadOnlyList<LeaveEffectInput> LeaveEffects { get; init; } =
        Array.Empty<LeaveEffectInput>();

    /// <summary>Approved loan and advance recoveries for this period.</summary>
    public IReadOnlyList<LoanDeductionInput> LoanDeductions { get; init; } =
        Array.Empty<LoanDeductionInput>();

    /// <summary>The working calendar this calculation used, where one applied.</summary>
    public HolidayCalendarInput? HolidayCalendar { get; init; }

    /// <summary>
    /// The casual engagement tally, where the employment type has a threshold. Null where the
    /// question does not arise.
    /// </summary>
    public CasualEngagementInput? CasualEngagement { get; init; }

    /// <summary>
    /// Every approved input this calculation consumed, by type and identity. Recorded so a run can
    /// state exactly which approved records it read, rather than leaving it to be inferred.
    /// </summary>
    public IReadOnlyList<ApprovedInputReference> ApprovedInputs { get; init; } =
        Array.Empty<ApprovedInputReference>();

    /// <summary>
    /// Inputs that exist for this employee and period but were not consumed, with the reason —
    /// almost always that they are not approved. Carried so the preview can say "this employee has
    /// an unapproved timesheet" instead of silently paying them nothing.
    /// </summary>
    public IReadOnlyList<SkippedInput> SkippedInputs { get; init; } = Array.Empty<SkippedInput>();
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
