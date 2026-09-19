using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Security;
using Tawaka.Domain.Common;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory.Obligations;

namespace Tawaka.Application.Statutory.Obligations;

/// <summary>A request to record that an authority was actually paid.</summary>
public sealed record RecordPaymentRequest
{
    public required Guid ObligationId { get; init; }
    public required decimal Amount { get; init; }
    public required string CurrencyCode { get; init; }
    public required DateOnly PaymentDate { get; init; }
    public required string PaymentReference { get; init; }
    public StatutoryPaymentMethod PaymentMethod { get; init; } = StatutoryPaymentMethod.BankTransfer;
    public string? AuthorityReceiptNumber { get; init; }
    public Guid? CompanyBankAccountId { get; init; }
    public decimal PenaltyOrInterestIncluded { get; init; }
    public string? ReceiptFilePath { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// The statutory obligation register.
/// <para>
/// This class enforces the state machine in <c>docs/MILESTONE_4_BRIEF.md</c> §2. The rule the
/// whole design exists to protect: <b>approving a payroll never marks an obligation paid.</b>
/// Only <see cref="RecordPaymentAsync"/> can do that, and only with a date, method, reference and
/// amount.
/// </para>
/// </summary>
public sealed class StatutoryObligationService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public StatutoryObligationService(
        IPayrollDataContext context, ICurrentUser currentUser, IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>
    /// Creates the obligations for a finalised run: <c>Calculated</c> and, for employee-borne
    /// items, <c>Deducted</c>. Approval and payment are separate, later acts.
    /// </summary>
    public async Task<IReadOnlyList<StatutoryObligation>> CreateForRunAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _context.PayrollRuns
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Payroll run not found.");

        var employees = await _context.PayrollRunEmployees
            .Include(e => e.EmployerCostLines)
            .Where(e => e.PayrollRunId == runId && !e.IsExcluded && e.IsCalculated)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var profiles = await _context.EmployeeStatutoryProfiles.AsNoTracking()
            .ToDictionaryAsync(p => p.EmployeeId, cancellationToken).ConfigureAwait(false);

        var now = _clock.Now;
        var created = new List<StatutoryObligation>();

        // Employee-borne obligations: the money is withheld, so deduction applies.
        foreach (var (type, authority, selector) in EmployeeBorne())
        {
            created.AddRange(Build(run, employees, profiles, type, authority, selector,
                deductionApplies: true, now));
        }

        // Employer-borne obligations: nothing is withheld from anyone, so deduction is n/a.
        foreach (var (type, authority, code) in EmployerBorne())
        {
            created.AddRange(BuildFromEmployerCosts(run, employees, profiles, type, authority,
                code, now));
        }

        foreach (var obligation in created)
        {
            _context.StatutoryObligations.Add(obligation);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <summary>
    /// Approves an obligation for payment. Refused before it is calculated, and before it is
    /// deducted where deduction applies.
    /// </summary>
    public async Task<ValidationResult> ApproveAsync(
        Guid obligationId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.StatutoryRecordPayment);

        var validation = ValidationResult.Success();
        var obligation = await _context.StatutoryObligations
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == obligationId, cancellationToken)
            .ConfigureAwait(false);

        if (obligation is null)
        {
            return validation.Add("Obligation", "Statutory obligation not found.");
        }

        var (canApprove, reason) = obligation.CanBeApproved();
        if (!canApprove)
        {
            return validation.Add("Obligation", reason!);
        }

        obligation.IsApproved = true;
        obligation.ApprovedAmount = obligation.CalculatedAmount;
        obligation.ApprovedBy = _currentUser.UserId;
        obligation.ApprovedAt = now();

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;

        DateTimeOffset now() => _clock.Now;
    }

    /// <summary>
    /// Records that the authority was actually paid. The only route to a paid obligation.
    /// </summary>
    public async Task<OperationResult<StatutoryPayment>> RecordPaymentAsync(
        RecordPaymentRequest request, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.StatutoryRecordPayment);

        var validation = ValidationResult.Success();
        var obligation = await _context.StatutoryObligations
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == request.ObligationId, cancellationToken)
            .ConfigureAwait(false);

        if (obligation is null)
        {
            return OperationResult<StatutoryPayment>.Failed(
                validation.Add("Obligation", "Statutory obligation not found."));
        }

