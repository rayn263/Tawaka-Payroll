using Microsoft.EntityFrameworkCore;
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

    public PayrollReportService(IPayrollDataContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<PayrollRunReports?> BuildAsync(
        Guid runId, CancellationToken cancellationToken = default)
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
            .Include(e => e.DeductionLines)
            .Include(e => e.EmployerCostLines)
            .Include(e => e.UnresolvedItems)
            .Include(e => e.CostAllocations)
            .Where(e => e.PayrollRunId == runId && !e.IsExcluded)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var obligations = await _context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Where(o => o.PayrollRunId == runId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var departments = await _context.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken).ConfigureAwait(false);

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
            CurrencySummary = BuildCurrencySummary(employees)
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

            var rows = allocations
                .GroupBy(a => new { a.ProjectId, Name = a.ProjectName ?? "Unallocated" })
                .Select(g => new ProjectLabourCostRow(
                    g.Key.ProjectId, g.Key.Name, g.Count(), g.Sum(a => a.AllocatedCostAmount),
                    totalCost == 0m ? 0m : Math.Round(g.Sum(a => a.AllocatedCostAmount) / totalCost * 100m, 2)))
                .OrderByDescending(r => r.AllocatedCost)
                .ToList();

            var totals = new ProjectLabourCostRow(
                null, "Total", allocations.Count, totalCost, rows.Count == 0 ? 0m : 100m);

            return new CurrencySection<ProjectLabourCostRow>(group.Key, totals, rows);
        }).ToList();

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
