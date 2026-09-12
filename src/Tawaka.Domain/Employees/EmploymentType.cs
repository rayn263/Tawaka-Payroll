using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;

namespace Tawaka.Domain.Employees;

/// <summary>How an employee's basic pay is derived.</summary>
public enum EarningsBasis
{
    MonthlySalary = 0,
    WeeklySalary = 1,
    DailyRate = 2,
    HourlyRate = 3,
    ProjectRate = 4,
    ContractAmount = 5,
    Commission = 6
}

public enum PaymentFrequency
{
    Monthly = 0,
    Fortnightly = 1,
    Weekly = 2,
    Daily = 3,
    PerProject = 4,
    PerEngagement = 5
}

/// <summary>
/// An employment category. Statutory behaviour is <b>not</b> encoded here or in the UI: this record
/// carries payroll-processing defaults, while NSSA coverage is decided by
/// <see cref="NssaEligibilityRule"/> keyed on <see cref="Code"/>, and PAYE by the table matching
/// <see cref="DefaultPaymentFrequency"/>. Changing the law therefore means editing a rule, not a
/// type (ADR-003).
/// </summary>
public class EmploymentType : AuditableEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>Stable code linking to the NSSA eligibility rule, e.g. "Casual".</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public PaymentFrequency DefaultPaymentFrequency { get; set; } = PaymentFrequency.Monthly;

    public EarningsBasis DefaultEarningsBasis { get; set; } = EarningsBasis.MonthlySalary;

    public bool RequiresContractEndDate { get; set; }

    public bool RequiresTimesheet { get; set; }

    public bool AccruesLeave { get; set; }

    /// <summary>
    /// Days of engagement in a rolling four-month window after which Labour Act s.12(3) deems the
    /// worker to be on a contract without limit of time. Null where the rule does not apply.
    /// The system warns; it never reclassifies (ADR-014).
    /// </summary>
    public int? EngagementWarningDays { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
