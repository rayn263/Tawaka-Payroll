using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Security;
using Tawaka.Domain.Common;
using Tawaka.Domain.Leave;
using Tawaka.Domain.Security;

namespace Tawaka.Application.Leave;

public sealed record LeaveRequestCommand
{
    public required Guid EmployeeId { get; init; }
    public required Guid LeaveTypeId { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }

    /// <summary>
    /// Days claimed. Optional: where it is omitted the service counts the days the calendar says
    /// are working days, which is a calendar lookup rather than a payroll calculation.
    /// </summary>
    public decimal? Days { get; init; }

    public string? Reason { get; init; }
}

/// <summary>
/// Leave types, entitlements, balances and requests.
/// <para>
/// The balance is never stored: it is the sum of the ledger. And where a leave type's entitlement
/// has not been established from an authoritative source, the balance is reported as
/// <b>undetermined</b> rather than as a number — the same distinction between unresolved and zero
/// that the payroll engine makes (ADR-032).
/// </para>
/// </summary>
public sealed class LeaveService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public LeaveService(IPayrollDataContext context, ICurrentUser currentUser, IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    // ---- Requests --------------------------------------------------------------------------

    public async Task<OperationResult<LeaveRequest>> RequestAsync(
        LeaveRequestCommand command, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LeaveEdit);

        var validation = ValidationResult.Success();

        var leaveType = await _context.LeaveTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == command.LeaveTypeId, cancellationToken)
            .ConfigureAwait(false);

        if (leaveType is null)
        {
            return OperationResult<LeaveRequest>.Failed(
                validation.Add("LeaveType", "Leave type not found."));
        }

        var employee = await _context.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == command.EmployeeId, cancellationToken)
            .ConfigureAwait(false);

        if (employee is null)
        {
            return OperationResult<LeaveRequest>.Failed(
                validation.Add("Employee", "Employee not found."));
        }

        validation.AddIf(command.EndDate < command.StartDate, "EndDate",
            "Leave cannot end before it starts.");

        var calendar = await DefaultCalendarAsync(employee.CompanyId, command.StartDate,
            cancellationToken).ConfigureAwait(false);

        var days = command.Days ?? await CountLeaveDaysAsync(
            leaveType, calendar?.Id, command.StartDate, command.EndDate, cancellationToken)
            .ConfigureAwait(false);

        validation.AddIf(days <= 0m, "Days",
            "This request covers no leave days. Check the dates and the calendar.");

        // Two approved requests over the same day would either double-count the entitlement or
        // double-dock the pay, depending on which is paid. Neither is recoverable quietly.
        var overlapping = await _context.LeaveRequests.AsNoTracking()
            .Where(r => r.EmployeeId == command.EmployeeId &&
                        r.ApprovalStatus != InputApprovalStatus.Rejected &&
                        r.ApprovalStatus != InputApprovalStatus.Returned &&
                        r.StartDate <= command.EndDate && r.EndDate >= command.StartDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var clash in overlapping)
        {
            validation.Add("Dates",
                $"This overlaps leave already requested from {clash.StartDate:dd MMM yyyy} to " +
                $"{clash.EndDate:dd MMM yyyy} ({clash.ApprovalStatus}).");
        }

        if (!validation.IsValid)
        {
            return OperationResult<LeaveRequest>.Failed(validation);
        }

        var request = new LeaveRequest
        {
            CompanyId = employee.CompanyId,
            EmployeeId = command.EmployeeId,
            LeaveTypeId = command.LeaveTypeId,
            StartDate = command.StartDate,
            EndDate = command.EndDate,
            Days = days,
            HolidayCalendarId = calendar?.Id,

            // Frozen at request time. Reclassifying the leave type later must not silently
            // re-price leave that has already been taken and paid.
            IsPaid = leaveType.IsPaid,
            Reason = command.Reason,
            ApprovalStatus = InputApprovalStatus.Draft
        };

        _context.LeaveRequests.Add(request);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<LeaveRequest>.Success(request);
    }

    public async Task<ValidationResult> SubmitAsync(
        Guid requestId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LeaveEdit);

        var validation = ValidationResult.Success();
        var request = await _context.LeaveRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return validation.Add("Request", "Leave request not found.");
        }

        validation.AddIf(!InputApprovalTransitions.CanSubmit(request.ApprovalStatus), "Request",
            $"A {request.ApprovalStatus} leave request cannot be submitted.");

        if (!validation.IsValid)
        {
            return validation;
        }

        request.ApprovalStatus = InputApprovalStatus.Submitted;
        request.SubmittedBy = _currentUser.UserId;
        request.SubmittedAt = _clock.Now;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// Approves leave and posts the days to the ledger. Approving is what moves the balance —
    /// requesting does not, because a request that is never approved must leave no trace on the
    /// entitlement.
    /// </summary>
    public async Task<ValidationResult> ApproveAsync(
        Guid requestId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LeaveApprove);

        var validation = ValidationResult.Success();
        var request = await _context.LeaveRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return validation.Add("Request", "Leave request not found.");
        }

        validation.AddIf(!InputApprovalTransitions.CanDecide(request.ApprovalStatus), "Request",
            $"Only a submitted leave request can be approved. This one is {request.ApprovalStatus}.");

        validation.AddIf(
            request.SubmittedBy is not null && request.SubmittedBy == _currentUser.UserId,
            "Request",
            "Segregation of duties: the user who submitted a leave request cannot approve it.");

        if (!validation.IsValid)
        {
            return validation;
        }

        request.ApprovalStatus = InputApprovalStatus.Approved;
        request.ApprovedBy = _currentUser.UserId;
        request.ApprovedAt = _clock.Now;

        var entitlement = await EnsureEntitlementAsync(
            request.CompanyId, request.EmployeeId, request.LeaveTypeId, request.StartDate,
            cancellationToken).ConfigureAwait(false);

        _context.LeaveTransactions.Add(new LeaveTransaction
        {
            LeaveEntitlementId = entitlement.Id,
            EmployeeId = request.EmployeeId,
            LeaveTypeId = request.LeaveTypeId,
            TransactionType = LeaveTransactionType.Taken,
            Days = request.Days,
            TransactionDate = DateOnly.FromDateTime(_clock.Now.Date),
            EffectiveFrom = request.StartDate,
            EffectiveTo = request.EndDate,
            LeaveRequestId = request.Id,
            Reason = "Approved leave request"
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public Task<ValidationResult> RejectAsync(
        Guid requestId, string reason, CancellationToken cancellationToken = default) =>
        DecideAsync(requestId, InputApprovalStatus.Rejected, reason, cancellationToken);

    public Task<ValidationResult> ReturnAsync(
        Guid requestId, string reason, CancellationToken cancellationToken = default) =>
        DecideAsync(requestId, InputApprovalStatus.Returned, reason, cancellationToken);

    private async Task<ValidationResult> DecideAsync(
        Guid requestId, InputApprovalStatus decision, string reason,
        CancellationToken cancellationToken)
    {
        _currentUser.Require(Permissions.LeaveApprove);

        var validation = ValidationResult.Success();
        validation.Require(reason, "Reason", "A reason is required.");

        var request = await _context.LeaveRequests
            .Include(r => r.LeaveType)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return validation.Add("Request", "Leave request not found.");
        }

        var wasApproved = request.ApprovalStatus == InputApprovalStatus.Approved;
        var allowed = decision == InputApprovalStatus.Rejected
            ? InputApprovalTransitions.CanDecide(request.ApprovalStatus)
            : InputApprovalTransitions.CanReturn(request.ApprovalStatus);

        validation.AddIf(!allowed, "Request",
            $"A {request.ApprovalStatus} leave request cannot be " +
            $"{decision.ToString().ToLowerInvariant()}.");

        if (!validation.IsValid)
        {
            return validation;
        }

        // Withdrawing an approval must give the days back, and by reversal rather than by deleting
        // the row: the ledger stays a complete record of what happened and when.
        if (wasApproved)
        {
            await ReverseTakenAsync(request, "Approval withdrawn: " + reason, cancellationToken)
                .ConfigureAwait(false);
        }

        request.ApprovalStatus = decision;
        request.DecisionReason = reason;
        request.ApprovedBy = null;
        request.ApprovedAt = null;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    private async Task ReverseTakenAsync(
        LeaveRequest request, string reason, CancellationToken cancellationToken)
    {
        var taken = await _context.LeaveTransactions
            .Where(t => t.LeaveRequestId == request.Id &&
                        t.TransactionType == LeaveTransactionType.Taken && !t.IsReversed)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var transaction in taken)
        {
            var reversal = new LeaveTransaction
            {
                LeaveEntitlementId = transaction.LeaveEntitlementId,
                EmployeeId = transaction.EmployeeId,
                LeaveTypeId = transaction.LeaveTypeId,
                TransactionType = LeaveTransactionType.Reversal,
                Days = -transaction.Days,
                TransactionDate = DateOnly.FromDateTime(_clock.Now.Date),
                LeaveRequestId = request.Id,
                Reason = reason
            };

            _context.LeaveTransactions.Add(reversal);
            transaction.IsReversed = true;
            transaction.ReversalReason = reason;
        }
    }

    // ---- Entitlements and balances ---------------------------------------------------------

    /// <summary>
    /// The employee's balance for a leave type, or null where the entitlement has not been
    /// established. Null is not zero, and the screens must not render it as such.
    /// </summary>
    public async Task<LeaveEntitlement?> GetEntitlementAsync(
        Guid employeeId, Guid leaveTypeId, DateOnly on,
        CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LeaveView);

        return await _context.LeaveEntitlements.AsNoTracking()
            .Include(e => e.Transactions)
            .Include(e => e.LeaveType)
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId &&
                                      e.LeaveTypeId == leaveTypeId &&
                                      e.LeaveYearStart <= on && e.LeaveYearEnd >= on,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<List<LeaveEntitlement>> GetBalancesAsync(
        Guid employeeId, DateOnly on, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LeaveView);

        return _context.LeaveEntitlements.AsNoTracking()
            .Include(e => e.Transactions)
            .Include(e => e.LeaveType)
            .Where(e => e.EmployeeId == employeeId &&
                        e.LeaveYearStart <= on && e.LeaveYearEnd >= on)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Records an adjustment to an entitlement, such as an opening balance brought forward or a
    /// correction. Adjustments are ledger rows with a reason, never edits to a stored balance.
    /// </summary>
    public async Task<ValidationResult> AdjustAsync(
        Guid entitlementId, decimal days, LeaveTransactionType type, string reason,
        CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LeaveEdit);

        var validation = ValidationResult.Success();
        validation.Require(reason, "Reason", "An adjustment needs a reason.");
        validation.AddIf(
            type is LeaveTransactionType.Taken or LeaveTransactionType.Reversal, "Type",
            "Days taken and reversals are posted by approving or withdrawing a leave request, " +
            "not by adjustment.");

        var entitlement = await _context.LeaveEntitlements
            .FirstOrDefaultAsync(e => e.Id == entitlementId, cancellationToken).ConfigureAwait(false);

        if (entitlement is null)
        {
            return validation.Add("Entitlement", "Leave entitlement not found.");
        }

        if (!validation.IsValid)
        {
            return validation;
        }

        _context.LeaveTransactions.Add(new LeaveTransaction
        {
            LeaveEntitlementId = entitlement.Id,
            EmployeeId = entitlement.EmployeeId,
            LeaveTypeId = entitlement.LeaveTypeId,
            TransactionType = type,
            Days = days,
            TransactionDate = DateOnly.FromDateTime(_clock.Now.Date),
            Reason = reason
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// Finds or creates the entitlement record for the leave year containing this date. The
    /// entitlement days are copied from the leave type — including a null, which means the
    /// entitlement is undetermined and the balance must say so.
    /// </summary>
    private async Task<LeaveEntitlement> EnsureEntitlementAsync(
        Guid companyId, Guid employeeId, Guid leaveTypeId, DateOnly on,
        CancellationToken cancellationToken)
    {
        var existing = await _context.LeaveEntitlements
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId &&
                                      e.LeaveTypeId == leaveTypeId &&
                                      e.LeaveYearStart <= on && e.LeaveYearEnd >= on,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var leaveType = await _context.LeaveTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == leaveTypeId, cancellationToken).ConfigureAwait(false);

        var entitlement = new LeaveEntitlement
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            LeaveYearStart = new DateOnly(on.Year, 1, 1),
            LeaveYearEnd = new DateOnly(on.Year, 12, 31),
            EntitlementDays = leaveType?.EntitlementDays
        };

        _context.LeaveEntitlements.Add(entitlement);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entitlement;
    }

    // ---- Queries ---------------------------------------------------------------------------

    public Task<List<LeaveRequest>> GetApprovalQueueAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LeaveView);

        return _context.LeaveRequests.AsNoTracking()
            .Include(r => r.LeaveType)
            .Where(r => r.CompanyId == companyId &&
                        r.ApprovalStatus == InputApprovalStatus.Submitted)
            .OrderBy(r => r.StartDate)
            .ToListAsync(cancellationToken);
    }

    public Task<List<LeaveRequest>> GetRequestsAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.LeaveView);

        return _context.LeaveRequests.AsNoTracking()
            .Include(r => r.LeaveType)
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.StartDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Counts the leave days between two dates using the calendar. This is a calendar lookup, not
    /// a payroll calculation: it decides how many days are claimed, never what a day is worth.
    /// </summary>
    private async Task<decimal> CountLeaveDaysAsync(
        LeaveType leaveType, Guid? calendarId, DateOnly start, DateOnly end,
        CancellationToken cancellationToken)
    {
        if (end < start)
        {
            return 0m;
        }

        if (leaveType.IncludesNonWorkingDays)
        {
            return end.DayNumber - start.DayNumber + 1;
        }

        var holidays = calendarId is null
            ? new HashSet<DateOnly>()
            : (await _context.PublicHolidays.AsNoTracking()
                .Where(h => h.HolidayCalendarId == calendarId && h.IsActive &&
                            h.Date >= start && h.Date <= end)
                .Select(h => h.Date)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)).ToHashSet();

        var days = 0m;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            if (holidays.Contains(date))
            {
                continue;
            }

            days++;
        }

        return days;
    }

    private Task<Domain.Calendars.HolidayCalendar?> DefaultCalendarAsync(
        Guid companyId, DateOnly on, CancellationToken cancellationToken) =>
        _context.HolidayCalendars.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.IsActive && c.IsDefault &&
                        c.EffectiveFrom <= on && (c.EffectiveTo == null || c.EffectiveTo >= on))
            .FirstOrDefaultAsync(cancellationToken);
}