        validation.AddIf(!obligation.IsApproved, "Obligation",
            "This obligation has not been approved for payment.");

        validation.Require(request.PaymentReference, nameof(request.PaymentReference),
            "A payment reference is required. A payment with no reference cannot be evidenced.");

        validation.AddIf(request.Amount <= 0m, nameof(request.Amount),
            "A payment must be greater than zero.");

        // A USD liability is not settled with a ZiG payment. That would be a conversion, which is
        // a separate and separately recorded act.
        validation.AddIf(
            !string.Equals(request.CurrencyCode, obligation.CurrencyCode, StringComparison.Ordinal),
            nameof(request.CurrencyCode),
            $"This obligation is denominated in {obligation.CurrencyCode} and must be paid in " +
            $"{obligation.CurrencyCode}, not {request.CurrencyCode}.");

        validation.AddIf(request.PenaltyOrInterestIncluded < 0m,
            nameof(request.PenaltyOrInterestIncluded), "Penalty or interest cannot be negative.");

        validation.AddIf(request.PenaltyOrInterestIncluded > request.Amount,
            nameof(request.PenaltyOrInterestIncluded),
            "Penalty or interest cannot exceed the payment.");

        var principal = request.Amount - request.PenaltyOrInterestIncluded;
        validation.AddIf(principal > obligation.Outstanding.Amount,
            nameof(request.Amount),
            $"This payment of {principal:N2} exceeds the outstanding balance of " +
            $"{obligation.Outstanding.Amount:N2}. Record any penalty or interest separately.");

        if (!validation.IsValid)
        {
            return OperationResult<StatutoryPayment>.Failed(validation);
        }

        var payment = new StatutoryPayment
        {
            StatutoryObligationId = obligation.Id,
            Amount = request.Amount,
            CurrencyCode = request.CurrencyCode,
            PaymentDate = request.PaymentDate,
            PaymentMethod = request.PaymentMethod,
            PaymentReference = request.PaymentReference,
            AuthorityReceiptNumber = request.AuthorityReceiptNumber,
            CompanyBankAccountId = request.CompanyBankAccountId,
            PenaltyOrInterestIncluded = request.PenaltyOrInterestIncluded,
            ReceiptFilePath = request.ReceiptFilePath,
            Notes = request.Notes
        };

