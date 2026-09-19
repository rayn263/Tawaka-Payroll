using Tawaka.Domain.Statutory;

namespace Tawaka.Payroll.Engine.Results;

/// <summary>
/// Why a figure could not be computed.
/// <para>
/// This exists to keep "unresolved" and "zero" apart. A missing or unverified statutory rule is
/// <b>not</b> a zero deduction: substituting 0.00 for an unavailable rule would produce a payslip
/// that looks complete and is wrong. An unresolved item leaves the figure null and says why.
/// </para>
/// </summary>
public sealed record UnresolvedItem(
    string Code,
    string ItemKey,
    string Message,
    StatutoryRuleType? RuleType = null,
    string? RuleId = null,
    VerificationStatus? VerificationStatus = null,
    string? ComplianceQuestion = null,
    string? Remedy = null)
{
    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>Codes the engine emits when it refuses to produce a figure.</summary>
public static class UnresolvedCodes
{
    public const string PayeTableUnresolved = "PAYE_TABLE_UNRESOLVED";
    public const string PayeFixedDeductionUnresolved = "PAYE_FIXED_DEDUCTION_UNRESOLVED";
    public const string AidsLevyRuleUnresolved = "AIDS_LEVY_RULE_UNRESOLVED";
    public const string NssaRuleUnresolved = "NSSA_RULE_UNRESOLVED";
    public const string NssaEligibilityUnresolved = "NSSA_ELIGIBILITY_UNRESOLVED";
    public const string NssaCeilingApplicationUnresolved = "NSSA_CEILING_APPLICATION_UNRESOLVED";
    public const string TaxCreditRuleUnresolved = "TAX_CREDIT_RULE_UNRESOLVED";
    public const string ExemptionRuleUnresolved = "EXEMPTION_RULE_UNRESOLVED";
    public const string CurrencyStrategyUnresolved = "CURRENCY_STRATEGY_UNRESOLVED";
    public const string ExchangeRateUnresolved = "EXCHANGE_RATE_UNRESOLVED";
    public const string ApwcsRuleUnresolved = "APWCS_RULE_UNRESOLVED";
    public const string EmployerLevyUnresolved = "EMPLOYER_LEVY_UNRESOLVED";
    public const string EarningTreatmentUnverified = "EARNING_TREATMENT_UNVERIFIED";
    public const string ContractUnresolved = "CONTRACT_UNRESOLVED";
    public const string RateMissing = "CONTRACT_RATE_MISSING";
    public const string OvertimeRuleUnresolved = "OVERTIME_RULE_UNRESOLVED";
    public const string OvertimeMultiplierUnresolved = "OVERTIME_MULTIPLIER_UNRESOLVED";
    public const string TimesheetMissing = "TIMESHEET_MISSING";
    public const string TimesheetNotApproved = "TIMESHEET_NOT_APPROVED";
    public const string LeaveEntitlementUnresolved = "LEAVE_ENTITLEMENT_UNRESOLVED";

    /// <summary>
    /// Deductions came to more than the employee earned. Not a missing rule — a recovery that has
    /// to be reduced or deferred before this payroll can be paid.
    /// </summary>
    public const string DeductionsExceedEarnings = "DEDUCTIONS_EXCEED_EARNINGS";

    /// <summary>Casual engagement approaching or past the Labour Act s.12(3) threshold.</summary>
    public const string CasualEngagementThreshold = "CASUAL_ENGAGEMENT_THRESHOLD";
}

/// <summary>A non-blocking observation about a calculation.</summary>
public sealed record CalculationWarning(string Code, string ItemKey, string Message);
