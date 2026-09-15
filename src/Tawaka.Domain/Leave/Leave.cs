using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;

namespace Tawaka.Domain.Leave;

public enum LeaveAccrualBasis
{
    /// <summary>A fixed entitlement granted for the leave year.</summary>
    AnnualGrant = 0,

    /// <summary>Accrues per month of service.</summary>
    Monthly = 1,

    /// <summary>Accrues per day or hour worked.</summary>
    PerDayWorked = 2,

    /// <summary>No entitlement is tracked; the balance is not meaningful for this type.</summary>
    NotTracked = 3
}

public enum LeaveTransactionType
{
    OpeningBalance = 0,
    Accrual = 1,
    Taken = 2,
    Adjustment = 3,
    Forfeiture = 4,
    Encashment = 5,
    Reversal = 6
}

/// <summary>
/// A kind of leave, configured rather than coded.
/// <para>
/// Zimbabwe's statutory leave entitlements are set by the Labour Act and, for many employers, by a
/// NEC collective bargaining agreement. Neither could be read from an authoritative source in this
/// environment, so <see cref="StatutoryEntitlementDays"/> is <b>nullable</b> and
/// <see cref="EntitlementVerificationStatus"/> starts Unverified. A leave type whose entitlement is
/// unresolved still works — requests can be captured and approved — but the system reports the
/// balance as undetermined rather than inventing a number of days (ADR-032).
/// </para>
/// </summary>
public class LeaveType : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Whether the employee is paid while on this leave. This is the only property of a leave type
    /// that payroll reads, and it is a company configuration decision, not a calculation.
    /// </summary>
    public bool IsPaid { get; set; } = true;

    public LeaveAccrualBasis AccrualBasis { get; set; } = LeaveAccrualBasis.AnnualGrant;

    /// <summary>
    /// Days granted per leave year, or per month where the basis is monthly. Null means the
    /// entitlement has not been established — not that it is zero.
    /// </summary>
    public decimal? EntitlementDays { get; set; }

    /// <summary>
    /// The statutory minimum entitlement, where one has been established from an authoritative
    /// source. Null while the question is open.
    /// </summary>
    public decimal? StatutoryEntitlementDays { get; set; }

    public VerificationStatus EntitlementVerificationStatus { get; set; } =
        VerificationStatus.Unverified;

    /// <summary>Where the entitlement figure came from, graded exactly like a tax rule.</summary>
    public string? EntitlementSource { get; set; }

    /// <summary>The open compliance question blocking verification, e.g. "Q31".</summary>
    public string? ComplianceQuestion { get; set; }

    public bool RequiresApproval { get; set; } = true;

    /// <summary>
    /// Whether a balance is carried into the next leave year.
    /// <para>
    /// <b>Configured but not yet acted on.</b> Nothing rolls a balance forward automatically:
    /// a new leave year's opening balance is posted as a ledger adjustment with a reason. Doing it
    /// automatically needs the carry-forward rules, which are part of compliance question Q32.
    /// </para>
    /// </summary>
    public bool CarriesForward { get; set; }

    /// <summary>Cap on the carried balance. Recorded; not yet acted on — see above.</summary>
    public decimal? MaximumCarryForwardDays { get; set; }

    /// <summary>Whether weekends and public holidays count against the entitlement.</summary>
    public bool IncludesNonWorkingDays { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>
    /// True when the number of days this type grants is known. False means the balance must be
    /// reported as undetermined rather than as a figure.
    /// </summary>
    public bool HasEstablishedEntitlement =>
        AccrualBasis == LeaveAccrualBasis.NotTracked || EntitlementDays is not null;
}

