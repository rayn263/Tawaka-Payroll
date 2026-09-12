using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;

namespace Tawaka.Domain.Earnings;

public enum DeductionCategory
{
    Statutory = 0,
    Loan = 1,
    Advance = 2,
    Benefit = 3,
    Union = 4,
    Garnishment = 5,
    Other = 6
}

/// <summary>
/// A kind of deduction. As with earnings, statutory treatment — in particular whether the
/// deduction reduces taxable income — is configuration carrying its own verification grade, not a
/// decision baked into the calculation code.
/// </summary>
public class DeductionType : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DeductionCategory Category { get; set; } = DeductionCategory.Other;

    /// <summary>An allowable deduction for PAYE purposes, such as the NSSA employee contribution.</summary>
    public bool ReducesTaxableIncome { get; set; }

    public bool AppliesBeforeTax { get; set; }

    public bool HasLimit { get; set; }

    public decimal? LimitAmount { get; set; }

    public string? LimitCurrency { get; set; }

    public VerificationStatus TreatmentVerificationStatus { get; set; } = VerificationStatus.Unverified;

    public string? TreatmentSource { get; set; }

    public string? TreatmentNotes { get; set; }

    /// <summary>Order of application where net pay is insufficient. Lower applies first.</summary>
    public int Priority { get; set; } = 100;

    public string? GlAccountCode { get; set; }

    public int DisplayOrder { get; set; }

    public bool ShowOnPayslipWhenZero { get; set; }

    public bool IsSystemType { get; set; }

    public bool IsActive { get; set; } = true;
}