        _context.StatutoryPayments.Add(payment);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return OperationResult<StatutoryPayment>.Success(payment);
    }

    /// <summary>
    /// Reverses a payment entered in error. The payment row is retained — both it and the reversal
    /// stay visible, because a payment that vanishes is a payment nobody can audit.
    /// </summary>
    public async Task<ValidationResult> ReversePaymentAsync(
        Guid paymentId, string reason, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.StatutoryRecordPayment);

        var validation = ValidationResult.Success();
        validation.Require(reason, nameof(reason), "A reason is required to reverse a payment.");

        var payment = await _context.StatutoryPayments
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken).ConfigureAwait(false);

        if (payment is null)
        {
            return validation.Add("Payment", "Payment not found.");
        }

        validation.AddIf(payment.IsReversed, "Payment", "This payment has already been reversed.");

        if (!validation.IsValid)
        {
            return validation;
        }

        payment.IsReversed = true;
        payment.ReversalReason = reason;
        payment.ReversedBy = _currentUser.UserId;
        payment.ReversedAt = _clock.Now;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public Task<List<StatutoryObligation>> GetRegisterAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.StatutoryView);

        return _context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Where(o => o.CompanyId == companyId)
            .OrderByDescending(o => o.DueDate)
            .ThenBy(o => o.ObligationType)
            .ToListAsync(cancellationToken);
    }


    // ---- Construction ------------------------------------------------------------------------

    private static IEnumerable<(StatutoryObligationType Type, StatutoryAuthority Authority,
        Func<PayrollRunEmployee, decimal?> Selector)> EmployeeBorne() =>
        new (StatutoryObligationType, StatutoryAuthority, Func<PayrollRunEmployee, decimal?>)[]
        {
            (StatutoryObligationType.Paye, StatutoryAuthority.Zimra, e => e.PayeAfterCreditsAmount),
            (StatutoryObligationType.AidsLevy, StatutoryAuthority.Zimra, e => e.AidsLevyAmount),
            (StatutoryObligationType.NssaPobsEmployee, StatutoryAuthority.Nssa,
                e => e.NssaEmployeeAmount)
        };

    private static IEnumerable<(StatutoryObligationType Type, StatutoryAuthority Authority,
        string Code)> EmployerBorne() =>
        new[]
        {
            (StatutoryObligationType.NssaPobsEmployer, StatutoryAuthority.Nssa, "NSSA_POBS_ER"),
            (StatutoryObligationType.Apwcs, StatutoryAuthority.Nssa, "APWCS"),
            (StatutoryObligationType.Zimdef, StatutoryAuthority.Zimdef, "Zimdef"),
            (StatutoryObligationType.StandardsDevelopmentFund,
                StatutoryAuthority.StandardsDevelopmentFund, "StandardsDevelopmentFund")
        };

    private IEnumerable<StatutoryObligation> Build(
        PayrollRun run, List<PayrollRunEmployee> employees,
        IReadOnlyDictionary<Guid, Domain.Employees.EmployeeStatutoryProfile> profiles,
        StatutoryObligationType type, StatutoryAuthority authority,
        Func<PayrollRunEmployee, decimal?> selector, bool deductionApplies, DateTimeOffset now)
    {
        // Grouped by currency: a USD liability and a ZiG liability are separate obligations,
        // remitted to different accounts, and are never combined.
        foreach (var group in employees.GroupBy(e => e.CurrencyCode))
        {
            var contributors = group.Where(e => selector(e) is > 0m).ToList();
            var total = contributors.Sum(e => selector(e)!.Value);

            if (total <= 0m)
            {
                continue;
            }

            var obligation = new StatutoryObligation
            {
                CompanyId = run.CompanyId,
                PayrollRunId = run.Id,
                PayrollPeriodId = run.PayrollPeriodId,
                Authority = authority,
                ObligationType = type,
                CurrencyCode = group.Key,
                CalculatedAmount = total,
                IsCalculated = true,
                CalculatedAt = now,
                IsDeductionApplicable = deductionApplies,
                DeductedAmount = deductionApplies ? total : 0m,
                IsDeducted = deductionApplies,
                DeductedAt = deductionApplies ? now : null,
                DueDate = DueDateFor(authority, run)
            };

            foreach (var employee in contributors)
            {
                profiles.TryGetValue(employee.EmployeeId, out var profile);
                obligation.Lines.Add(new StatutoryObligationLine
                {
                    PayrollRunEmployeeId = employee.Id,
                    EmployeeId = employee.EmployeeId,
                    EmployeeNumber = employee.EmployeeNumber,
                    EmployeeName = employee.EmployeeName,
                    TaxNumber = profile?.TaxNumber,
                    NssaNumber = profile?.NssaNumber,
                    Amount = selector(employee)!.Value,
                    CurrencyCode = employee.CurrencyCode
                });
            }

            yield return obligation;
        }
    }

    private IEnumerable<StatutoryObligation> BuildFromEmployerCosts(
        PayrollRun run, List<PayrollRunEmployee> employees,
        IReadOnlyDictionary<Guid, Domain.Employees.EmployeeStatutoryProfile> profiles,
        StatutoryObligationType type, StatutoryAuthority authority, string code,
        DateTimeOffset now) =>
        Build(run, employees, profiles, type, authority,
            e => e.EmployerCostLines.Where(l => l.Code == code).Sum(l => (decimal?)l.Amount),
            deductionApplies: false, now);

    /// <summary>
    /// Due dates differ by authority — PAYE and NSSA on the 10th, ZIMDEF on the 15th — so each
    /// carries its own rule rather than a single assumed deadline.
    /// </summary>
    private static DateOnly DueDateFor(StatutoryAuthority authority, PayrollRun run)
    {
        var payDate = run.PayrollPeriod?.PayDate ?? DateOnly.FromDateTime(DateTime.Today);
        var day = authority == StatutoryAuthority.Zimdef ? 15 : 10;
        var followingMonth = new DateOnly(payDate.Year, payDate.Month, 1).AddMonths(1);
        return new DateOnly(followingMonth.Year, followingMonth.Month, day);
    }
}
