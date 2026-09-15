using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Loans;
using Tawaka.Application.Statutory;
using Tawaka.Domain.Calendars;
using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Inputs;
using Tawaka.Payroll.Engine.Results;

namespace Tawaka.Application.Payroll;

/// <summary>
/// Assembles the immutable <see cref="PayrollInputSnapshot"/> the engine calculates from.
/// <para>
/// This is the only place that reads the database for a calculation. It resolves the contract that
/// applied on the pay date — not the current one — and the statutory rules effective then, so a
/// past period recalculates to the same figures. Rules that fail to resolve are carried into the
/// snapshot as unresolved items rather than dropped, so the engine can refuse with a reason.
/// </para>
/// </summary>
public sealed class PayrollSnapshotBuilder
{
    private readonly IPayrollDataContext _context;
    private readonly IStatutoryRuleResolver _resolver;
    private readonly LoanService _loans;

    public PayrollSnapshotBuilder(
        IPayrollDataContext context, IStatutoryRuleResolver resolver, LoanService loans)
    {
        _context = context;
        _resolver = resolver;
        _loans = loans;
    }

    public async Task<PayrollInputSnapshot?> BuildAsync(
        Guid employeeId, PayrollPeriod period, PayrollMode mode,
        CancellationToken cancellationToken = default)
    {
        var employee = await _context.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null)
        {
            return null;
        }

        // The contract that applied on the pay date, which may be an earlier version.
        var contract = await _context.EmployeeContracts.AsNoTracking()
            .Where(c => c.EmployeeId == employeeId &&
                        c.StartDate <= period.PayDate &&
                        (c.EndDate == null || c.EndDate >= period.PayDate))
            .OrderByDescending(c => c.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (contract is null)
        {
            return null;
        }

        var employmentType = await _context.EmploymentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == contract.EmploymentTypeId, cancellationToken)
            .ConfigureAwait(false);

