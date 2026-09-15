using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Security;
using Tawaka.Domain.Common;
using Tawaka.Domain.Security;
using Tawaka.Domain.Time;

namespace Tawaka.Application.Time;

public sealed record TimeEntryRequest
{
    public required DateOnly WorkDate { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? ProjectSiteId { get; init; }
    public decimal OrdinaryHours { get; init; }
    public decimal DaysWorked { get; init; }
    public bool IsAbsence { get; init; }
    public Guid? LeaveRequestId { get; init; }
    public string? Notes { get; init; }

    /// <summary>Overtime hours by category code, e.g. {"OT_SUNDAY": 6}.</summary>
    public IReadOnlyDictionary<string, decimal> Overtime { get; init; } =
        new Dictionary<string, decimal>();
}

/// <summary>
/// Captures and approves attendance.
/// <para>
/// Nothing in this service works out what time is worth. It records hours, days, projects and
/// approvals; pricing happens in the engine, from dated overtime rules, so that there is one
/// implementation of overtime pay and not a second one hidden in a timesheet screen (ADR-031).
/// </para>
/// </summary>
public sealed class TimesheetService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public TimesheetService(IPayrollDataContext context, ICurrentUser currentUser, IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<OperationResult<Timesheet>> CreateAsync(
        Guid employeeId, Guid payrollPeriodId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.TimeEdit);

        var validation = ValidationResult.Success();

        var period = await _context.PayrollPeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == payrollPeriodId, cancellationToken)
            .ConfigureAwait(false);

        if (period is null)
        {
            return OperationResult<Timesheet>.Failed(
                validation.Add("Period", "Payroll period not found."));
        }

        validation.AddIf(period.IsLocked, "Period",
            "This payroll period is locked; time cannot be captured against it.");

