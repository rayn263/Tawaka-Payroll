using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Release;
using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Loans;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Domain.Statutory.Obligations;

namespace Tawaka.Application.Administration;

public sealed record DashboardWarning(string Message, string Tone, string? Link, string? Action = null);

public sealed record CurrencyTotalRow(string CurrencyCode, int Employees, decimal Amount)
{
    public string CurrencyLabel => CurrencyCode == "ZWG" ? "ZiG" : CurrencyCode;
}

public sealed record PayrollActivityRow(
    DateTimeOffset At, string PeriodName, int RunNumber, string Status, string? Actor);

public sealed record OutstandingObligationRow(
    string CurrencyCode, int Count, decimal Outstanding, int Overdue)
{
    public string CurrencyLabel => CurrencyCode == "ZWG" ? "ZiG" : CurrencyCode;
}

/// <summary>
/// Everything the dashboard shows, computed from the database.
/// <para>
/// There is no sample or placeholder figure anywhere in here. A dashboard that shows a plausible
/// number nobody computed is worse than an empty one: it gets believed.
/// </para>
/// </summary>
public sealed record DashboardData
{
    public required ReleaseReadiness Readiness { get; init; }

    public string? CompanyName { get; init; }

    public int EmployeesTotal { get; init; }
    public int EmployeesActive { get; init; }
    public int ProjectsActive { get; init; }

    public PayrollPeriod? CurrentPeriod { get; init; }
    public PayrollRun? LatestRun { get; init; }

    public int RunsAwaitingApproval { get; init; }
    public int RunsLocked { get; init; }

    public int TimesheetsAwaitingApproval { get; init; }
    public int LeaveAwaitingApproval { get; init; }
    public int LoansAwaitingApproval { get; init; }

    public int UnapprovedInputsInCurrentPeriod { get; init; }

    public IReadOnlyList<CurrencyTotalRow> ContractedBasicByCurrency { get; init; } =
        Array.Empty<CurrencyTotalRow>();

    public IReadOnlyList<OutstandingObligationRow> OutstandingObligations { get; init; } =
        Array.Empty<OutstandingObligationRow>();

    public IReadOnlyList<PayrollActivityRow> RecentActivity { get; init; } =
        Array.Empty<PayrollActivityRow>();

    public IReadOnlyList<DashboardWarning> Warnings { get; init; } =
        Array.Empty<DashboardWarning>();

    public int PendingApprovals =>
        TimesheetsAwaitingApproval + LeaveAwaitingApproval + LoansAwaitingApproval;
}

public sealed class DashboardService
{
    private readonly IPayrollDataContext _context;
    private readonly ReleaseReadinessService _readiness;
    private readonly UserDirectory _directory;

    public DashboardService(
        IPayrollDataContext context, ReleaseReadinessService readiness, UserDirectory directory)
    {
        _context = context;
        _readiness = readiness;
        _directory = directory;
    }

    public async Task<DashboardData> BuildAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        var readiness = await _readiness.EvaluateAsync(cancellationToken).ConfigureAwait(false);