/// <summary>
/// One employee's entitlement to one leave type for one leave year.
/// <para>
/// The balance is <em>derived from the transactions</em>, never stored and maintained. A stored
/// balance and a transaction ledger disagree eventually, and when they do there is no way to tell
/// which is right.
/// </para>
/// </summary>
public class LeaveEntitlement : AuditableEntity
{
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }

    /// <summary>The leave year this entitlement covers.</summary>
    public DateOnly LeaveYearStart { get; set; }
    public DateOnly LeaveYearEnd { get; set; }

    /// <summary>
    /// Null where the leave type's entitlement has not been established. The screen shows "—", not
    /// zero: an employee with an undetermined entitlement has not been granted nothing.
    /// </summary>
    public decimal? EntitlementDays { get; set; }

    public ICollection<LeaveTransaction> Transactions { get; set; } = new List<LeaveTransaction>();

    public DateRange LeaveYear => new(LeaveYearStart, LeaveYearEnd);

    public decimal DaysAccrued => Transactions
        .Where(t => !t.IsReversed && t.TransactionType is LeaveTransactionType.OpeningBalance
                        or LeaveTransactionType.Accrual or LeaveTransactionType.Adjustment)
        .Sum(t => t.Days);

    public decimal DaysTaken => Transactions
        .Where(t => !t.IsReversed && t.TransactionType == LeaveTransactionType.Taken)
        .Sum(t => t.Days);

    public decimal DaysForfeited => Transactions
        .Where(t => !t.IsReversed && t.TransactionType is LeaveTransactionType.Forfeiture
                        or LeaveTransactionType.Encashment)
        .Sum(t => t.Days);

    /// <summary>
    /// Days remaining, or null where the entitlement itself is undetermined. Null is not zero.
    /// <para>
    /// The granted entitlement plus any ledger accruals and adjustments, less days taken and days
    /// forfeited. Reversal rows are excluded from every bucket: a reversal marks its original row
    /// reversed, so counting the reversal as well would subtract the same days twice.
    /// </para>
    /// </summary>
    public decimal? BalanceDays => EntitlementDays is null
        ? null
        : EntitlementDays.Value + DaysAccrued - DaysTaken - DaysForfeited;
}

/// <summary>
/// One movement in the leave ledger. Append-only: a mistake is corrected by a reversal plus a new
/// entry, never by editing history.
/// </summary>
public class LeaveTransaction : AuditableEntity
{
    public Guid LeaveEntitlementId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }

    public LeaveTransactionType TransactionType { get; set; }

    /// <summary>Signed against the ledger's convention: accruals positive, days taken positive.</summary>
    public decimal Days { get; set; }

    public DateOnly TransactionDate { get; set; }

    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>The leave request this movement came from, where it came from one.</summary>
    public Guid? LeaveRequestId { get; set; }

    /// <summary>The payroll run that consumed this movement, where one did.</summary>
    public Guid? PayrollRunId { get; set; }

    public string? Reason { get; set; }

    public bool IsReversed { get; set; }
    public Guid? ReversedByTransactionId { get; set; }
    public string? ReversalReason { get; set; }
}

/// <summary>
/// A request for leave, moving through the standard input lifecycle before payroll may see it.
/// </summary>
public class LeaveRequest : AuditableEntity, IApprovableInput
{
    public Guid CompanyId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid LeaveTypeId { get; set; }
    public LeaveType? LeaveType { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// Days claimed. Captured rather than derived from the dates, because whether weekends and
    /// public holidays count is a configuration question per leave type, and the calendar that
    /// answered it must be recorded — see <see cref="HolidayCalendarId"/>.
    /// </summary>
    public decimal Days { get; set; }

    /// <summary>The calendar used to work out the days, so the figure stays explicable later.</summary>
    public Guid? HolidayCalendarId { get; set; }

    /// <summary>
    /// Whether this absence is paid. Copied from the leave type at request time and frozen here, so
    /// that reclassifying the type later does not silently re-price historical leave.
    /// </summary>
    public bool IsPaid { get; set; } = true;

    public string? Reason { get; set; }

    public InputApprovalStatus ApprovalStatus { get; set; } = InputApprovalStatus.Draft;

    public string? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? DecisionReason { get; set; }

    public Guid? ConsumedByPayrollRunId { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }

    public Guid? CorrectsLeaveRequestId { get; set; }
    public string? CorrectionReason { get; set; }

    public DateRange Period => new(StartDate, EndDate);

    public bool IsAvailableToPayroll =>
        InputApprovalTransitions.IsAvailableToPayroll(ApprovalStatus);

    /// <summary>
    /// Whether this request overlaps another. Two approved leave requests covering the same day
    /// would either double-count the entitlement or double-dock the pay.
    /// </summary>
    public bool Overlaps(LeaveRequest other) =>
        other.EmployeeId == EmployeeId && other.Id != Id && Period.Overlaps(other.Period);
}
