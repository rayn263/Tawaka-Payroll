using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Security;
using Tawaka.Domain.Common;
using Tawaka.Domain.Loans;
using Tawaka.Domain.Security;

namespace Tawaka.Application.Loans;

public sealed record LoanCommand
{
    public required Guid EmployeeId { get; init; }
    public LoanKind Kind { get; init; } = LoanKind.Loan;
    public required string CurrencyCode { get; init; }
    public required decimal PrincipalAmount { get; init; }
    public required int InstalmentCount { get; init; }
    public DateOnly? FirstInstalmentDate { get; init; }
    public decimal? InterestRatePercent { get; init; }
    public decimal InterestAmount { get; init; }
    public string? Purpose { get; init; }
}

/// <summary>A loan recovery due from one payroll period.</summary>
public sealed record LoanDueDeduction(
    EmployeeLoan Loan, LoanInstalment? Instalment, Money Amount, Money OutstandingBefore,
    bool WasCapped);

/// <summary>
/// Employee loans and advances, and their recovery through payroll.
/// <para>
/// The outstanding balance is always derived from the transaction ledger (ADR-034). Nothing here
/// stores a balance, so nothing here can disagree with the movements that produced it. A deduction
/// is capped at what is still owed unless over-recovery was explicitly approved for that loan by a
/// named person with a reason — taking more than is owed is taking money that is not the
/// employer's.
/// </para>
/// </summary>
public sealed class LoanService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public LoanService(IPayrollDataContext context, ICurrentUser currentUser, IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<OperationResult<EmployeeLoan>> CreateAsync(
        LoanCommand command, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LoansEdit);

        var validation = ValidationResult.Success();
        validation.Require(command.CurrencyCode, "Currency");
        validation.AddIf(command.PrincipalAmount <= 0m, "Principal",
            "A loan principal must be greater than zero.");
        validation.AddIf(command.InstalmentCount <= 0, "Instalments",
            "A loan needs at least one instalment.");
        validation.AddIf(command.InterestAmount < 0m, "Interest",
            "Interest cannot be negative.");

        var employee = await _context.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == command.EmployeeId, cancellationToken)
            .ConfigureAwait(false);

        if (employee is null)
        {
            return OperationResult<EmployeeLoan>.Failed(
                validation.Add("Employee", "Employee not found."));
        }

        if (!validation.IsValid)
        {
            return OperationResult<EmployeeLoan>.Failed(validation);
        }

        var total = command.PrincipalAmount + command.InterestAmount;
        var instalmentAmount = Math.Round(total / command.InstalmentCount, 2,
            MidpointRounding.AwayFromZero);

        var loan = new EmployeeLoan
        {
            CompanyId = employee.CompanyId,
            EmployeeId = command.EmployeeId,
            LoanNumber = await NextLoanNumberAsync(employee.CompanyId, command.Kind, cancellationToken)
                .ConfigureAwait(false),
            Kind = command.Kind,
            CurrencyCode = command.CurrencyCode,
            PrincipalAmount = command.PrincipalAmount,
            InterestRatePercent = command.InterestRatePercent,
            InterestAmount = command.InterestAmount,
            InstalmentCount = command.InstalmentCount,
            InstalmentAmount = instalmentAmount,
            FirstInstalmentDate = command.FirstInstalmentDate,
            Purpose = command.Purpose,
            Status = LoanStatus.Draft,
            ApprovalStatus = InputApprovalStatus.Draft
        };

        BuildSchedule(loan);

        _context.EmployeeLoans.Add(loan);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<EmployeeLoan>.Success(loan);
    }

    /// <summary>
    /// Builds the repayment schedule. The last instalment absorbs the rounding difference, so the
    /// instalments always sum to exactly what is repayable — a schedule that does not add up is a
    /// dispute waiting to happen.
    /// </summary>
    private static void BuildSchedule(EmployeeLoan loan)
    {
        loan.Instalments.Clear();

        var total = loan.PrincipalAmount + loan.InterestAmount;
        var start = loan.FirstInstalmentDate ?? DateOnly.FromDateTime(DateTime.Today);
        var running = 0m;

        for (var number = 1; number <= loan.InstalmentCount; number++)
        {
            var amount = number == loan.InstalmentCount
                ? total - running
                : loan.InstalmentAmount;
            running += amount;

            var interestPortion = loan.InterestAmount == 0m
                ? 0m
                : Math.Round(loan.InterestAmount / loan.InstalmentCount, 2,
                    MidpointRounding.AwayFromZero);

            loan.Instalments.Add(new LoanInstalment
            {
                InstalmentNumber = number,
                DueDate = start.AddMonths(number - 1),
                Amount = amount,
                InterestPortion = Math.Min(interestPortion, amount),
                PrincipalPortion = amount - Math.Min(interestPortion, amount),
                Status = LoanInstalmentStatus.Scheduled
            });
        }
    }

    public async Task<ValidationResult> SubmitAsync(
        Guid loanId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LoansEdit);

        var validation = ValidationResult.Success();
        var loan = await _context.EmployeeLoans
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken).ConfigureAwait(false);

        if (loan is null)
        {
            return validation.Add("Loan", "Loan not found.");
        }

        validation.AddIf(!InputApprovalTransitions.CanSubmit(loan.ApprovalStatus), "Loan",
            $"A {loan.ApprovalStatus} loan cannot be submitted.");

        if (!validation.IsValid)
        {
            return validation;
        }

        loan.ApprovalStatus = InputApprovalStatus.Submitted;
        loan.Status = LoanStatus.Submitted;
        loan.SubmittedBy = _currentUser.UserId;
        loan.SubmittedAt = _clock.Now;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public async Task<ValidationResult> ApproveAsync(
        Guid loanId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LoansApprove);

        var validation = ValidationResult.Success();
        var loan = await _context.EmployeeLoans
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken).ConfigureAwait(false);

        if (loan is null)
        {
            return validation.Add("Loan", "Loan not found.");
        }

        validation.AddIf(!InputApprovalTransitions.CanDecide(loan.ApprovalStatus), "Loan",
            $"Only a submitted loan can be approved. This one is {loan.ApprovalStatus}.");

        validation.AddIf(loan.SubmittedBy is not null && loan.SubmittedBy == _currentUser.UserId,
            "Loan", "Segregation of duties: the user who submitted a loan cannot approve it.");

        if (!validation.IsValid)
        {
            return validation;
        }

        loan.ApprovalStatus = InputApprovalStatus.Approved;
        loan.Status = LoanStatus.Approved;
        loan.ApprovedBy = _currentUser.UserId;
        loan.ApprovedAt = _clock.Now;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public async Task<ValidationResult> RejectAsync(
        Guid loanId, string reason, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LoansApprove);

        var validation = ValidationResult.Success();
        validation.Require(reason, "Reason", "A rejection needs a reason.");

        var loan = await _context.EmployeeLoans
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken).ConfigureAwait(false);

        if (loan is null)
        {
            return validation.Add("Loan", "Loan not found.");
        }

        validation.AddIf(!InputApprovalTransitions.CanDecide(loan.ApprovalStatus), "Loan",
            $"A {loan.ApprovalStatus} loan cannot be rejected.");

        if (!validation.IsValid)
        {
            return validation;
        }

        loan.ApprovalStatus = InputApprovalStatus.Rejected;
        loan.Status = LoanStatus.Rejected;
        loan.DecisionReason = reason;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// Records that the money reached the employee. Until this happens there is nothing to recover,
    /// which is why an approved-but-undisbursed loan produces no payroll deduction.
    /// </summary>
    public async Task<ValidationResult> DisburseAsync(
        Guid loanId, DateOnly disbursementDate, string reference,
        CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LoansDisburse);

        var validation = ValidationResult.Success();
        validation.Require(reference, "Reference",
            "A disbursement reference is required: money left the business and must be evidenced.");

        var loan = await _context.EmployeeLoans
            .Include(l => l.Transactions)
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken).ConfigureAwait(false);

        if (loan is null)
        {
            return validation.Add("Loan", "Loan not found.");
        }

        validation.AddIf(loan.ApprovalStatus != InputApprovalStatus.Approved, "Loan",
            "Only an approved loan can be disbursed.");
        validation.AddIf(loan.Status == LoanStatus.Disbursed, "Loan",
            "This loan has already been disbursed.");

        if (!validation.IsValid)
        {
            return validation;
        }

        loan.Status = LoanStatus.Disbursed;
        loan.DisbursementDate = disbursementDate;
        loan.DisbursementReference = reference;

        _context.LoanTransactions.Add(new LoanTransaction
        {
            EmployeeLoanId = loan.Id,
            TransactionType = LoanTransactionType.Disbursement,
            Amount = loan.PrincipalAmount,
            CurrencyCode = loan.CurrencyCode,
            TransactionDate = disbursementDate,
            Reference = reference
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// Approves over-recovery on one loan: a named person accepting that a deduction may exceed the
    /// outstanding balance. Deliberately per loan and per reason, never a global setting.
    /// </summary>
    public async Task<ValidationResult> ApproveOverRecoveryAsync(
        Guid loanId, string reason, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LoansApprove);

        var validation = ValidationResult.Success();
        validation.Require(reason, "Reason",
            "Over-recovery takes more than is owed and needs a stated business reason.");

        var loan = await _context.EmployeeLoans
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken).ConfigureAwait(false);

        if (loan is null)
        {
            return validation.Add("Loan", "Loan not found.");
        }

        if (!validation.IsValid)
        {
            return validation;
        }

        loan.AllowsOverRecovery = true;
        loan.OverRecoveryApprovalReason = reason;
        loan.OverRecoveryApprovedBy = _currentUser.UserId;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// What payroll should recover from this period, per loan, already capped at what is owed.
    /// <para>
    /// The cap is applied here rather than in the engine because it is a fact about the loan
    /// ledger, not about pay: the engine is told the amount and the balance it was capped against,
    /// and records both.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<LoanDueDeduction>> GetDueDeductionsAsync(
        Guid employeeId, Guid payrollPeriodId, DateOnly periodEnd,
        CancellationToken cancellationToken = default)
    {
        var loans = await _context.EmployeeLoans.AsNoTracking()
            .Include(l => l.Instalments)
            .Include(l => l.Transactions)
            .Where(l => l.EmployeeId == employeeId &&
                        l.Status == LoanStatus.Disbursed &&
                        (l.ApprovalStatus == InputApprovalStatus.Approved ||
                         l.ApprovalStatus == InputApprovalStatus.Locked))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var due = new List<LoanDueDeduction>();

        foreach (var loan in loans)
        {
            if (loan.IsSettled)
            {
                continue;
            }

            var instalment = loan.Instalments
                .Where(i => i.Status is LoanInstalmentStatus.Scheduled or LoanInstalmentStatus.Due)
                .Where(i => i.PayrollPeriodId == payrollPeriodId || i.DueDate <= periodEnd)
                .OrderBy(i => i.InstalmentNumber)
                .FirstOrDefault();

            if (instalment is null)
            {
                continue;
            }

            var outstanding = loan.Outstanding;
            var scheduled = new Money(instalment.Amount, loan.Currency);

            var amount = scheduled;
            var capped = false;

            if (!loan.AllowsOverRecovery && scheduled.Amount > outstanding.Amount)
            {
                amount = outstanding;
                capped = true;
            }

            if (amount.Amount <= 0m)
            {
                continue;
            }

            due.Add(new LoanDueDeduction(loan, instalment, amount, outstanding, capped));
        }

        return due;
    }

    /// <summary>
    /// Posts the repayments a payroll run actually deducted. Called once the run is finalised, so
    /// that a calculated-but-abandoned run never moves a loan balance.
    /// </summary>
    public async Task RecordPayrollRepaymentsAsync(
        Guid payrollRunId, Guid payrollPeriodId,
        IReadOnlyList<(Guid LoanId, Guid? InstalmentId, decimal Amount, string CurrencyCode)> repayments,
        DateOnly paymentDate, CancellationToken cancellationToken = default)
    {
        if (repayments.Count == 0)
        {
            return;
        }

        var loanIds = repayments.Select(r => r.LoanId).Distinct().ToList();
        var loans = await _context.EmployeeLoans
            .Include(l => l.Instalments)
            .Include(l => l.Transactions)
            .Where(l => loanIds.Contains(l.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var repayment in repayments.Where(r => r.Amount > 0m))
        {
            var loan = loans.FirstOrDefault(l => l.Id == repayment.LoanId);
            if (loan is null)
            {
                continue;
            }

            _context.LoanTransactions.Add(new LoanTransaction
            {
                EmployeeLoanId = loan.Id,
                TransactionType = LoanTransactionType.ScheduledRepayment,
                Amount = repayment.Amount,
                CurrencyCode = repayment.CurrencyCode,
                TransactionDate = paymentDate,
                LoanInstalmentId = repayment.InstalmentId,
                PayrollRunId = payrollRunId,
                PayrollPeriodId = payrollPeriodId,
                Reference = $"Payroll run {payrollRunId}"
            });

            if (repayment.InstalmentId is { } instalmentId)
            {
                var instalment = loan.Instalments.FirstOrDefault(i => i.Id == instalmentId);
                if (instalment is not null)
                {
                    instalment.Status = LoanInstalmentStatus.Deducted;
                    instalment.PayrollRunId = payrollRunId;
                    instalment.DeductedOn = paymentDate;
                }
            }

            // The loan itself is locked once payroll has recovered against it, so its terms cannot
            // be edited underneath a completed run.
            if (loan.ApprovalStatus == InputApprovalStatus.Approved)
            {
                loan.ApprovalStatus = InputApprovalStatus.Locked;
            }
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var loan in loans)
        {
            await SettleIfClearedAsync(loan.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Records a repayment made outside payroll, such as a cash settlement.</summary>
    public async Task<OperationResult<LoanTransaction>> RecordManualRepaymentAsync(
        Guid loanId, decimal amount, DateOnly date, string reference, bool isEarlySettlement,
        CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LoansEdit);

        var validation = ValidationResult.Success();
        validation.Require(reference, "Reference", "A repayment reference is required.");
        validation.AddIf(amount <= 0m, "Amount", "A repayment must be greater than zero.");

        var loan = await _context.EmployeeLoans
            .Include(l => l.Transactions)
            .Include(l => l.Instalments)
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken).ConfigureAwait(false);

        if (loan is null)
        {
            return OperationResult<LoanTransaction>.Failed(
                validation.Add("Loan", "Loan not found."));
        }

        validation.AddIf(loan.Status != LoanStatus.Disbursed, "Loan",
            "Only a disbursed loan can be repaid.");

        validation.AddIf(!loan.AllowsOverRecovery && amount > loan.Outstanding.Amount, "Amount",
            $"This repayment of {amount:N2} exceeds the outstanding balance of " +
            $"{loan.Outstanding.Amount:N2}. Approve over-recovery on this loan if that is intended.");

        if (!validation.IsValid)
        {
            return OperationResult<LoanTransaction>.Failed(validation);
        }

        var transaction = new LoanTransaction
        {
            EmployeeLoanId = loan.Id,
            TransactionType = isEarlySettlement
                ? LoanTransactionType.EarlySettlement
                : LoanTransactionType.ManualRepayment,
            Amount = amount,
            CurrencyCode = loan.CurrencyCode,
            TransactionDate = date,
            Reference = reference
        };

        _context.LoanTransactions.Add(transaction);

        // An early settlement clears the remaining schedule: those instalments will never be
        // deducted, and leaving them Scheduled would have payroll try.
        if (isEarlySettlement)
        {
            foreach (var instalment in loan.Instalments
                         .Where(i => i.Status == LoanInstalmentStatus.Scheduled))
            {
                instalment.Status = LoanInstalmentStatus.Cancelled;
                instalment.SkipReason = "Loan settled early.";
            }
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await SettleIfClearedAsync(loan.Id, cancellationToken).ConfigureAwait(false);
        return OperationResult<LoanTransaction>.Success(transaction);
    }

    /// <summary>
    /// Reverses a loan movement. The original row stays with its reason: an employee is entitled to
    /// see both what was taken and what was given back.
    /// </summary>
    public async Task<ValidationResult> ReverseAsync(
        Guid transactionId, string reason, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LoansEdit);

        var validation = ValidationResult.Success();
        validation.Require(reason, "Reason", "A reversal needs a reason.");

        var transaction = await _context.LoanTransactions
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken).ConfigureAwait(false);

        if (transaction is null)
        {
            return validation.Add("Transaction", "Loan transaction not found.");
        }

        validation.AddIf(transaction.IsReversed, "Transaction",
            "This transaction has already been reversed.");

        if (!validation.IsValid)
        {
            return validation;
        }

        var reversal = new LoanTransaction
        {
            EmployeeLoanId = transaction.EmployeeLoanId,
            TransactionType = LoanTransactionType.Reversal,
            Amount = -transaction.Amount,
            CurrencyCode = transaction.CurrencyCode,
            TransactionDate = DateOnly.FromDateTime(_clock.Now.Date),
            LoanInstalmentId = transaction.LoanInstalmentId,
            PayrollRunId = transaction.PayrollRunId,
            Reference = $"Reversal of {transaction.Reference}",
            Notes = reason
        };

        _context.LoanTransactions.Add(reversal);
        transaction.IsReversed = true;
        transaction.ReversalReason = reason;
        transaction.ReversedBy = _currentUser.UserId;
        transaction.ReversedAt = _clock.Now;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public Task<List<EmployeeLoan>> GetLoansAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LoansView);

        return _context.EmployeeLoans.AsNoTracking()
            .Include(l => l.Instalments)
            .Include(l => l.Transactions)
            .Where(l => l.CompanyId == companyId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<List<EmployeeLoan>> GetApprovalQueueAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LoansView);

        return _context.EmployeeLoans.AsNoTracking()
            .Include(l => l.Instalments)
            .Include(l => l.Transactions)
            .Where(l => l.CompanyId == companyId &&
                        l.ApprovalStatus == InputApprovalStatus.Submitted)
            .OrderBy(l => l.SubmittedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// A settled loan is marked settled rather than left looking live. The test is the derived
    /// balance, so a loan cannot be marked settled while money is still owed.
    /// </summary>
    private async Task SettleIfClearedAsync(Guid loanId, CancellationToken cancellationToken)
    {
        var loan = await _context.EmployeeLoans
            .Include(l => l.Transactions)
            .Include(l => l.Instalments)
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken).ConfigureAwait(false);

        if (loan is null || !loan.IsSettled || loan.Status == LoanStatus.Settled)
        {
            return;
        }

        loan.Status = LoanStatus.Settled;

        foreach (var instalment in loan.Instalments
                     .Where(i => i.Status == LoanInstalmentStatus.Scheduled))
        {
            instalment.Status = LoanInstalmentStatus.Cancelled;
            instalment.SkipReason = "Loan fully repaid.";
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> NextLoanNumberAsync(
        Guid companyId, LoanKind kind, CancellationToken cancellationToken)
    {
        var prefix = kind == LoanKind.SalaryAdvance ? "ADV" : "LN";
        var year = _clock.Now.Year;
        var count = await _context.EmployeeLoans
            .CountAsync(l => l.CompanyId == companyId && l.Kind == kind, cancellationToken)
            .ConfigureAwait(false);

        return $"{prefix}-{year}-{count + 1:D4}";
    }
}