        var company = await _context.Companies.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var employees = await _context.Employees.AsNoTracking()
            .Select(e => new { e.Id, e.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var contracts = await _context.EmployeeContracts.AsNoTracking()
            .Where(c => c.IsCurrent)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The period in force today, or the most recent one if today falls outside every period.
        var currentPeriod = await _context.PayrollPeriods.AsNoTracking()
            .Where(p => p.StartDate <= today && p.EndDate >= today)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? await _context.PayrollPeriods.AsNoTracking()
                .OrderByDescending(p => p.StartDate)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var latestRun = await _context.PayrollRuns.AsNoTracking()
            .Include(r => r.PayrollPeriod)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var runsAwaitingApproval = await _context.PayrollRuns.AsNoTracking()
            .CountAsync(r => r.Status == PayrollRunStatus.Review, cancellationToken)
            .ConfigureAwait(false);

        var runsLocked = await _context.PayrollRuns.AsNoTracking()
            .CountAsync(r => r.Status == PayrollRunStatus.Locked, cancellationToken)
            .ConfigureAwait(false);

        var timesheetsPending = await _context.Timesheets.AsNoTracking()
            .CountAsync(t => t.ApprovalStatus == InputApprovalStatus.Submitted, cancellationToken)
            .ConfigureAwait(false);

        var leavePending = await _context.LeaveRequests.AsNoTracking()
            .CountAsync(r => r.ApprovalStatus == InputApprovalStatus.Submitted, cancellationToken)
            .ConfigureAwait(false);

        var loansPending = await _context.EmployeeLoans.AsNoTracking()
            .CountAsync(l => l.ApprovalStatus == InputApprovalStatus.Submitted, cancellationToken)
            .ConfigureAwait(false);

        // Inputs that exist for the current period but are not yet approved: these are exactly the
        // ones a payroll run would skip, so naming them now prevents an employee being underpaid.
        var unapprovedInPeriod = currentPeriod is null
            ? 0
            : await _context.Timesheets.AsNoTracking()
                .CountAsync(t => t.PayrollPeriodId == currentPeriod.Id &&
                                 t.ApprovalStatus != InputApprovalStatus.Approved &&
                                 t.ApprovalStatus != InputApprovalStatus.Locked, cancellationToken)
                .ConfigureAwait(false);

        var obligations = await _context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Where(o => o.IsApproved || o.IsCalculated)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var outstanding = obligations
            .Where(o => o.Outstanding.Amount > 0m)
            .GroupBy(o => o.CurrencyCode)
            .Select(g => new OutstandingObligationRow(
                g.Key,
                g.Count(),
                g.Sum(o => o.Outstanding.Amount),
                g.Count(o => o.StatusOn(today) == StatutoryObligationStatus.Overdue)))
            .OrderBy(r => r.CurrencyCode)
            .ToList();

        var runs = await _context.PayrollRuns.AsNoTracking()
            .Include(r => r.PayrollPeriod)
            .OrderByDescending(r => r.CreatedAt)
            .Take(8)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var actorIds = runs
            .SelectMany(r => new[] { r.LockedBy, r.FinalisedBy, r.ApprovedBy, r.CalculatedBy })
            .ToList();
        var actors = await _directory.ResolveAsync(actorIds, cancellationToken).ConfigureAwait(false);

        var activity = runs.Select(run =>
        {
            var actorId = run.LockedBy ?? run.PaidBy ?? run.FinalisedBy ?? run.ApprovedBy
                          ?? run.CalculatedBy;
            var at = run.LockedAt ?? run.FinalisedAt ?? run.ApprovedAt ?? run.CalculatedAt
                     ?? run.CreatedAt;

            return new PayrollActivityRow(
                at,
                run.PayrollPeriod?.Name ?? "Payroll run",
                run.RunNumber,
                run.Status.ToString(),
                actorId is not null && actors.TryGetValue(actorId, out var name) ? name : null);
        }).ToList();

        var warnings = await BuildWarningsAsync(
            readiness, employees.Count(e => e.Status == EmployeeStatus.Active), contracts,
            today, currentPeriod, unapprovedInPeriod, outstanding, cancellationToken)
            .ConfigureAwait(false);

        return new DashboardData
        {
            Readiness = readiness,
            CompanyName = company?.LegalName,
            EmployeesTotal = employees.Count,
            EmployeesActive = employees.Count(e => e.Status == EmployeeStatus.Active),
            ProjectsActive = await _context.Projects.AsNoTracking()
                .CountAsync(p => p.Status == Domain.Organisation.ProjectStatus.Active, cancellationToken)
                .ConfigureAwait(false),
            CurrentPeriod = currentPeriod,
            LatestRun = latestRun,
            RunsAwaitingApproval = runsAwaitingApproval,
            RunsLocked = runsLocked,
            TimesheetsAwaitingApproval = timesheetsPending,
            LeaveAwaitingApproval = leavePending,
            LoansAwaitingApproval = loansPending,
            UnapprovedInputsInCurrentPeriod = unapprovedInPeriod,
            ContractedBasicByCurrency = contracts
                .GroupBy(c => c.PayrollCurrency)
                .Select(g => new CurrencyTotalRow(g.Key, g.Count(), g.Sum(c => c.PrimaryRate ?? 0m)))
                .OrderBy(r => r.CurrencyCode)
                .ToList(),
            OutstandingObligations = outstanding,
            RecentActivity = activity,
            Warnings = warnings
        };
    }

    private async Task<List<DashboardWarning>> BuildWarningsAsync(
        ReleaseReadiness readiness, int activeEmployees, List<EmployeeContract> contracts,
        DateOnly today, PayrollPeriod? currentPeriod, int unapprovedInPeriod,
        IReadOnlyList<OutstandingObligationRow> outstanding, CancellationToken cancellationToken)
    {
        var warnings = new List<DashboardWarning>();

        if (readiness.RulesUnverified > 0)
        {
            warnings.Add(new DashboardWarning(
                $"{readiness.RulesUnverified} of {readiness.RulesTotal} statutory rules are not " +
                "verified. Live payroll is blocked until each is confirmed against its official source.",
                "error", "statutory", "Review rules"));
        }

        var overdue = outstanding.Sum(o => o.Overdue);
        if (overdue > 0)
        {
            warnings.Add(new DashboardWarning(
                $"{overdue} statutory obligations are past their due date and not fully paid.",
                "error", "statutory", "Open obligations"));
        }
        else if (outstanding.Count > 0)
        {
            warnings.Add(new DashboardWarning(
                $"{outstanding.Sum(o => o.Count)} statutory obligations are outstanding across " +
                $"{outstanding.Count} currencies.",
                "warning", "statutory", "Open obligations"));
        }

        if (unapprovedInPeriod > 0 && currentPeriod is not null)
        {
            warnings.Add(new DashboardWarning(
                $"{unapprovedInPeriod} timesheets for {currentPeriod.Name} have not been approved. " +
                "Payroll will not consume them, and the employees concerned may be underpaid.",
                "error", "time-leave", "Open approvals"));
        }

        var withoutContract = activeEmployees - contracts.Count;
        if (withoutContract > 0)
        {
            warnings.Add(new DashboardWarning(
                $"{withoutContract} active employees have no current contract and cannot be paid.",
                "error", "employees", "Open employees"));
        }

        var soon = today.AddDays(30);
        var expiring = contracts.Count(c => c.EndDate.HasValue && c.EndDate.Value <= soon);
        if (expiring > 0)
        {
            warnings.Add(new DashboardWarning(
                $"{expiring} contracts expire within 30 days.", "warning", "employees",
                "Open employees"));
        }

        // Casual engagement: the Labour Act s.12(3) deeming threshold. The system warns; it never
        // reclassifies (ADR-014).
        var casualWarnings = await CountCasualEngagementWarningsAsync(today, cancellationToken)
            .ConfigureAwait(false);
        if (casualWarnings > 0)
        {
            warnings.Add(new DashboardWarning(
                $"{casualWarnings} casual employees are approaching or past the Labour Act s.12(3) " +
                "engagement threshold. Seek advice — the system does not reclassify anybody.",
                "warning", "time-leave", "Open timesheets"));
        }

        var missingTax = await _context.Employees.AsNoTracking()
            .CountAsync(e => e.Status == EmployeeStatus.Active &&
                             !_context.EmployeeStatutoryProfiles
                                 .Any(p => p.EmployeeId == e.Id && p.TaxNumber != null),
                cancellationToken)
            .ConfigureAwait(false);

        if (missingTax > 0)
        {
            warnings.Add(new DashboardWarning(
                $"{missingTax} active employees have no tax number recorded.",
                "warning", "employees", "Open employees"));
        }

        return warnings;
    }

    /// <summary>
    /// Counts casual employees whose approved engagement over the rolling four months is at or past
    /// the warning point for their employment type.
    /// </summary>
    private async Task<int> CountCasualEngagementWarningsAsync(
        DateOnly today, CancellationToken cancellationToken)
    {
        var thresholds = await _context.EmploymentTypes.AsNoTracking()
            .Where(t => t.EngagementWarningDays != null)
            .ToDictionaryAsync(t => t.Id, t => t.EngagementWarningDays!.Value, cancellationToken)
            .ConfigureAwait(false);

        if (thresholds.Count == 0)
        {
            return 0;
        }

        var windowStart = today.AddMonths(-4).AddDays(1);

        var contracts = await _context.EmployeeContracts.AsNoTracking()
            .Where(c => c.IsCurrent && thresholds.Keys.Contains(c.EmploymentTypeId))
            .Select(c => new { c.EmployeeId, c.EmploymentTypeId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (contracts.Count == 0)
        {
            return 0;
        }

        var employeeIds = contracts.Select(c => c.EmployeeId).ToList();

        var engagement = await _context.TimeEntries.AsNoTracking()
            .Where(e => !e.IsAbsence && e.WorkDate >= windowStart && e.WorkDate <= today)
            .Where(e => _context.Timesheets.Any(t =>
                t.Id == e.TimesheetId &&
                employeeIds.Contains(t.EmployeeId) &&
                (t.ApprovalStatus == InputApprovalStatus.Approved ||
                 t.ApprovalStatus == InputApprovalStatus.Locked)))
            .Select(e => new { e.TimesheetId, e.WorkDate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sheets = await _context.Timesheets.AsNoTracking()
            .Where(t => employeeIds.Contains(t.EmployeeId))
            .Select(t => new { t.Id, t.EmployeeId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byEmployee = engagement
            .Join(sheets, e => e.TimesheetId, s => s.Id, (e, s) => new { s.EmployeeId, e.WorkDate })
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.WorkDate).Distinct().Count());

        return contracts.Count(contract =>
        {
            if (!byEmployee.TryGetValue(contract.EmployeeId, out var days))
            {
                return false;
            }

            var threshold = thresholds[contract.EmploymentTypeId];
            var warnAt = (int)Math.Floor(threshold * 5m / 6m);
            return days >= warnAt;
        });
    }
}
