using Tawaka.Domain.Common;

namespace Tawaka.Domain.Loans;

public enum LoanKind
{
    /// <summary>Repaid over a schedule of instalments.</summary>
    Loan = 0,

    /// <summary>An advance against pay, normally recovered in full from the next payroll.</summary>
    SalaryAdvance = 1
}

public enum LoanStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Disbursed = 4,
    Settled = 5,
    WrittenOff = 6,
    Cancelled = 7
}

public enum LoanTransactionType
{
    Disbursement = 0,
    ScheduledRepayment = 1,
    ManualRepayment = 2,
    EarlySettlement = 3,
    Adjustment = 4,
    WriteOff = 5,
    Reversal = 6
}

public enum LoanInstalmentStatus
{
    Scheduled = 0,
    Due = 1,
    Deducted = 2,
    Skipped = 3,
    Cancelled = 4
}

/// <summary>
/// A loan or advance to an employee, recovered through payroll.
/// <para>
/// The outstanding balance is <b>derived from the transactions</b>, never stored (ADR-034). A
/// stored balance beside a transaction ledger is two sources of truth for the same number, and
/// when they disagree — and they do — nobody can say which is right, least of all the employee
/// whose money it is.
/// </para>
/// </summary>
public class EmployeeLoan : AuditableEntity, IApprovableInput
{
    public Guid CompanyId { get; set; }

    public Guid EmployeeId { get; set; }

    public string LoanNumber { get; set; } = string.Empty;

    public LoanKind Kind { get; set; } = LoanKind.Loan;

    /// <summary>
    /// The currency of the loan. A loan taken in USD is repaid in USD; the instalment never
    /// silently becomes a ZiG figure because the employee's payroll currency changed.
    /// </summary>
    public string CurrencyCode { get; set; } = "USD";

    public decimal PrincipalAmount { get; set; }

    public DateOnly? DisbursementDate { get; set; }

    public string? DisbursementReference { get; set; }

    /// <summary>
    /// Interest rate per annum, where the loan carries interest. Null means no interest, which is
    /// a decision the employer makes, not an unresolved statutory question.
    /// </summary>
    public decimal? InterestRatePercent { get; set; }

    /// <summary>
    /// Total interest charged, computed once at scheduling time and recorded, so the schedule and
    /// the balance cannot drift apart.
    /// </summary>
    public decimal InterestAmount { get; set; }

    public int InstalmentCount { get; set; }

    public decimal InstalmentAmount { get; set; }

    public DateOnly? FirstInstalmentDate { get; set; }

    public LoanStatus Status { get; set; } = LoanStatus.Draft;

    public InputApprovalStatus ApprovalStatus { get; set; } = InputApprovalStatus.Draft;

    public string? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? DecisionReason { get; set; }

    /// <summary>
    /// Whether a payroll deduction may exceed the outstanding balance. False everywhere unless a
    /// named person approves it for this loan, with a reason: over-recovering is taking money that
    /// is not owed.
    /// </summary>
    public bool AllowsOverRecovery { get; set; }

    public string? OverRecoveryApprovalReason { get; set; }
    public string? OverRecoveryApprovedBy { get; set; }

    public string? Purpose { get; set; }
    public string? Notes { get; set; }

    public ICollection<LoanInstalment> Instalments { get; set; } = new List<LoanInstalment>();
    public ICollection<LoanTransaction> Transactions { get; set; } = new List<LoanTransaction>();

    public CurrencyCode Currency => new(CurrencyCode);

    public Money Principal => new(PrincipalAmount, Currency);

    public bool IsAvailableToPayroll =>
        InputApprovalTransitions.IsAvailableToPayroll(ApprovalStatus) &&
        Status == LoanStatus.Disbursed;

    /// <summary>Total repayable: principal plus any interest charged at scheduling time.</summary>
    public Money TotalRepayable => new(PrincipalAmount + InterestAmount, Currency);

    public Money TotalRepaid => new(
        Transactions.Where(t => t.CountsTowardsBalance && t.ReducesBalance).Sum(t => t.Amount),
        Currency);

