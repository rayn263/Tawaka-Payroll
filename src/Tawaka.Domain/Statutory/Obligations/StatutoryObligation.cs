using Tawaka.Domain.Common;

namespace Tawaka.Domain.Statutory.Obligations;

/// <summary>The authority an obligation is owed to.</summary>
public enum StatutoryAuthority
{
    Zimra = 0,
    Nssa = 1,
    Zimdef = 2,
    StandardsDevelopmentFund = 3,
    Nec = 4
}

/// <summary>What is owed.</summary>
public enum StatutoryObligationType
{
    Paye = 0,
    AidsLevy = 1,
    NssaPobsEmployee = 2,
    NssaPobsEmployer = 3,
    Apwcs = 4,
    Zimdef = 5,
    StandardsDevelopmentFund = 6,
    NecEmployee = 7,
    NecEmployer = 8
}

/// <summary>
/// The status shown to a user. Derived from the four independent state flags — never stored as a
/// settable field, because a status that can be set by hand is a status that can lie.
/// </summary>
public enum StatutoryObligationStatus
{
    Calculated = 0,
    Deducted = 1,
    Approved = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Overdue = 5
}

/// <summary>
/// An amount owed to an authority for one payroll run, in one currency.
/// <para>
/// The four states are independent, each with its own actor and timestamp, because a business
/// routinely sits at "calculated, deducted, approved, but not yet paid" and a single status field
/// cannot express that. <see cref="IsPaid"/> is derived from actual payment records: there is no
/// code path anywhere that marks an obligation paid without one.
/// </para>
/// </summary>
public class StatutoryObligation : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public Guid PayrollRunId { get; set; }

    public Guid PayrollPeriodId { get; set; }

    public StatutoryAuthority Authority { get; set; }

    public StatutoryObligationType ObligationType { get; set; }

    /// <summary>ISO code. An obligation is settled in its own currency and never converted.</summary>
    public string CurrencyCode { get; set; } = "USD";

    // ---- State 1: Calculated ----------------------------------------------------------------

    /// <summary>Produced by the payroll engine. Never entered by hand.</summary>
    public decimal CalculatedAmount { get; set; }

    public bool IsCalculated { get; set; }

    public DateTimeOffset? CalculatedAt { get; set; }

    // ---- State 2: Deducted ------------------------------------------------------------------

    /// <summary>
    /// False for employer-borne obligations, where nothing is withheld from anyone. Those show
    /// "not applicable" rather than an outstanding deduction that will never happen.
    /// </summary>
    public bool IsDeductionApplicable { get; set; } = true;

    public decimal DeductedAmount { get; set; }

    public bool IsDeducted { get; set; }

    public DateTimeOffset? DeductedAt { get; set; }

    // ---- State 3: Approved ------------------------------------------------------------------

    public decimal ApprovedAmount { get; set; }

    public bool IsApproved { get; set; }

    public string? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    // ---- State 4: Paid (derived from payments) ----------------------------------------------

    public DateOnly? DueDate { get; set; }

    public string? Notes { get; set; }

    public ICollection<StatutoryPayment> Payments { get; set; } = new List<StatutoryPayment>();

    public ICollection<StatutoryObligationLine> Lines { get; set; } =
        new List<StatutoryObligationLine>();

    public CurrencyCode Currency => new(CurrencyCode);

    public Money Calculated => new(CalculatedAmount, Currency);

    public Money Deducted => new(DeductedAmount, Currency);

    public Money Approved => new(ApprovedAmount, Currency);

    /// <summary>The sum of payments that have not been reversed.</summary>
    public Money PaidAmount => new(
        Payments.Where(p => !p.IsReversed).Sum(p => p.Amount), Currency);

    public Money Outstanding
    {
        get
        {
            var outstanding = CalculatedAmount - PaidAmount.Amount;
            return new Money(outstanding < 0m ? 0m : outstanding, Currency);
        }
    }

    /// <summary>
    /// Derived, never stored. An obligation is paid when unreversed payments cover the amount due.
    /// </summary>
    public bool IsPaid => CalculatedAmount > 0m && PaidAmount.Amount >= CalculatedAmount;

    public bool IsPartiallyPaid => PaidAmount.Amount > 0m && !IsPaid;

    public bool IsOverdue(DateOnly today) =>
        DueDate is { } due && today > due && !IsPaid;

    public StatutoryObligationStatus StatusOn(DateOnly today)
    {
        if (IsPaid)
        {
            return StatutoryObligationStatus.Paid;
        }

        if (IsOverdue(today))
        {
            return StatutoryObligationStatus.Overdue;
        }

        if (IsPartiallyPaid)
        {
            return StatutoryObligationStatus.PartiallyPaid;
        }

        if (IsApproved)
        {
            return StatutoryObligationStatus.Approved;
        }

        return IsDeducted ? StatutoryObligationStatus.Deducted : StatutoryObligationStatus.Calculated;
    }

    /// <summary>Whether this obligation may be approved, and if not, why not.</summary>
    public (bool CanApprove, string? Reason) CanBeApproved()
    {
        if (IsApproved)
        {
            return (false, "This obligation has already been approved.");
        }

        if (!IsCalculated)
        {
            return (false, "An obligation must be calculated before it can be approved.");
        }

        if (IsDeductionApplicable && !IsDeducted)
        {
            return (false,
                "This amount has not yet been deducted from employees. Approving payment of money " +
                "the business has not withheld would authorise paying over funds it does not hold.");
        }

        return (true, null);
    }
}

/// <summary>
/// The per-employee breakdown behind an obligation, so a remittance reconciles to the individuals
/// it was withheld from — which is what a statutory return has to show.
/// </summary>
public class StatutoryObligationLine : Entity
{
    public Guid StatutoryObligationId { get; set; }

    public Guid PayrollRunEmployeeId { get; set; }

    public Guid EmployeeId { get; set; }

    public string EmployeeNumber { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    /// <summary>Carried so a return can be produced without re-joining to employee master data.</summary>
    public string? TaxNumber { get; set; }

    public string? NssaNumber { get; set; }

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "USD";

    public Money AsMoney() => new(Amount, new CurrencyCode(CurrencyCode));
}
