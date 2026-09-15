using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;

namespace Tawaka.Domain.Earnings;

public enum EarningCategory
{
    Basic = 0,
    Overtime = 1,
    Allowance = 2,
    Bonus = 3,
    Commission = 4,
    BenefitInKind = 5,
    Other = 6
}

public enum EarningCalculationBasis
{
    FixedAmount = 0,
    PercentOfBasic = 1,
    RatePerUnit = 2,
    Manual = 3
}

/// <summary>How a benefit in kind is valued. Automation waits on verified tables.</summary>
public enum BenefitValuationMethod
{
    NotApplicable = 0,
    DeemedTable = 1,
    OpenMarketValue = 2,
    InterestDifferential = 3,
    Manual = 4
}

/// <summary>
/// A kind of earning, with its statutory treatment held as configuration.
/// <para>
/// Whether an allowance is taxable or NSSA-applicable is <b>never</b> decided in code or in the
/// user interface. It is set here, and each treatment carries its own
/// <see cref="TreatmentVerificationStatus"/> and source, so an earning type whose treatment is
/// merely assumed blocks live payroll in the same way an unverified tax table does.
/// </para>
/// </summary>
public class EarningType : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public EarningCategory Category { get; set; } = EarningCategory.Allowance;

    // ---- Statutory treatment -------------------------------------------------------------

    public bool IsTaxable { get; set; } = true;

    public bool IsNssaApplicable { get; set; }

    public bool IsIncludedInGross { get; set; } = true;

    public bool IsPensionable { get; set; }

    /// <summary>Counts towards the ZIMDEF/SDF leviable wage bill.</summary>
    public bool IsEmployerLevyBase { get; set; } = true;

    /// <summary>Exempt up to a configured limit, e.g. the annual bonus exemption.</summary>
    public bool IsExemptUpToLimit { get; set; }

    public Guid? ExemptionRuleId { get; set; }

    /// <summary>
    /// A reimbursement of a vouched business expense, which is exempt where proof is held. The
    /// test is evidence-dependent and per transaction, so the system prompts rather than assumes.
    /// </summary>
    public bool IsReimbursive { get; set; }

    public bool RequiresExpenseProof { get; set; }

    public bool IsBenefitInKind { get; set; }

    public BenefitValuationMethod ValuationMethod { get; set; } = BenefitValuationMethod.NotApplicable;

    /// <summary>
    /// Confidence in the statutory treatment above. Unverified treatments block live payroll.
    /// </summary>
    public VerificationStatus TreatmentVerificationStatus { get; set; } = VerificationStatus.Unverified;

    public string? TreatmentSource { get; set; }

    public string? TreatmentNotes { get; set; }

    // ---- Payroll behaviour ---------------------------------------------------------------

    public EarningCalculationBasis DefaultCalculationBasis { get; set; } = EarningCalculationBasis.FixedAmount;

    /// <summary>
    /// A capture default only, e.g. 1.5 for overtime.
    /// <para>
    /// <b>The payroll engine does not read this.</b> Overtime is priced from dated, graded
    /// <c>OvertimeRules</c> (ADR-031), because a multiplier needs an effective date, a source, a
    /// verification status and a category, and a nullable column here carries none of those. The
    /// column is retained rather than dropped — removing an established column is not done
    /// casually — but nothing should treat it as authoritative.
    /// </para>
    /// </summary>
    public decimal? DefaultMultiplier { get; set; }

    public string? GlAccountCode { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>Statutory categories stay visible on a payslip even at zero.</summary>
    public bool ShowOnPayslipWhenZero { get; set; }

    /// <summary>System types underpin core payroll and cannot be deleted.</summary>
    public bool IsSystemType { get; set; }

    public bool IsActive { get; set; } = true;
}
