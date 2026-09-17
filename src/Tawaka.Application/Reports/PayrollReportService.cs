using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Payroll;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Security;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory.Obligations;

namespace Tawaka.Application.Reports;

/// <summary>
/// Builds reports from persisted payroll results.
/// <para>
/// Like the payslip, this reads: it aggregates stored figures and never recalculates payroll.
/// Every aggregation groups by currency first, so no total can span USD and ZiG.
/// </para>
/// </summary>
public sealed class PayrollReportService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly Administration.UserDirectory _directory;

    public PayrollReportService(
        IPayrollDataContext context, ICurrentUser currentUser,
        Administration.UserDirectory directory)
    {
        _context = context;
        _currentUser = currentUser;
        _directory = directory;
    }

    /// <summary>
    /// Narrows the population before anything is summed, so a filtered report's totals are the
    /// totals of what it shows. A filtered report that carried full-run totals would be read as
    /// the whole payroll and reconciled against nothing.
    /// </summary>
    private static List<PayrollRunEmployee> Apply(
        ReportFilter filter, List<PayrollRunEmployee> employees)
    {
        if (filter.IsEmpty)
        {
            return employees;
        }

        var filtered = employees.AsEnumerable();

        if (filter.EmployeeId is { } employeeId)
        {
            filtered = filtered.Where(e => e.EmployeeId == employeeId);
        }

        if (filter.DepartmentId is { } departmentId)
        {
            filtered = filtered.Where(e => e.DepartmentId == departmentId);
        }

        if (filter.ProjectId is { } projectId)
        {
            filtered = filtered.Where(e =>
                e.ProjectId == projectId ||
                e.CostAllocations.Any(a => a.ProjectId == projectId));
        }

        if (filter.ProjectSiteId is { } siteId)
        {
            filtered = filtered.Where(e => e.CostAllocations.Any(a => a.ProjectSiteId == siteId));
        }

        if (!string.IsNullOrWhiteSpace(filter.EmploymentTypeCode))
        {
            filtered = filtered.Where(e => string.Equals(
                e.EmploymentTypeCode, filter.EmploymentTypeCode, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.CurrencyCode))
        {
            filtered = filtered.Where(e => string.Equals(
                e.CurrencyCode, filter.CurrencyCode, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.IsCalculated is { } calculated)
        {
            filtered = filtered.Where(e => e.IsCalculated == calculated);
        }

        return filtered.ToList();
    }

    public Task<PayrollRunReports?> BuildAsync(
        Guid runId, CancellationToken cancellationToken = default) =>
        BuildAsync(runId, new ReportFilter(), cancellationToken);

    public async Task<PayrollRunReports?> BuildAsync(
        Guid runId, ReportFilter filter, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.ReportsRun);

        var run = await _context.PayrollRuns.AsNoTracking()
            .Include(r => r.PayrollPeriod)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return null;
        }

        var employees = await _context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.EarningLines)
            .Include(e => e.DeductionLines)
            .Include(e => e.EmployerCostLines)
            .Include(e => e.UnresolvedItems)
            .Include(e => e.CostAllocations)
            .Where(e => e.PayrollRunId == runId && !e.IsExcluded)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        employees = Apply(filter, employees);

        var obligations = await _context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Where(o => o.PayrollRunId == runId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var departments = await _context.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken).ConfigureAwait(false);

        var employeeIds = employees.Select(e => e.EmployeeId).ToList();
        var profiles = await _context.EmployeeStatutoryProfiles.AsNoTracking()
            .Where(p => employeeIds.Contains(p.EmployeeId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var runEmployeeIds = employees.Select(e => e.Id).ToList();

        var inputSources = await _context.PayrollRunInputSources.AsNoTracking()
            .Where(s => s.PayrollRunId == runId && runEmployeeIds.Contains(s.PayrollRunEmployeeId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var snapshots = await _context.PayrollInputSnapshots.AsNoTracking()
            .Where(s => s.PayrollRunId == runId)
            .Select(s => new { s.PayrollRunEmployeeId, s.SnapshotHash, s.SnapshotJson })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var payments = await _context.StatutoryPayments.AsNoTracking()
            .Where(p => _context.StatutoryObligations
                .Any(o => o.Id == p.StatutoryObligationId && o.PayrollRunId == runId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var actorIds = new[]
        {
            run.CalculatedBy, run.ApprovedBy, run.FinalisedBy, run.PaidBy, run.LockedBy
        };
        var actors = await _directory.ResolveAsync(actorIds, cancellationToken).ConfigureAwait(false);

        var today = run.PayrollPeriod?.PayDate ?? DateOnly.FromDateTime(DateTime.Today);

        return new PayrollRunReports
        {
            PeriodName = run.PayrollPeriod?.Name ?? "Payroll run",
            PayDate = today,
            IsDevelopmentRun = run.Mode == PayrollMode.Development,
            Summary = BuildSummary(employees),
            Register = BuildRegister(employees, departments),
            Deductions = BuildDeductions(employees),
            EmployerCosts = BuildEmployerCosts(employees),
            Statutory = BuildStatutory(obligations, today),
            ProjectLabourCost = BuildProjectCost(employees),
            CurrencySummary = BuildCurrencySummary(employees),
            EmployeeEarnings = BuildEmployeeEarnings(employees),
            Tax = BuildTax(employees, profiles),
            Nssa = BuildNssa(employees, profiles),
            Inputs = BuildInputs(employees, inputSources,
                snapshots.ToDictionary(s => s.PayrollRunEmployeeId, s => s.SnapshotHash)),
            SkippedInputs = BuildSkippedInputs(
                employees,
                snapshots.ToDictionary(
                    s => s.PayrollRunEmployeeId, s => (s.SnapshotHash, s.SnapshotJson))),
            StatutoryPayments = BuildPayments(obligations, payments),
            Audit = BuildAudit(run, actors),
            Filter = filter,
            FilterDescription = filter.Describe()
        };
    }

    private static IReadOnlyList<CurrencySection<PayrollSummaryRow>> BuildSummary(
        List<PayrollRunEmployee> employees) =>
        employees.GroupBy(e => e.CurrencyCode).OrderBy(g => g.Key).Select(group =>
        {
            var rows = group.GroupBy(e => e.EmploymentTypeCode)
                .Select(byType => Summarise(byType.Key, byType.ToList()))
                .OrderBy(r => r.Key)
                .ToList();

            return new CurrencySection<PayrollSummaryRow>(
                group.Key, Summarise("All employees", group.ToList()), rows);
        }).ToList();

    private static PayrollSummaryRow Summarise(string key, List<PayrollRunEmployee> employees) =>
        new(key,
            employees.Count,
            employees.Sum(e => e.GrossEarningsAmount ?? 0m),
            employees.Sum(e => e.TaxableIncomeAmount ?? 0m),
            employees.Sum(e => e.PayeAfterCreditsAmount ?? 0m),
            employees.Sum(e => e.AidsLevyAmount ?? 0m),
            employees.Sum(e => e.NssaEmployeeAmount ?? 0m),
            employees.Sum(e => e.TotalOtherDeductionsAmount ?? 0m),
            employees.Sum(e => e.NetPayAmount ?? 0m),
            employees.Sum(e => e.TotalEmployerCostAmount ?? 0m));

    private static IReadOnlyList<CurrencySection<PayrollRegisterRow>> BuildRegister(
        List<PayrollRunEmployee> employees, IReadOnlyDictionary<Guid, string> departments) =>
        employees.GroupBy(e => e.CurrencyCode).OrderBy(g => g.Key).Select(group =>
        {
            var rows = group.OrderBy(e => e.EmployeeName).Select(e => new PayrollRegisterRow(
                e.EmployeeNumber, e.EmployeeName, e.EmploymentTypeCode,
                e.DepartmentId is { } id && departments.TryGetValue(id, out var name) ? name : null,
                e.CostAllocations.FirstOrDefault()?.ProjectName,
                e.GrossEarningsAmount, e.TaxableIncomeAmount, e.PayeAfterCreditsAmount,
                e.AidsLevyAmount, e.NssaEmployeeAmount, e.TotalOtherDeductionsAmount,
                e.NetPayAmount, e.TotalEmployerCostAmount,
                e.IsCalculated, e.UnresolvedItems.Count)).ToList();

            var totals = new PayrollRegisterRow(
                string.Empty, "Total", string.Empty, null, null,
                rows.Sum(r => r.Gross ?? 0m), rows.Sum(r => r.Taxable ?? 0m),
                rows.Sum(r => r.Paye ?? 0m), rows.Sum(r => r.AidsLevy ?? 0m),
                rows.Sum(r => r.NssaEmployee ?? 0m), rows.Sum(r => r.OtherDeductions ?? 0m),
                rows.Sum(r => r.NetPay ?? 0m), rows.Sum(r => r.EmployerCost ?? 0m),
                rows.All(r => r.IsCalculated), rows.Sum(r => r.UnresolvedCount));

            return new CurrencySection<PayrollRegisterRow>(group.Key, totals, rows);
        }).ToList();

    private static IReadOnlyList<CurrencySection<DeductionReportRow>> BuildDeductions(
        List<PayrollRunEmployee> employees) =>
        employees.GroupBy(e => e.CurrencyCode).OrderBy(g => g.Key).Select(group =>
        {
            var rows = group.SelectMany(e => e.DeductionLines)
                .GroupBy(l => new { l.Code, l.Name, l.IsStatutory })
                .Select(g => new DeductionReportRow(
                    g.Key.Code, g.Key.Name, g.Key.IsStatutory, g.Count(), g.Sum(l => l.Amount)))
                .OrderByDescending(r => r.IsStatutory).ThenBy(r => r.Name)
                .ToList();

            var totals = new DeductionReportRow(
                string.Empty, "Total deductions", false, group.Count(), rows.Sum(r => r.Total));

            return new CurrencySection<DeductionReportRow>(group.Key, totals, rows);
        }).ToList();

    private static IReadOnlyList<CurrencySection<EmployerCostRow>> BuildEmployerCosts(
        List<PayrollRunEmployee> employees) =>
        employees.GroupBy(e => e.CurrencyCode).OrderBy(g => g.Key).Select(group =>
        {
            var rows = group.SelectMany(e => e.EmployerCostLines)
                .GroupBy(l => new { l.Code, l.Name })
                .Select(g => new EmployerCostRow(
                    g.Key.Code, g.Key.Name, g.Count(), g.Sum(l => l.BaseAmount),
                    g.Sum(l => l.Amount)))
                .OrderBy(r => r.Name)
                .ToList();

            // Gross is included so the report reconciles: gross + contributions = total cost.
            var gross = group.Sum(e => e.GrossEarningsAmount ?? 0m);
            rows.Insert(0, new EmployerCostRow("GROSS", "Gross earnings", group.Count(), gross, gross));

            var totals = new EmployerCostRow(
                string.Empty, "Total employer cost", group.Count(), gross, rows.Sum(r => r.Amount));

            return new CurrencySection<EmployerCostRow>(group.Key, totals, rows);
        }).ToList();

    private static IReadOnlyList<CurrencySection<StatutoryReportRow>> BuildStatutory(
        List<StatutoryObligation> obligations, DateOnly today) =>
        obligations.GroupBy(o => o.CurrencyCode).OrderBy(g => g.Key).Select(group =>
        {
            var rows = group.OrderBy(o => o.ObligationType).Select(o => new StatutoryReportRow(
                o.ObligationType, o.Authority, o.DueDate, o.CalculatedAmount, o.DeductedAmount,
                o.ApprovedAmount, o.PaidAmount.Amount, o.Outstanding.Amount,
                o.IsDeductionApplicable, o.StatusOn(today))).ToList();

            var totals = new StatutoryReportRow(
                StatutoryObligationType.Paye, StatutoryAuthority.Zimra, null,
                rows.Sum(r => r.Calculated), rows.Sum(r => r.Deducted), rows.Sum(r => r.Approved),
                rows.Sum(r => r.Paid), rows.Sum(r => r.Outstanding), true,
                StatutoryObligationStatus.Calculated);

            return new CurrencySection<StatutoryReportRow>(group.Key, totals, rows);
        }).ToList();

    private static IReadOnlyList<CurrencySection<ProjectLabourCostRow>> BuildProjectCost(
        List<PayrollRunEmployee> employees) =>
        employees.GroupBy(e => e.CurrencyCode).OrderBy(g => g.Key).Select(group =>
        {
            var allocations = group.SelectMany(e => e.CostAllocations).ToList();
            var totalCost = allocations.Sum(a => a.AllocatedCostAmount);

            // Grouped by project and site together: on a construction contract, "what did the
            // Nyanga site cost" is asked at least as often as "what did the contract cost".
            var rows = allocations
                .GroupBy(a => new
                {
                    a.ProjectId,
                    Name = a.ProjectName ?? "Unallocated",
                    a.ProjectSiteId,
                    SiteName = a.ProjectSiteName
                })
                .Select(g => new ProjectLabourCostRow(
                    g.Key.ProjectId, g.Key.Name, g.Count(), g.Sum(a => a.AllocatedCostAmount),
                    totalCost == 0m ? 0m : Math.Round(g.Sum(a => a.AllocatedCostAmount) / totalCost * 100m, 2),
                    g.Key.ProjectSiteId, g.Key.SiteName))
                .OrderByDescending(r => r.AllocatedCost)
                .ToList();

            var totals = new ProjectLabourCostRow(
                null, "Total", allocations.Count, totalCost, rows.Count == 0 ? 0m : 100m);

            return new CurrencySection<ProjectLabourCostRow>(group.Key, totals, rows);
        }).ToList();


    // ---- Milestone 6 reports ---------------------------------------------------------------

    /// <summary>Every earning line, per employee, so a query about one allowance has an answer.</summary>
    private static IReadOnlyList<CurrencySection<EmployeeEarningsRow>> BuildEmployeeEarnings(
        List<PayrollRunEmployee> employees) =>
        employees.GroupBy(e => e.CurrencyCode).OrderBy(g => g.Key).Select(group =>
        {
            var rows = group
                .OrderBy(e => e.EmployeeName)
                .SelectMany(e => e.EarningLines
                    .OrderBy(l => l.DisplayOrder)
                    .Select(l => new EmployeeEarningsRow(
                        e.EmployeeNumber, e.EmployeeName, l.Code, l.Name,
                        l.Quantity, l.RateAmount, l.Amount, l.TaxableAmount, l.ExemptAmount)))
                .ToList();

            var totals = new EmployeeEarningsRow(
                string.Empty, "Total", string.Empty, "All earnings", null, null,
                rows.Sum(r => r.Amount), rows.Sum(r => r.Taxable), rows.Sum(r => r.Exempt));

            return new CurrencySection<EmployeeEarningsRow>(group.Key, totals, rows);
        }).ToList();

    /// <summary>
    /// PAYE and the AIDS Levy together, because the levy is charged on the tax and reading one
    /// without the other invites the wrong conclusion.
    /// </summary>
    private static IReadOnlyList<CurrencySection<TaxReportRow>> BuildTax(
        List<PayrollRunEmployee> employees, List<Domain.Employees.EmployeeStatutoryProfile> profiles) =>
        employees.GroupBy(e => e.CurrencyCode).OrderBy(g => g.Key).Select(group =>
        {
            var rows = group.OrderBy(e => e.EmployeeName).Select(e => new TaxReportRow(
                e.EmployeeNumber, e.EmployeeName,
                profiles.FirstOrDefault(p => p.EmployeeId == e.EmployeeId)?.TaxNumber,
                e.GrossEarningsAmount, e.TaxableIncomeAmount, e.PayeBeforeCreditsAmount,
                e.TaxCreditsAmount, e.PayeAfterCreditsAmount, e.AidsLevyAmount)).ToList();

            var totals = new TaxReportRow(
                string.Empty, "Total", null,
                rows.Sum(r => r.Gross ?? 0m), rows.Sum(r => r.Taxable ?? 0m),
                rows.Sum(r => r.PayeBeforeCredits ?? 0m), rows.Sum(r => r.Credits ?? 0m),
                rows.Sum(r => r.PayeAfterCredits ?? 0m), rows.Sum(r => r.AidsLevy ?? 0m));

            return new CurrencySection<TaxReportRow>(group.Key, totals, rows);
        }).ToList();

    private static IReadOnlyList<CurrencySection<NssaReportRow>> BuildNssa(
        List<PayrollRunEmployee> employees, List<Domain.Employees.EmployeeStatutoryProfile> profiles) =>
        employees.GroupBy(e => e.CurrencyCode).OrderBy(g => g.Key).Select(group =>
        {
            var rows = group.OrderBy(e => e.EmployeeName).Select(e => new NssaReportRow(
                e.EmployeeNumber, e.EmployeeName,
                profiles.FirstOrDefault(p => p.EmployeeId == e.EmployeeId)?.NssaNumber,
                e.NssaInsurableEarningsAmount, e.NssaEmployeeAmount, e.NssaEmployerAmount)).ToList();

            var totals = new NssaReportRow(
                string.Empty, "Total", null,
                rows.Sum(r => r.InsurableEarnings ?? 0m),
                rows.Sum(r => r.EmployeeContribution ?? 0m),
                rows.Sum(r => r.EmployerContribution ?? 0m));

            return new CurrencySection<NssaReportRow>(group.Key, totals, rows);
        }).ToList();

    /// <summary>
    /// Which approved records each employee's figures were based on, and the hash of the sealed
    /// snapshot. This is the report an auditor reconstructing a payroll actually needs.
    /// </summary>
    private static IReadOnlyList<PayrollInputRow> BuildInputs(
        List<PayrollRunEmployee> employees,
        List<Domain.Payroll.PayrollRunInputSource> sources,
        IReadOnlyDictionary<Guid, string> snapshotHashes) =>
        employees
            .OrderBy(e => e.EmployeeName)
            .SelectMany(e => sources
                .Where(s => s.PayrollRunEmployeeId == e.Id)
                .OrderBy(s => s.InputType)
                .Select(s => new PayrollInputRow(
                    e.EmployeeNumber, e.EmployeeName, s.InputType, s.Description,
                    s.ApprovedBy, s.ApprovedAt,
                    snapshotHashes.TryGetValue(e.Id, out var hash) ? hash : string.Empty)))
            .ToList();

    /// <summary>
    /// What this run left out. Read from each employee's sealed snapshot, which is the only place
    /// that knows: the calculation itself has no record of an input it never received.
    /// </summary>
    private static IReadOnlyList<SkippedInputRow> BuildSkippedInputs(
        List<PayrollRunEmployee> employees,
        Dictionary<Guid, (string Hash, string Json)> snapshots)
    {
        var rows = new List<SkippedInputRow>();

        foreach (var employee in employees.OrderBy(e => e.EmployeeName))
        {
            if (!snapshots.TryGetValue(employee.Id, out var stored))
            {
                continue;
            }

            if (PayrollSnapshotStore.Hash(stored.Json) != stored.Hash)
            {
                rows.Add(new SkippedInputRow(
                    employee.EmployeeNumber, employee.EmployeeName, "Snapshot",
                    "This employee's stored snapshot no longer matches its hash and cannot be " +
                    "read. It has been altered since it was sealed."));
                continue;
            }

            var snapshot = PayrollSnapshotStore.Deserialise(stored.Json);
            if (snapshot is null)
            {
                continue;
            }

            rows.AddRange(snapshot.SkippedInputs.Select(skipped => new SkippedInputRow(
                employee.EmployeeNumber, employee.EmployeeName, skipped.InputType, skipped.Reason)));
        }

        return rows;
    }

    /// <summary>
    /// Payments actually made against this run's obligations, reversals included. A reversed
    /// payment stays on the report: an auditor needs to see both the payment and its undoing.
    /// </summary>
    private static IReadOnlyList<CurrencySection<StatutoryPaymentRow>> BuildPayments(
        List<StatutoryObligation> obligations, List<StatutoryPayment> payments) =>
        payments
            .Join(obligations, p => p.StatutoryObligationId, o => o.Id, (p, o) => new { p, o })
            .GroupBy(x => x.p.CurrencyCode)
            .OrderBy(g => g.Key)
            .Select(group =>
            {
                var rows = group
                    .OrderBy(x => x.p.PaymentDate)
                    .Select(x => new StatutoryPaymentRow(
                        x.o.ObligationType, x.o.Authority, x.p.PaymentDate, x.p.Amount,
                        x.p.PrincipalAmount, x.p.PenaltyOrInterestIncluded,
                        x.p.PaymentMethod.ToString(), x.p.PaymentReference,
                        x.p.AuthorityReceiptNumber, x.p.IsReversed, x.p.ReversalReason))
                    .ToList();

                // Reversed payments are shown but excluded from the total: they settled nothing.
                var live = rows.Where(r => !r.IsReversed).ToList();
                var totals = new StatutoryPaymentRow(
                    StatutoryObligationType.Paye, StatutoryAuthority.Zimra, default,
                    live.Sum(r => r.Amount), live.Sum(r => r.PrincipalAmount),
                    live.Sum(r => r.PenaltyOrInterest), string.Empty, "Total", null, false, null);

                return new CurrencySection<StatutoryPaymentRow>(group.Key, totals, rows);
            })
            .ToList();

    /// <summary>The run's lifecycle: who moved it to each stage, and when.</summary>
    private static IReadOnlyList<PayrollAuditRow> BuildAudit(
        PayrollRun run, IReadOnlyDictionary<string, string> actors)
    {
        string? Name(string? id) =>
            id is not null && actors.TryGetValue(id, out var name) ? name : id;

        return new List<PayrollAuditRow>
        {
            new("Created", run.CreatedAt, run.CreatedBy, Name(run.CreatedBy),
                $"Run {run.RunNumber}, {run.RunType}"),
            new("Calculated", run.CalculatedAt, run.CalculatedBy, Name(run.CalculatedBy),
                run.EngineVersion is null ? null : $"Engine {run.EngineVersion}"),
            new("Approved", run.ApprovedAt, run.ApprovedBy, Name(run.ApprovedBy), null),
            new("Finalised", run.FinalisedAt, run.FinalisedBy, Name(run.FinalisedBy),
                "Statutory obligations created"),
            new("Net wages paid", run.PaidAt, run.PaidBy, Name(run.PaidBy), null),
            new("Locked", run.LockedAt, run.LockedBy, Name(run.LockedBy), null)
        };
    }

    private static IReadOnlyList<CurrencySummaryRow> BuildCurrencySummary(
        List<PayrollRunEmployee> employees) =>
        employees.GroupBy(e => e.CurrencyCode).OrderBy(g => g.Key).Select(group =>
            new CurrencySummaryRow(
                group.Key,
                group.Count(),
                group.Sum(e => e.GrossEarningsAmount ?? 0m),
                group.Sum(e => e.TotalDeductionsAmount ?? 0m),
                group.Sum(e => e.NetPayAmount ?? 0m),
                group.SelectMany(e => e.EmployerCostLines).Sum(l => l.Amount),
                group.Sum(e => e.TotalEmployerCostAmount ?? 0m))).ToList();
}