        var profile = await _context.EmployeeStatutoryProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId, cancellationToken)
            .ConfigureAwait(false);

        var currency = new CurrencyCode(contract.PayrollCurrency);
        var earnings = await BuildEarningsAsync(employee, contract, period, currency, cancellationToken)
            .ConfigureAwait(false);
        var deductions = await BuildDeductionsAsync(employeeId, period, cancellationToken)
            .ConfigureAwait(false);
        var allocations = await BuildAllocationsAsync(employeeId, contract, period, cancellationToken)
            .ConfigureAwait(false);

        var approved = new List<ApprovedInputReference>();
        var skipped = new List<SkippedInput>();

        var calendar = await BuildCalendarAsync(employee.CompanyId, period, approved, cancellationToken)
            .ConfigureAwait(false);
        var timesheet = await BuildTimesheetAsync(employeeId, period, approved, skipped, cancellationToken)
            .ConfigureAwait(false);
        var leave = await BuildLeaveAsync(employeeId, period, approved, skipped, cancellationToken)
            .ConfigureAwait(false);
        var loans = await BuildLoanDeductionsAsync(employeeId, period, currency, approved, cancellationToken)
            .ConfigureAwait(false);
        var engagement = await BuildCasualEngagementAsync(employeeId, employmentType, period,
            cancellationToken).ConfigureAwait(false);

        var rules = ResolveRules(contract, employmentType, period, currency, mode,
            timesheet?.Overtime.Select(o => o.CategoryCode).ToList() ?? new List<string>());

        // The hours were captured before the rules were resolved; pair them up now so each
        // category carries the dated rule that prices it into the engine.
        if (timesheet is not null)
        {
            timesheet = timesheet with { Overtime = AttachOvertimeRules(timesheet, rules) };
        }

        // Where time was booked to projects, the cost follows the work actually done rather than a
        // standing assignment percentage: that is the whole point of capturing it per project.
        if (timesheet is { Allocations.Count: > 0 })
        {
            var fromTime = AllocateByTime(timesheet);
            if (fromTime.Count > 0)
            {
                allocations = fromTime;
            }
        }

        return new PayrollInputSnapshot
        {
            CompanyId = employee.CompanyId,
            EmployeeId = employee.Id,
            EmployeeNumber = employee.EmployeeNumber,
            EmployeeName = employee.FullName,
            PayrollPeriodId = period.Id,
            PeriodStart = period.StartDate,
            PeriodEnd = period.EndDate,
            PayDate = period.PayDate,
            PeriodBasis = period.Frequency,
            Mode = mode,
            ContractId = contract.Id,
            ContractVersion = contract.VersionNumber,
            PayrollCurrency = currency,
            EarningsBasis = contract.EarningsBasis,
            PaymentFrequency = contract.PaymentFrequency,
            EmploymentTypeCode = employmentType?.Code ?? string.Empty,
            ContractRate = contract.PrimaryRateMoney,
            StandardHoursPerDay = contract.StandardHoursPerDay,
            StandardDaysPerWeek = contract.StandardDaysPerWeek,
            Earnings = earnings,
            Deductions = deductions,
            Timesheet = timesheet,
            LeaveEffects = leave,
            LoanDeductions = loans,
            HolidayCalendar = calendar,
            CasualEngagement = engagement,
            ApprovedInputs = approved,
            SkippedInputs = skipped,
            StatutoryProfile = new StatutoryProfileInput
            {
                TaxNumber = profile?.TaxNumber,
                NssaNumber = profile?.NssaNumber,
                IsPayeExempt = profile?.IsPayeExempt ?? false,
                NssaEligibilityOverride = profile?.NssaEligibilityOverride,
                Age = employee.AgeAt(period.PayDate),
                IsElderlyCreditEligible = profile?.IsElderlyCreditEligible ?? false,
                IsDisabledCreditEligible = profile?.IsDisabledCreditEligible ?? false,
                IsBlindCreditEligible = profile?.IsBlindCreditEligible ?? false,
                MedicalAidCreditApplies = profile?.MedicalAidCreditApplies ?? false,
                IsNecMember = profile?.IsNecMember ?? false
            },
            CostAllocations = allocations,
            Rules = rules
        };
    }

    /// <summary>
    /// Basic pay from the contract, plus the recurring earnings effective in the period. Each
    /// carries its earning type's statutory treatment and verification grade.
    /// </summary>
    private async Task<List<EarningInput>> BuildEarningsAsync(
        Employee employee, EmployeeContract contract, PayrollPeriod period, CurrencyCode currency,
        CancellationToken cancellationToken)
    {
        var earnings = new List<EarningInput>();

        var basicType = await _context.EarningTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CompanyId == employee.CompanyId && t.Code == "BASIC",
                cancellationToken)
            .ConfigureAwait(false);

        // An hourly or daily contract has no basic pay until approved time says how much was
        // worked. The engine derives it from the timesheet; adding a flat period amount here would
        // pay a full period to somebody who worked three days.
        var isTimeBased = contract.EarningsBasis is EarningsBasis.HourlyRate
            or EarningsBasis.DailyRate;

        if (contract.PrimaryRate is { } rate && !isTimeBased)
        {
            earnings.Add(new EarningInput
            {
                Code = "BASIC",
                Name = basicType?.Name ?? "Basic Salary",
                Amount = new Money(rate, currency),
                IsBasic = true,
                IsTaxable = basicType?.IsTaxable ?? true,
                IsNssaApplicable = basicType?.IsNssaApplicable ?? true,
                IsIncludedInGross = basicType?.IsIncludedInGross ?? true,
                IsEmployerLevyBase = basicType?.IsEmployerLevyBase ?? true,
                TreatmentVerification = basicType?.TreatmentVerificationStatus
                                        ?? VerificationStatus.Unverified,
                Source = basicType?.TreatmentSource
            });
        }

        var recurring = await _context.EmployeeRecurringEarnings.AsNoTracking()
            .Include(e => e.EarningType)
            .Where(e => e.EmployeeId == employee.Id && e.IsActive &&
                        e.EffectiveFrom <= period.PayDate &&
                        (e.EffectiveTo == null || e.EffectiveTo >= period.PayDate))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var item in recurring)
        {
            var type = item.EarningType;
            earnings.Add(new EarningInput
            {
                Code = type?.Code ?? "OTHER",
                Name = type?.Name ?? "Other earning",
                Amount = item.AsMoney(),
                IsTaxable = type?.IsTaxable ?? true,
                IsNssaApplicable = type?.IsNssaApplicable ?? false,
                IsIncludedInGross = type?.IsIncludedInGross ?? true,
                IsEmployerLevyBase = type?.IsEmployerLevyBase ?? true,
                IsExemptUpToLimit = type?.IsExemptUpToLimit ?? false,
                IsReimbursive = type?.IsReimbursive ?? false,
                IsBonus = type?.Category == Domain.Earnings.EarningCategory.Bonus,
                IsOvertime = type?.Category == Domain.Earnings.EarningCategory.Overtime,
                TreatmentVerification = type?.TreatmentVerificationStatus
                                        ?? VerificationStatus.Unverified,
                Source = type?.TreatmentSource
            });
        }

        return earnings;
    }

    private async Task<List<DeductionInput>> BuildDeductionsAsync(
        Guid employeeId, PayrollPeriod period, CancellationToken cancellationToken)
    {
        var recurring = await _context.EmployeeRecurringDeductions.AsNoTracking()
            .Include(d => d.DeductionType)
            .Where(d => d.EmployeeId == employeeId && d.IsActive &&
                        d.EffectiveFrom <= period.PayDate &&
                        (d.EffectiveTo == null || d.EffectiveTo >= period.PayDate))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return recurring.Select(d => new DeductionInput
        {
            Code = d.DeductionType?.Code ?? "OTHER",
            Name = d.DeductionType?.Name ?? "Other deduction",
            Amount = d.AsMoney(),
            ReducesTaxableIncome = d.DeductionType?.ReducesTaxableIncome ?? false,
            AppliesBeforeTax = d.DeductionType?.AppliesBeforeTax ?? false,
            Priority = d.Priority,
            TreatmentVerification = d.DeductionType?.TreatmentVerificationStatus
                                    ?? VerificationStatus.Unverified
        }).ToList();
    }

    private async Task<List<CostAllocationInput>> BuildAllocationsAsync(
        Guid employeeId, EmployeeContract contract, PayrollPeriod period,
        CancellationToken cancellationToken)
    {
        var assignments = await _context.EmployeeProjectAssignments.AsNoTracking()
            .Include(a => a.Project)
            .Where(a => a.EmployeeId == employeeId && a.IsActive &&
                        a.StartDate <= period.PayDate &&
                        (a.EndDate == null || a.EndDate >= period.PayDate))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (assignments.Count > 0)
        {
            return assignments.Select(a => new CostAllocationInput(
                a.ProjectId, a.Project?.Name, a.ProjectSiteId, contract.DepartmentId, null,
                a.AllocationPercent)).ToList();
        }

        // With no explicit assignment, cost follows the contract's project or department in full.
        if (contract.ProjectId is not null || contract.DepartmentId is not null)
        {
            var project = contract.ProjectId is null
                ? null
                : await _context.Projects.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == contract.ProjectId, cancellationToken)
                    .ConfigureAwait(false);

            return new List<CostAllocationInput>
            {
                new(contract.ProjectId, project?.Name, contract.ProjectSiteId,
                    contract.DepartmentId, null, 100m)
            };
        }

        return new List<CostAllocationInput>();
    }

    /// <summary>
    /// Attaches each resolved overtime rule to the hours claimed in its category. A category with
    /// no resolved rule keeps its hours and carries no multiplier, so the engine refuses it by name
    /// rather than dropping the hours silently.
    /// </summary>
    private static List<OvertimeInput> AttachOvertimeRules(
        TimesheetInput timesheet, ResolvedRules rules) =>
        timesheet.Overtime.Select(input =>
        {
            var rule = rules.Overtime.FirstOrDefault(r =>
                string.Equals(r.CategoryCode, input.CategoryCode,
                    StringComparison.OrdinalIgnoreCase));

            return rule is null
                ? input
                : input with
                {
                    CategoryName = rule.CategoryName,
                    Multiplier = rule.Multiplier,
                    RuleId = rule.RuleId,
                    RuleVerification = rule.VerificationStatus,
                    IsTaxable = rule.IsTaxable,
                    IsNssaApplicable = rule.IsNssaApplicable,
                    Source = rule.Source.ToString()
                };
        }).ToList();

    // ---- Approved inputs (Milestone 5) -----------------------------------------------------

    /// <summary>
    /// The working calendar in force for this period, frozen into the snapshot with the holiday
    /// dates it contributed, so a later edit to the calendar cannot change what this run believed.
    /// </summary>
    private async Task<HolidayCalendarInput?> BuildCalendarAsync(
        Guid companyId, PayrollPeriod period, List<ApprovedInputReference> approved,
        CancellationToken cancellationToken)
    {
        var calendar = await _context.HolidayCalendars.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.IsActive && c.IsDefault &&
                        c.EffectiveFrom <= period.EndDate &&
                        (c.EffectiveTo == null || c.EffectiveTo >= period.StartDate))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (calendar is null)
        {
            return null;
        }

        var dates = await _context.PublicHolidays.AsNoTracking()
            .Where(h => h.HolidayCalendarId == calendar.Id && h.IsActive &&
                        h.Date >= period.StartDate && h.Date <= period.EndDate)
            .Select(h => h.Date)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        approved.Add(new ApprovedInputReference(
            "HolidayCalendar", calendar.Id, $"{calendar.Name} ({calendar.Code})", null, null));

        return new HolidayCalendarInput
        {
            CalendarId = calendar.Id,
            CalendarCode = calendar.Code,
            CalendarName = calendar.Name,
            HolidayDatesInPeriod = dates.OrderBy(d => d).ToList()
        };
    }

    /// <summary>
    /// The approved timesheet for this period, if there is one.
    /// <para>
    /// Payroll reads approved and locked timesheets only. Where a timesheet exists but has not been
    /// approved it is recorded as skipped, with the reason, so the preview can say so rather than
    /// leaving an employee silently unpaid.
    /// </para>
    /// </summary>
    private async Task<TimesheetInput?> BuildTimesheetAsync(
        Guid employeeId, PayrollPeriod period, List<ApprovedInputReference> approved,
        List<SkippedInput> skipped, CancellationToken cancellationToken)
    {
        var timesheets = await _context.Timesheets.AsNoTracking()
            .Include(ts => ts.Entries).ThenInclude(e => e.OvertimeLines)
            .Where(ts => ts.EmployeeId == employeeId && ts.PayrollPeriodId == period.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (timesheets.Count == 0)
        {
            return null;
        }

        foreach (var unusable in timesheets.Where(ts => !ts.IsAvailableToPayroll))
        {
            skipped.Add(new SkippedInput("Timesheet", unusable.Id,
                $"The timesheet is {unusable.ApprovalStatus} and payroll consumes approved input only."));
        }

        // A correction supersedes the timesheet it corrects, so the latest approved correction wins
        // and the original is left exactly as the run that consumed it saw it.
        var usable = timesheets
            .Where(ts => ts.IsAvailableToPayroll)
            .OrderByDescending(ts => ts.IsCorrection)
            .ThenByDescending(ts => ts.ApprovedAt)
            .FirstOrDefault();

        if (usable is null)
        {
            return null;
        }

        approved.Add(new ApprovedInputReference(
            "Timesheet", usable.Id,
            $"Timesheet {usable.PeriodStart:dd MMM} to {usable.PeriodEnd:dd MMM yyyy}" +
            (usable.IsCorrection ? " (correction)" : string.Empty),
            usable.ApprovedBy, usable.ApprovedAt));

        var overtimeByCategory = usable.Entries
            .SelectMany(e => e.OvertimeLines)
            .GroupBy(l => l.OvertimeCategoryCode)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Hours));

        var overtime = overtimeByCategory
            .Select(pair => new OvertimeInput
            {
                CategoryCode = pair.Key,
                CategoryName = pair.Key,
                Hours = pair.Value
            })
            .OrderBy(o => o.CategoryCode)
            .ToList();

        var allocations = usable.Entries
            .GroupBy(e => new { e.ProjectId, e.ProjectName, e.ProjectSiteId, e.ProjectSiteName })
            .Select(g => new TimeAllocationInput(
                g.Key.ProjectId, g.Key.ProjectName, g.Key.ProjectSiteId, g.Key.ProjectSiteName,
                g.Sum(e => e.OrdinaryHours + e.OvertimeLines.Sum(l => l.Hours)),
                g.Sum(e => e.DaysWorked)))
            .ToList();

        return new TimesheetInput
        {
            TimesheetId = usable.Id,
            DaysWorked = usable.Entries.Sum(e => e.DaysWorked),
            HoursWorked = usable.Entries.Sum(e => e.OrdinaryHours),
            OvertimeHours = overtimeByCategory.Values.Sum(),
            DaysEngagedInMonth = usable.Entries.Count(e => e.DaysWorked > 0m || e.OrdinaryHours > 0m),
            IsApproved = true,
            Overtime = overtime,
            TimeEntryIds = usable.Entries.OrderBy(e => e.WorkDate).Select(e => e.Id).ToList(),
            Allocations = allocations
        };
    }

    /// <summary>
    /// Approved leave overlapping this period. Leave reaches the engine as days and a paid flag;
    /// what an unpaid day costs is decided there, from the divisor rule.
    /// </summary>
    private async Task<List<LeaveEffectInput>> BuildLeaveAsync(
        Guid employeeId, PayrollPeriod period, List<ApprovedInputReference> approved,
        List<SkippedInput> skipped, CancellationToken cancellationToken)
    {
        var requests = await _context.LeaveRequests.AsNoTracking()
            .Include(r => r.LeaveType)
            .Where(r => r.EmployeeId == employeeId &&
                        r.StartDate <= period.EndDate && r.EndDate >= period.StartDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var effects = new List<LeaveEffectInput>();

        foreach (var request in requests)
        {
            if (!request.IsAvailableToPayroll)
            {
                if (request.ApprovalStatus is InputApprovalStatus.Submitted
                    or InputApprovalStatus.Draft)
                {
                    skipped.Add(new SkippedInput("LeaveRequest", request.Id,
                        $"Leave from {request.StartDate:dd MMM} to {request.EndDate:dd MMM yyyy} " +
                        $"is {request.ApprovalStatus} and was not applied."));
                }

                continue;
            }

            approved.Add(new ApprovedInputReference(
                "LeaveRequest", request.Id,
                $"{request.LeaveType?.Name ?? "Leave"} {request.StartDate:dd MMM} to " +
                $"{request.EndDate:dd MMM yyyy}, {request.Days:N2} day(s)",
                request.ApprovedBy, request.ApprovedAt));

            effects.Add(new LeaveEffectInput
            {
                LeaveRequestId = request.Id,
                LeaveTypeCode = request.LeaveType?.Code ?? "LEAVE",
                LeaveTypeName = request.LeaveType?.Name ?? "Leave",

                // Only the days that fall inside this period belong to this payroll. Leave that
                // spans a period boundary is split, not counted twice.
                Days = DaysWithin(request, period),
                IsPaid = request.IsPaid,
                StartDate = request.StartDate,
                EndDate = request.EndDate
            });
        }

        return effects;
    }

    /// <summary>
    /// Apportions a leave request to the part of it that falls inside this payroll period. The
    /// split is by calendar days covered, which is the only apportionment the request itself
    /// supports; a request wholly inside the period is unaffected.
    /// </summary>
    private static decimal DaysWithin(Domain.Leave.LeaveRequest request, PayrollPeriod period)
    {
        var start = request.StartDate > period.StartDate ? request.StartDate : period.StartDate;
        var end = request.EndDate < period.EndDate ? request.EndDate : period.EndDate;

        var total = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
        var inside = end.DayNumber - start.DayNumber + 1;

        if (total <= 0 || inside <= 0)
        {
            return 0m;
        }

        return inside >= total
            ? request.Days
            : Math.Round(request.Days * inside / total, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Loan recoveries due from this period, already capped at what is owed by the loan ledger.
    /// The snapshot records the balance each was capped against, so the deduction stays explicable.
    /// </summary>
    private async Task<List<LoanDeductionInput>> BuildLoanDeductionsAsync(
        Guid employeeId, PayrollPeriod period, CurrencyCode currency,
        List<ApprovedInputReference> approved, CancellationToken cancellationToken)
    {
        var due = await _loans.GetDueDeductionsAsync(
            employeeId, period.Id, period.EndDate, cancellationToken).ConfigureAwait(false);

        var deductions = new List<LoanDeductionInput>();

        foreach (var item in due)
        {
            // A USD loan is repaid in USD. Recovering it from a ZiG payroll would need a converted
            // amount and therefore an approved conversion rule, which is Q1 and still open.
            if (item.Amount.Currency != currency)
            {
                continue;
            }

            approved.Add(new ApprovedInputReference(
                "Loan", item.Loan.Id,
                $"{item.Loan.LoanNumber} instalment " +
                $"{item.Instalment?.InstalmentNumber.ToString() ?? "-"} of " +
                $"{item.Loan.InstalmentCount}",
                item.Loan.ApprovedBy, item.Loan.ApprovedAt));

            deductions.Add(new LoanDeductionInput
            {
                LoanId = item.Loan.Id,
                LoanNumber = item.Loan.LoanNumber,
                LoanTypeCode = item.Loan.Kind == Domain.Loans.LoanKind.SalaryAdvance
                    ? "ADVANCE"
                    : "LOAN",
                Amount = item.Amount,
                InstalmentId = item.Instalment?.Id,
                InstalmentNumber = item.Instalment?.InstalmentNumber,
                OutstandingBefore = item.OutstandingBefore,
                WasCappedAtOutstanding = item.WasCapped,
                OverRecoveryApproved = item.Loan.AllowsOverRecovery
            });
        }

        return deductions;
    }

    /// <summary>
    /// Counts days of engagement over a rolling four-month window, for employment types that carry
    /// a deeming threshold.
    /// <para>
    /// Counted from approved timesheets: a day the employer approved as worked is a day of
    /// engagement. Days are counted distinctly, so two entries on one date — which the timesheet
    /// rules already prevent — could not inflate the tally.
    /// </para>
    /// </summary>
    private async Task<CasualEngagementInput?> BuildCasualEngagementAsync(
        Guid employeeId, EmploymentType? employmentType, PayrollPeriod period,
        CancellationToken cancellationToken)
    {
        if (employmentType?.EngagementWarningDays is not { } threshold || threshold <= 0)
        {
            return null;
        }

        var windowStart = period.EndDate.AddMonths(-4).AddDays(1);

        var dates = await _context.TimeEntries.AsNoTracking()
            .Where(e => _context.Timesheets.Any(t =>
                t.Id == e.TimesheetId &&
                t.EmployeeId == employeeId &&
                (t.ApprovalStatus == InputApprovalStatus.Approved ||
                 t.ApprovalStatus == InputApprovalStatus.Locked)))
            .Where(e => e.WorkDate >= windowStart && e.WorkDate <= period.EndDate)
            .Where(e => !e.IsAbsence)
            .Select(e => e.WorkDate)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CasualEngagementInput
        {
            DaysEngagedInWindow = dates.Count,
            WindowStart = windowStart,
            WindowEnd = period.EndDate,
            DeemedAtDays = threshold
        };
    }

    /// <summary>
    /// Splits cost across projects in proportion to the hours and days actually booked, rather than
    /// by a standing assignment percentage.
    /// </summary>
    private static List<CostAllocationInput> AllocateByTime(TimesheetInput timesheet)
    {
        var withProject = timesheet.Allocations.Where(a => a.ProjectId is not null).ToList();
        if (withProject.Count == 0)
        {
            return new List<CostAllocationInput>();
        }

        var totalHours = withProject.Sum(a => a.Hours);
        var totalDays = withProject.Sum(a => a.Days);
        var basis = totalHours > 0m ? totalHours : totalDays;

        if (basis <= 0m)
        {
            return new List<CostAllocationInput>();
        }

        var allocations = withProject.Select(a => new CostAllocationInput(
            a.ProjectId, a.ProjectName, a.ProjectSiteId, null, null,
            Math.Round((totalHours > 0m ? a.Hours : a.Days) / basis * 100m, 2,
                MidpointRounding.AwayFromZero))).ToList();

        // Rounding each share independently can leave the total a cent either side of 100%. The
        // largest share absorbs the difference, so allocations always sum to exactly 100.
        var drift = 100m - allocations.Sum(a => a.Percent);
        if (drift != 0m)
        {
            var largest = allocations.OrderByDescending(a => a.Percent).First();
            var index = allocations.IndexOf(largest);
            allocations[index] = largest with { Percent = largest.Percent + drift };
        }

        return allocations;
    }

    /// <summary>
    /// Resolves every rule the calculation needs. A failure is recorded, never defaulted: the
    /// engine then refuses that figure and names the rule.
    /// </summary>
    private ResolvedRules ResolveRules(
        EmployeeContract contract, EmploymentType? employmentType, PayrollPeriod period,
        CurrencyCode currency, PayrollMode mode, IReadOnlyList<string> overtimeCategories)
    {
        var unresolved = new List<UnresolvedItem>();

        TRule? Resolve<TRule>(StatutoryRuleType type, PeriodBasis? basis = null,
            string? discriminator = null) where TRule : StatutoryRule
        {
            var resolution = _resolver.Resolve<TRule>(new StatutoryRuleQuery
            {
                RuleType = type,
                EffectiveDate = period.PayDate,
                Currency = RequiresCurrency(type) ? currency : null,
                PeriodBasis = basis,
                Mode = mode,
                Discriminator = discriminator,
                Employee = new EmployeeRuleContext
                {
                    EmploymentTypeCode = employmentType?.Code,
                    IndustryCode = null
                }
            });

            if (resolution.Succeeded)
            {
                return resolution.Rule;
            }

            var failure = resolution.Failure!;
            unresolved.Add(new UnresolvedItem(
                CodeFor(type), type.ToString(), failure.Message, type,
                failure.RuleId, failure.VerificationStatus, QuestionFor(type), failure.Remedy));
            return null;
        }

        var credits = new List<TaxCreditRule>();
        foreach (var creditType in new[]
                 {
                     TaxCreditType.Elderly, TaxCreditType.Disabled, TaxCreditType.Blind
                 })
        {
            var rule = Resolve<TaxCreditRule>(StatutoryRuleType.TaxCredit,
                discriminator: creditType.ToString());
            if (rule is not null)
            {
                credits.Add(rule);
            }
        }

        var levies = new List<EmployerLevyRule>();
        foreach (var levyType in new[]
                 {
                     Domain.Statutory.EmployerLevyType.Zimdef,
                     Domain.Statutory.EmployerLevyType.StandardsDevelopmentFund
                 })
        {
            var rule = Resolve<EmployerLevyRule>(StatutoryRuleType.EmployerLevy,
                discriminator: levyType.ToString());
            if (rule is { IsActive: true })
            {
                levies.Add(rule);
            }
        }

        // Only the categories this employee actually claimed are resolved. Resolving every
        // configured category would report an unverified Sunday rate as blocking for somebody who
        // never worked a Sunday.
        var overtimeRules = new List<OvertimeRule>();
        foreach (var category in overtimeCategories.Distinct())
        {
            var rule = Resolve<OvertimeRule>(StatutoryRuleType.Overtime, discriminator: category);
            if (rule is not null)
            {
                overtimeRules.Add(rule);
            }
        }

        return new ResolvedRules
        {
            PayeTable = Resolve<TaxRule>(StatutoryRuleType.PayeTable, period.Frequency),
            Overtime = overtimeRules,
            PayDivisor = Resolve<PayDivisorRule>(StatutoryRuleType.PayDivisor, period.Frequency),
            AidsLevy = Resolve<AidsLevyRule>(StatutoryRuleType.AidsLevy),
            Nssa = Resolve<NssaRule>(StatutoryRuleType.NssaPobs),
            NssaEligibility = Resolve<NssaEligibilityRule>(StatutoryRuleType.NssaEligibility,
                discriminator: employmentType?.Code),
            TaxCredits = credits,
            BonusExemption = Resolve<TaxExemptionRule>(StatutoryRuleType.TaxExemption,
                discriminator: TaxExemptionType.AnnualBonus.ToString()),
            Apwcs = Resolve<ApwcsRule>(StatutoryRuleType.Apwcs),
            EmployerLevies = levies,
            CurrencyStrategy = Resolve<CurrencyTaxStrategyRule>(
                StatutoryRuleType.CurrencyTaxStrategy),
            Unresolved = unresolved
        };
    }

    /// <summary>
    /// The engine's code for a failure to resolve this rule type, so a resolution failure and an
    /// in-engine refusal are reported identically.
    /// </summary>
    private static string CodeFor(StatutoryRuleType type) => type switch
    {
        StatutoryRuleType.PayeTable => UnresolvedCodes.PayeTableUnresolved,
        StatutoryRuleType.AidsLevy => UnresolvedCodes.AidsLevyRuleUnresolved,
        StatutoryRuleType.NssaPobs => UnresolvedCodes.NssaRuleUnresolved,
        StatutoryRuleType.NssaEligibility => UnresolvedCodes.NssaEligibilityUnresolved,
        StatutoryRuleType.Apwcs => UnresolvedCodes.ApwcsRuleUnresolved,
        StatutoryRuleType.EmployerLevy => UnresolvedCodes.EmployerLevyUnresolved,
        StatutoryRuleType.TaxCredit => UnresolvedCodes.TaxCreditRuleUnresolved,
        StatutoryRuleType.TaxExemption => UnresolvedCodes.ExemptionRuleUnresolved,
        StatutoryRuleType.CurrencyTaxStrategy => UnresolvedCodes.CurrencyStrategyUnresolved,
        StatutoryRuleType.Overtime => UnresolvedCodes.OvertimeRuleUnresolved,
        StatutoryRuleType.PayDivisor => UnresolvedCodes.OvertimeRuleUnresolved,
        _ => "RULE_UNRESOLVED"
    };

    /// <summary>
    /// The compliance question that must be answered to clear this rule, from the specification.
    /// Carrying it here means the payroll screen can point at the exact open question.
    /// </summary>
    private static string? QuestionFor(StatutoryRuleType type) => type switch
    {
        StatutoryRuleType.PayeTable => "Q26",
        StatutoryRuleType.AidsLevy => "Q2",
        StatutoryRuleType.NssaPobs => "Q22",
        StatutoryRuleType.NssaEligibility => "Q5",
        StatutoryRuleType.Apwcs => "Q6",
        StatutoryRuleType.TaxCredit => "Q24",
        StatutoryRuleType.TaxExemption => "Q28",
        StatutoryRuleType.CurrencyTaxStrategy => "Q1",
        StatutoryRuleType.Overtime => "Q31",
        StatutoryRuleType.PayDivisor => "Q33",
        _ => null
    };

    private static bool RequiresCurrency(StatutoryRuleType type) => type switch
    {
        StatutoryRuleType.PayeTable => true,
        StatutoryRuleType.NssaPobs => true,
        StatutoryRuleType.Apwcs => true,
        StatutoryRuleType.TaxCredit => true,
        StatutoryRuleType.TaxExemption => true,
        _ => false
    };
}