    public Money TotalAdvanced => new(
        Transactions.Where(t => t.CountsTowardsBalance && !t.ReducesBalance).Sum(t => t.Amount),
        Currency);

    /// <summary>
    /// What is still owed. Derived, so it cannot disagree with the ledger that produced it.
    /// </summary>
    public Money Outstanding =>
        new(TotalAdvanced.Amount + InterestAmount - TotalRepaid.Amount, Currency);

    public bool IsSettled => Outstanding.Amount <= 0m;

    /// <summary>
    /// The most a payroll deduction may take, or null where over-recovery has been explicitly
    /// approved for this loan and there is therefore no cap. Null means "no limit", which is why
    /// it is not expressed as a very large number that could be mistaken for one.
    /// </summary>
    public Money? MaximumDeduction => AllowsOverRecovery ? null : Outstanding;
}

/// <summary>One scheduled instalment, tied to the payroll period that should recover it.</summary>
public class LoanInstalment : Entity
{
    public Guid EmployeeLoanId { get; set; }
    public EmployeeLoan? Loan { get; set; }

    public int InstalmentNumber { get; set; }

    public DateOnly DueDate { get; set; }

    /// <summary>
    /// The payroll period this instalment belongs to, where one has been matched. Matching by
    /// period rather than by date means a period with shifted dates still recovers the right
    /// instalment.
    /// </summary>
    public Guid? PayrollPeriodId { get; set; }

    public decimal Amount { get; set; }

    public decimal PrincipalPortion { get; set; }

    public decimal InterestPortion { get; set; }

    public LoanInstalmentStatus Status { get; set; } = LoanInstalmentStatus.Scheduled;

    /// <summary>The payroll run that recovered this instalment, where one has.</summary>
    public Guid? PayrollRunId { get; set; }

    public DateOnly? DeductedOn { get; set; }

    public string? SkipReason { get; set; }
}

/// <summary>
/// One movement on a loan. Append-only: a wrong repayment is reversed with a reason, and the
/// original row stays, because the employee is entitled to see what was taken and what was undone.
/// </summary>
public class LoanTransaction : AuditableEntity
{
    public Guid EmployeeLoanId { get; set; }
    public EmployeeLoan? Loan { get; set; }

    public LoanTransactionType TransactionType { get; set; }

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "USD";

    public DateOnly TransactionDate { get; set; }

    public Guid? LoanInstalmentId { get; set; }

    /// <summary>The payroll run that produced this movement, where it came from payroll.</summary>
    public Guid? PayrollRunId { get; set; }

    public Guid? PayrollPeriodId { get; set; }

    public string? Reference { get; set; }

    public string? Notes { get; set; }

    public bool IsReversed { get; set; }
    public Guid? ReversedByTransactionId { get; set; }
    public string? ReversalReason { get; set; }
    public string? ReversedBy { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }

    public Money Money => new(Amount, new CurrencyCode(CurrencyCode));

    /// <summary>
    /// Whether this movement counts against what the employee owes. A disbursement increases the
    /// balance; everything here reduces it. An <see cref="LoanTransactionType.Adjustment"/> is
    /// signed — a negative adjustment increases the balance again — so a correction in either
    /// direction is one row with a reason rather than an edit to history.
    /// </summary>
    public bool ReducesBalance => TransactionType is LoanTransactionType.ScheduledRepayment
        or LoanTransactionType.ManualRepayment or LoanTransactionType.EarlySettlement
        or LoanTransactionType.WriteOff or LoanTransactionType.Adjustment;

    /// <summary>
    /// Whether this row moves the balance at all.
    /// <para>
    /// A row that has been reversed does not: its reversal already cancelled it. Nor does the
    /// reversal row itself — it is the record of the undoing, and counting it as well would move
    /// the balance twice for one correction. Both are kept and shown; neither is arithmetic.
    /// </para>
    /// </summary>
    public bool CountsTowardsBalance =>
        !IsReversed && TransactionType != LoanTransactionType.Reversal;
}