        var employee = await _context.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null)
        {
            return OperationResult<Timesheet>.Failed(
                validation.Add("Employee", "Employee not found."));
        }

        // One live timesheet per employee and period. A second would either double-pay the hours
        // or leave the run picking between two without a rule for which.
        var existing = await _context.Timesheets.AsNoTracking()
            .AnyAsync(t => t.EmployeeId == employeeId &&
                           t.PayrollPeriodId == payrollPeriodId &&
                           t.CorrectsTimesheetId == null &&
                           t.ApprovalStatus != InputApprovalStatus.Rejected,
                cancellationToken)
            .ConfigureAwait(false);

        validation.AddIf(existing, "Timesheet",
            "This employee already has a timesheet for this period. Correct that one rather than " +
            "creating a second.");

        if (!validation.IsValid)
        {
            return OperationResult<Timesheet>.Failed(validation);
        }

        var timesheet = new Timesheet
        {
            CompanyId = employee.CompanyId,
            EmployeeId = employeeId,
            PayrollPeriodId = payrollPeriodId,
            PeriodStart = period.StartDate,
            PeriodEnd = period.EndDate,
            ApprovalStatus = InputApprovalStatus.Draft
        };

        _context.Timesheets.Add(timesheet);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<Timesheet>.Success(timesheet);
    }

    /// <summary>
    /// Replaces the entries on a draft or returned timesheet. Submitted, approved and locked
    /// timesheets are not editable: that is the point of approving one.
    /// </summary>
    public async Task<ValidationResult> SetEntriesAsync(
        Guid timesheetId, IReadOnlyList<TimeEntryRequest> entries,
        CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.TimeEdit);

        var validation = ValidationResult.Success();
        var timesheet = await _context.Timesheets
            .Include(t => t.Entries).ThenInclude(e => e.OvertimeLines)
            .FirstOrDefaultAsync(t => t.Id == timesheetId, cancellationToken).ConfigureAwait(false);

        if (timesheet is null)
        {
            return validation.Add("Timesheet", "Timesheet not found.");
        }

        if (!InputApprovalTransitions.CanEdit(timesheet.ApprovalStatus))
        {
            return validation.Add("Timesheet",
                $"A {timesheet.ApprovalStatus} timesheet cannot be edited. Return it for " +
                "correction, or raise a correction timesheet.");
        }

        Validate(entries, timesheet, validation);
        if (!validation.IsValid)
        {
            return validation;
        }

        var projects = await _context.Projects.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken).ConfigureAwait(false);
        var sites = await _context.ProjectSites.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken).ConfigureAwait(false);
        var holidays = await HolidayDatesAsync(timesheet, cancellationToken).ConfigureAwait(false);

        _context.TimeEntries.RemoveRange(timesheet.Entries);

        foreach (var request in entries.OrderBy(e => e.WorkDate))
        {
            var entry = new TimeEntry
            {
                TimesheetId = timesheet.Id,
                WorkDate = request.WorkDate,
                ProjectId = request.ProjectId,
                ProjectSiteId = request.ProjectSiteId,
                ProjectName = request.ProjectId is { } pid && projects.TryGetValue(pid, out var pname)
                    ? pname
                    : null,
                ProjectSiteName = request.ProjectSiteId is { } sid && sites.TryGetValue(sid, out var sname)
                    ? sname
                    : null,
                OrdinaryHours = request.OrdinaryHours,
                DaysWorked = request.DaysWorked,
                IsAbsence = request.IsAbsence,
                LeaveRequestId = request.LeaveRequestId,
                Notes = request.Notes,
                IsPublicHoliday = holidays.ContainsKey(request.WorkDate),
                PublicHolidayId = holidays.TryGetValue(request.WorkDate, out var holidayId)
                    ? holidayId
                    : null
            };

            foreach (var (code, hours) in request.Overtime.Where(o => o.Value != 0m))
            {
                entry.OvertimeLines.Add(new TimeEntryOvertimeLine
                {
                    OvertimeCategoryCode = code,
                    Hours = hours
                });
            }

            _context.TimeEntries.Add(entry);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public async Task<ValidationResult> SubmitAsync(
        Guid timesheetId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.TimeEdit);

        var validation = ValidationResult.Success();
        var timesheet = await _context.Timesheets
            .Include(t => t.Entries)
            .FirstOrDefaultAsync(t => t.Id == timesheetId, cancellationToken).ConfigureAwait(false);

        if (timesheet is null)
        {
            return validation.Add("Timesheet", "Timesheet not found.");
        }

        validation.AddIf(!InputApprovalTransitions.CanSubmit(timesheet.ApprovalStatus), "Timesheet",
            $"A {timesheet.ApprovalStatus} timesheet cannot be submitted.");
        validation.AddIf(timesheet.Entries.Count == 0, "Timesheet",
            "An empty timesheet cannot be submitted. If the employee did not work, say so with " +
            "zero-hour entries or an approved absence, so the record is deliberate.");

        if (!validation.IsValid)
        {
            return validation;
        }

        timesheet.ApprovalStatus = InputApprovalStatus.Submitted;
        timesheet.SubmittedBy = _currentUser.UserId;
        timesheet.SubmittedAt = _clock.Now;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// Approves a timesheet. Whoever submitted it cannot approve it: the whole value of approval is
    /// that a second person looked.
    /// </summary>
    public async Task<ValidationResult> ApproveAsync(
        Guid timesheetId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.TimeApprove);

        var validation = ValidationResult.Success();
        var timesheet = await _context.Timesheets
            .FirstOrDefaultAsync(t => t.Id == timesheetId, cancellationToken).ConfigureAwait(false);

        if (timesheet is null)
        {
            return validation.Add("Timesheet", "Timesheet not found.");
        }

        validation.AddIf(!InputApprovalTransitions.CanDecide(timesheet.ApprovalStatus), "Timesheet",
            $"Only a submitted timesheet can be approved. This one is {timesheet.ApprovalStatus}.");

        validation.AddIf(
            timesheet.SubmittedBy is not null && timesheet.SubmittedBy == _currentUser.UserId,
            "Timesheet",
            "Segregation of duties: the user who submitted a timesheet cannot approve it.");

        if (!validation.IsValid)
        {
            return validation;
        }

        timesheet.ApprovalStatus = InputApprovalStatus.Approved;
        timesheet.ApprovedBy = _currentUser.UserId;
        timesheet.ApprovedAt = _clock.Now;
        timesheet.DecisionReason = null;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public async Task<ValidationResult> RejectAsync(
        Guid timesheetId, string reason, CancellationToken cancellationToken = default)
    {
        return await DecideAsync(timesheetId, InputApprovalStatus.Rejected, reason,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ValidationResult> ReturnAsync(
        Guid timesheetId, string reason, CancellationToken cancellationToken = default)
    {
        return await DecideAsync(timesheetId, InputApprovalStatus.Returned, reason,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ValidationResult> DecideAsync(
        Guid timesheetId, InputApprovalStatus decision, string reason,
        CancellationToken cancellationToken)
    {
        _currentUser.Require(Permissions.TimeApprove);

        var validation = ValidationResult.Success();
        validation.Require(reason, "Reason",
            "A reason is required: the person who has to fix the timesheet needs to know what is wrong.");

        var timesheet = await _context.Timesheets
            .FirstOrDefaultAsync(t => t.Id == timesheetId, cancellationToken).ConfigureAwait(false);

        if (timesheet is null)
        {
            return validation.Add("Timesheet", "Timesheet not found.");
        }

        var allowed = decision == InputApprovalStatus.Rejected
            ? InputApprovalTransitions.CanDecide(timesheet.ApprovalStatus)
            : InputApprovalTransitions.CanReturn(timesheet.ApprovalStatus);

        validation.AddIf(!allowed, "Timesheet",
            $"A {timesheet.ApprovalStatus} timesheet cannot be {decision.ToString().ToLowerInvariant()}.");

        if (!validation.IsValid)
        {
            return validation;
        }

        timesheet.ApprovalStatus = decision;
        timesheet.DecisionReason = reason;
        timesheet.ApprovedBy = null;
        timesheet.ApprovedAt = null;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// Marks approved timesheets as consumed by a payroll run. From this point they are Locked:
    /// altering them would change what a calculated run was based on.
    /// </summary>
    public async Task LockForRunAsync(
        IReadOnlyList<Guid> timesheetIds, Guid payrollRunId,
        CancellationToken cancellationToken = default)
    {
        if (timesheetIds.Count == 0)
        {
            return;
        }

        var timesheets = await _context.Timesheets
            .Where(t => timesheetIds.Contains(t.Id) &&
                        t.ApprovalStatus == InputApprovalStatus.Approved)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var timesheet in timesheets)
        {
            timesheet.ApprovalStatus = InputApprovalStatus.Locked;
            timesheet.ConsumedByPayrollRunId = payrollRunId;
            timesheet.ConsumedAt = _clock.Now;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Raises a correction timesheet that supersedes a locked one. The original is never edited:
    /// it is what the original run saw, and a later run has to be able to show the difference.
    /// </summary>
    public async Task<OperationResult<Timesheet>> CreateCorrectionAsync(
        Guid originalTimesheetId, string reason, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.TimeEdit);

        var validation = ValidationResult.Success();
        validation.Require(reason, "Reason", "A correction needs a reason.");

        var original = await _context.Timesheets.AsNoTracking()
            .Include(t => t.Entries).ThenInclude(e => e.OvertimeLines)
            .FirstOrDefaultAsync(t => t.Id == originalTimesheetId, cancellationToken)
            .ConfigureAwait(false);

        if (original is null)
        {
            return OperationResult<Timesheet>.Failed(
                validation.Add("Timesheet", "Timesheet not found."));
        }

        validation.AddIf(original.ApprovalStatus is InputApprovalStatus.Draft
                or InputApprovalStatus.Returned, "Timesheet",
            "This timesheet is still editable; correct it directly rather than superseding it.");

        if (!validation.IsValid)
        {
            return OperationResult<Timesheet>.Failed(validation);
        }

        var correction = new Timesheet
        {
            CompanyId = original.CompanyId,
            EmployeeId = original.EmployeeId,
            PayrollPeriodId = original.PayrollPeriodId,
            PeriodStart = original.PeriodStart,
            PeriodEnd = original.PeriodEnd,
            ApprovalStatus = InputApprovalStatus.Draft,
            CorrectsTimesheetId = original.Id,
            CorrectionReason = reason
        };

        // Copied, not moved: the original keeps its own entries exactly as approved.
        foreach (var entry in original.Entries)
        {
            var copy = new TimeEntry
            {
                WorkDate = entry.WorkDate,
                ProjectId = entry.ProjectId,
                ProjectSiteId = entry.ProjectSiteId,
                ProjectName = entry.ProjectName,
                ProjectSiteName = entry.ProjectSiteName,
                OrdinaryHours = entry.OrdinaryHours,
                DaysWorked = entry.DaysWorked,
                IsAbsence = entry.IsAbsence,
                LeaveRequestId = entry.LeaveRequestId,
                IsPublicHoliday = entry.IsPublicHoliday,
                PublicHolidayId = entry.PublicHolidayId,
                Notes = entry.Notes
            };

            foreach (var line in entry.OvertimeLines)
            {
                copy.OvertimeLines.Add(new TimeEntryOvertimeLine
                {
                    OvertimeCategoryCode = line.OvertimeCategoryCode,
                    Hours = line.Hours,
                    Notes = line.Notes
                });
            }

            correction.Entries.Add(copy);
        }

        _context.Timesheets.Add(correction);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<Timesheet>.Success(correction);
    }

    public Task<List<Timesheet>> GetForPeriodAsync(
        Guid payrollPeriodId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.TimeView);

        return _context.Timesheets.AsNoTracking()
            .Include(t => t.Entries).ThenInclude(e => e.OvertimeLines)
            .Where(t => t.PayrollPeriodId == payrollPeriodId)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Timesheet>> GetApprovalQueueAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.TimeView);

        return _context.Timesheets.AsNoTracking()
            .Include(t => t.Entries).ThenInclude(e => e.OvertimeLines)
            .Where(t => t.CompanyId == companyId &&
                        t.ApprovalStatus == InputApprovalStatus.Submitted)
            .OrderBy(t => t.SubmittedAt)
            .ToListAsync(cancellationToken);
    }

    private static void Validate(
        IReadOnlyList<TimeEntryRequest> entries, Timesheet timesheet, ValidationResult validation)
    {
        foreach (var entry in entries)
        {
            var label = entry.WorkDate.ToString("dd MMM yyyy");

            validation.AddIf(!timesheet.Period.Contains(entry.WorkDate), "WorkDate",
                $"{label} falls outside the payroll period " +
                $"{timesheet.PeriodStart:dd MMM} to {timesheet.PeriodEnd:dd MMM yyyy}.");

            validation.AddIf(entry.OrdinaryHours < 0m, "OrdinaryHours",
                $"{label}: hours cannot be negative.");
            validation.AddIf(entry.DaysWorked < 0m, "DaysWorked",
                $"{label}: days cannot be negative.");
            validation.AddIf(entry.DaysWorked > 1m, "DaysWorked",
                $"{label}: more than one day cannot be worked on a single date.");

            // A day has 24 hours. A timesheet claiming more is a capture error, and catching it
            // here is far cheaper than explaining it on a payslip.
            var totalHours = entry.OrdinaryHours + entry.Overtime.Values.Sum();
            validation.AddIf(totalHours > 24m, "Hours",
                $"{label}: {totalHours:N2} hours claimed on one day.");

            foreach (var (code, hours) in entry.Overtime)
            {
                validation.AddIf(hours < 0m, "Overtime",
                    $"{label}: overtime hours for {code} cannot be negative.");
                validation.Require(code, "Overtime", $"{label}: an overtime category is required.");
            }
        }

        var duplicates = entries.GroupBy(e => e.WorkDate).Where(g => g.Count() > 1).ToList();
        foreach (var duplicate in duplicates)
        {
            validation.Add("WorkDate",
                $"{duplicate.Key:dd MMM yyyy} appears {duplicate.Count()} times. One entry per date: " +
                "split the day across projects using its project allocation, not a second row.");
        }
    }

    private async Task<Dictionary<DateOnly, Guid>> HolidayDatesAsync(
        Timesheet timesheet, CancellationToken cancellationToken)
    {
        var calendar = await _context.HolidayCalendars.AsNoTracking()
            .Where(c => c.CompanyId == timesheet.CompanyId && c.IsActive && c.IsDefault &&
                        c.EffectiveFrom <= timesheet.PeriodEnd &&
                        (c.EffectiveTo == null || c.EffectiveTo >= timesheet.PeriodStart))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (calendar is null)
        {
            return new Dictionary<DateOnly, Guid>();
        }

        var holidays = await _context.PublicHolidays.AsNoTracking()
            .Where(h => h.HolidayCalendarId == calendar.Id && h.IsActive &&
                        h.Date >= timesheet.PeriodStart && h.Date <= timesheet.PeriodEnd)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return holidays
            .GroupBy(h => h.Date)
            .ToDictionary(g => g.Key, g => g.First().Id);
    }
}
