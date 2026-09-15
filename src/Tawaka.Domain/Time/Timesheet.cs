using Tawaka.Domain.Common;

namespace Tawaka.Domain.Time;

/// <summary>
/// One employee's attendance for one payroll period, and the unit that gets approved.
/// <para>
/// A timesheet is a payroll <em>input</em>, not a payroll calculation. It records hours and days
/// against dates, projects and sites. What those hours are worth is decided by the engine, from
/// the contract rate and the dated overtime rules — never here (ADR-031).
/// </para>
/// </summary>
public class Timesheet : AuditableEntity, IApprovableInput, ILockable
{
    public Guid CompanyId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>
    /// The period this attendance belongs to. A timesheet is scoped to a payroll period so that
    /// "which time did this run consume" has an exact answer.
    /// </summary>
    public Guid PayrollPeriodId { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public InputApprovalStatus ApprovalStatus { get; set; } = InputApprovalStatus.Draft;

    public string? SubmittedBy { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? DecisionReason { get; set; }

    /// <summary>Set when a payroll run consumed this timesheet, naming the run that did.</summary>
    public Guid? ConsumedByPayrollRunId { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>
    /// A correction timesheet supersedes an earlier one rather than editing it. The original stays
    /// exactly as the run that consumed it saw it.
    /// </summary>
    public Guid? CorrectsTimesheetId { get; set; }
    public string? CorrectionReason { get; set; }

    public string? Notes { get; set; }

    public ICollection<TimeEntry> Entries { get; set; } = new List<TimeEntry>();

    public DateRange Period => new(PeriodStart, PeriodEnd);

    public bool IsAvailableToPayroll =>
        InputApprovalTransitions.IsAvailableToPayroll(ApprovalStatus);

    public bool IsLocked => ApprovalStatus == InputApprovalStatus.Locked;

    public bool IsCorrection => CorrectsTimesheetId is not null;

    public decimal TotalOrdinaryHours => Entries.Sum(e => e.OrdinaryHours);

    public decimal TotalDaysWorked => Entries.Sum(e => e.DaysWorked);

    /// <summary>
    /// Overtime is not one number. Each category has its own rule and its own multiplier, so they
    /// are kept apart all the way into the snapshot.
    /// </summary>
    public IReadOnlyDictionary<string, decimal> OvertimeHoursByCategory =>
        Entries.SelectMany(e => e.OvertimeLines)
            .GroupBy(l => l.OvertimeCategoryCode)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Hours));

    public decimal TotalOvertimeHours => Entries.SelectMany(e => e.OvertimeLines).Sum(l => l.Hours);
}

/// <summary>One day of one timesheet, allocated to a project and site.</summary>
public class TimeEntry : Entity
{
    public Guid TimesheetId { get; set; }
    public Timesheet? Timesheet { get; set; }

    public DateOnly WorkDate { get; set; }

    public Guid? ProjectId { get; set; }
    public Guid? ProjectSiteId { get; set; }

    /// <summary>Snapshotted so a later project rename does not rewrite an approved timesheet.</summary>
    public string? ProjectName { get; set; }
    public string? ProjectSiteName { get; set; }

    /// <summary>Hours at the ordinary rate.</summary>
    public decimal OrdinaryHours { get; set; }

    /// <summary>
    /// Days worked, for employees paid by the day. Kept separate from hours rather than derived
    /// from them: a day is a unit of engagement, not a count of hours, and the NSSA casual test
    /// counts days.
    /// </summary>
    public decimal DaysWorked { get; set; }

    /// <summary>True where this date fell on a public holiday in the applicable calendar.</summary>
    public bool IsPublicHoliday { get; set; }

    /// <summary>The calendar entry that made it a public holiday, so the claim is evidenced.</summary>
    public Guid? PublicHolidayId { get; set; }

    public bool IsAbsence { get; set; }

    /// <summary>Set where the absence is covered by an approved leave request.</summary>
    public Guid? LeaveRequestId { get; set; }

    public string? Notes { get; set; }

    public ICollection<TimeEntryOvertimeLine> OvertimeLines { get; set; } =
        new List<TimeEntryOvertimeLine>();

    public decimal TotalOvertimeHours => OvertimeLines.Sum(l => l.Hours);
}

/// <summary>
/// Hours claimed in one overtime category on one day.
/// <para>
/// The category is a code, not a multiplier. The multiplier lives in a dated, graded
/// <c>OvertimeRule</c>, so an unverified overtime rate refuses rather than quietly paying 1.5×.
/// </para>
/// </summary>
public class TimeEntryOvertimeLine : Entity
{
    public Guid TimeEntryId { get; set; }

    public string OvertimeCategoryCode { get; set; } = string.Empty;

    public decimal Hours { get; set; }

    public string? Notes { get; set; }
}
