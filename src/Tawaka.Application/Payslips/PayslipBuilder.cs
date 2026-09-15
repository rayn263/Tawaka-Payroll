using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Security;
using Tawaka.Domain.Common;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory.Obligations;

namespace Tawaka.Application.Payslips;

/// <summary>
/// Builds payslips from persisted payroll results, and records their issue.
/// <para>
/// This class reads. It does not calculate: every figure it places on a document was produced by
/// the engine, stored against the payroll run, and is copied here unchanged. Reconciliation
/// between the rendered lines and the stored totals is asserted by test.
/// </para>
/// </summary>
public sealed class PayslipBuilder
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public PayslipBuilder(IPayrollDataContext context, ICurrentUser currentUser, IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<PayslipDocument?> BuildAsync(
        Guid runEmployeeId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.PayrollView);

        var runEmployee = await _context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.EarningLines)
            .Include(e => e.DeductionLines)
            .Include(e => e.EmployerCostLines)
            .Include(e => e.UnresolvedItems)
            .FirstOrDefaultAsync(e => e.Id == runEmployeeId, cancellationToken)
            .ConfigureAwait(false);

        if (runEmployee is null)
        {
            return null;
        }

        var run = await _context.PayrollRuns.AsNoTracking()
            .Include(r => r.PayrollPeriod)
            .FirstAsync(r => r.Id == runEmployee.PayrollRunId, cancellationToken)
            .ConfigureAwait(false);

        var company = await _context.Companies.AsNoTracking()
            .FirstAsync(c => c.Id == run.CompanyId, cancellationToken).ConfigureAwait(false);

        var employee = await _context.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == runEmployee.EmployeeId, cancellationToken)
            .ConfigureAwait(false);

        var profile = await _context.EmployeeStatutoryProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EmployeeId == runEmployee.EmployeeId, cancellationToken)
            .ConfigureAwait(false);

        var account = await _context.EmployeePaymentAccounts.AsNoTracking()
            .Where(a => a.EmployeeId == runEmployee.EmployeeId && a.IsActive && a.IsPrimary)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var contract = await _context.EmployeeContracts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == runEmployee.EmployeeContractId, cancellationToken)
            .ConfigureAwait(false);

        var jobTitle = contract?.JobTitleId is null
            ? null
            : await _context.JobTitles.AsNoTracking()
                .Where(j => j.Id == contract.JobTitleId).Select(j => j.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var department = contract?.DepartmentId is null
            ? null
            : await _context.Departments.AsNoTracking()
                .Where(d => d.Id == contract.DepartmentId).Select(d => d.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var project = contract?.ProjectId is null
            ? null
            : await _context.Projects.AsNoTracking()
                .Where(p => p.Id == contract.ProjectId).Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        var payslip = await _context.Payslips.AsNoTracking()
            .Where(p => p.PayrollRunEmployeeId == runEmployeeId && p.SupersededByPayslipId == null)
            .OrderByDescending(p => p.Revision)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var obligations = await _context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Where(o => o.PayrollRunId == run.Id && o.CurrencyCode == runEmployee.CurrencyCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var currency = runEmployee.Currency;

        return new PayslipDocument
        {
            Company = new PayslipCompany(
                company.DisplayName,
                string.Join(", ", new[] { company.AddressLine1, company.City }
                    .Where(v => !string.IsNullOrWhiteSpace(v))),
                company.Phone, company.Email, company.TaxNumber, company.NssaEmployerNumber,
                company.LogoPath),
            Employee = new PayslipEmployee(
                runEmployee.EmployeeName, runEmployee.EmployeeNumber, employee?.NationalId,
                jobTitle, department, runEmployee.EmploymentTypeCode, project,
                employee?.HireDate ?? run.PayrollPeriod!.StartDate,
                profile?.TaxNumber, profile?.NssaNumber,
                account?.PaymentMethod.ToString(), account?.Describe()),
            PeriodName = run.PayrollPeriod!.Name,
            PeriodStart = run.PayrollPeriod.StartDate,
            PeriodEnd = run.PayrollPeriod.EndDate,
            PayDate = run.PayrollPeriod.PayDate,
            Currency = currency,
            PayslipNumber = payslip?.PayslipNumber,
            Revision = payslip?.Revision ?? 1,
            IsDevelopmentCopy = run.Mode == PayrollMode.Development,
            Earnings = BuildEarnings(runEmployee, currency),
            Deductions = BuildDeductions(runEmployee, currency),
            EmployerContributions = BuildEmployerContributions(runEmployee, currency),
            StatutoryStatus = BuildStatutoryStatus(runEmployee, obligations, currency),
            GrossEarnings = PayslipAmount.From(runEmployee.GrossEarningsAmount, currency),
            TotalDeductions = PayslipAmount.From(runEmployee.TotalDeductionsAmount, currency),
            NetPay = PayslipAmount.From(runEmployee.NetPayAmount, currency),
            TotalEmployerCost = PayslipAmount.From(runEmployee.TotalEmployerCostAmount, currency),
            UnresolvedNotes = runEmployee.UnresolvedItems
                .Select(u => $"{u.ItemKey}: {u.Message}").ToList()
        };
    }

    private static List<PayslipLine> BuildEarnings(PayrollRunEmployee employee, CurrencyCode currency) =>
        employee.EarningLines
            .OrderBy(l => l.DisplayOrder)
            .Select(l => new PayslipLine(
                l.Code, l.Name, PayslipAmount.From(l.Amount, currency),
                l.Quantity is null ? null : $"{l.Quantity:N2} @ {l.RateAmount:N2}",
                l.OriginalAmount, l.OriginalCurrencyCode, l.ExchangeRateUsed, l.ExchangeRateDate,
                l.ExchangeRateSource))
            .ToList();

    /// <summary>
    /// Deduction lines, with a zero row added for any statutory category the run did not produce —
    /// a configured statutory category stays visible at 0.00, because a missing line is ambiguous
    /// while a zero is information.
    /// </summary>
    private static List<PayslipLine> BuildDeductions(
        PayrollRunEmployee employee, CurrencyCode currency)
    {
        var lines = employee.DeductionLines
            .OrderBy(l => l.IsStatutory ? 0 : 1).ThenBy(l => l.DisplayOrder)
            .Select(l => new PayslipLine(l.Code, l.Name, PayslipAmount.From(l.Amount, currency)))
            .ToList();

        foreach (var (code, name) in new[]
                 {
                     ("PAYE", "PAYE"), ("AIDSLEVY", "AIDS Levy"), ("NSSA_EE", "NSSA Employee (POBS)")
                 })
        {
            if (lines.Any(l => l.Code == code))
            {
                continue;
            }

            // The figure was not produced at all: unresolved, not zero — unless the run genuinely
            // calculated and it simply did not arise.
            var unresolved = employee.UnresolvedItems.Any(u =>
                u.ItemKey.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                u.ItemKey.Contains(code, StringComparison.OrdinalIgnoreCase));

            lines.Insert(0, new PayslipLine(code, name,
                unresolved
                    ? new PayslipAmount(null, PayslipValueState.Unresolved,
                        "The rule needed for this figure is missing or unverified.")
                    : PayslipAmount.From(0m, currency)));
        }

        return lines;
    }

    private static List<PayslipLine> BuildEmployerContributions(
        PayrollRunEmployee employee, CurrencyCode currency) =>
        employee.EmployerCostLines
            .OrderBy(l => l.DisplayOrder)
            .Select(l => new PayslipLine(l.Code, l.Name, PayslipAmount.From(l.Amount, currency)))
            .ToList();

    /// <summary>
    /// Shows how far each statutory amount has actually got. Remitted is true only where a payment
    /// has been recorded against the obligation.
    /// </summary>
    private static List<PayslipStatutoryStatus> BuildStatutoryStatus(
        PayrollRunEmployee employee, List<StatutoryObligation> obligations, CurrencyCode currency)
    {
        var statuses = new List<PayslipStatutoryStatus>();

        void Add(string name, decimal? amount, StatutoryObligationType type)
        {
            var obligation = obligations.FirstOrDefault(o => o.ObligationType == type);
            statuses.Add(new PayslipStatutoryStatus(
                name,
                PayslipAmount.From(amount, currency),
                obligation?.IsDeducted ?? false,
                obligation?.IsApproved ?? false,
                obligation?.IsPaid ?? false,
                obligation?.IsDeductionApplicable ?? true));
        }

        Add("PAYE", employee.PayeAfterCreditsAmount, StatutoryObligationType.Paye);
        Add("AIDS Levy", employee.AidsLevyAmount, StatutoryObligationType.AidsLevy);
        Add("NSSA Employee", employee.NssaEmployeeAmount, StatutoryObligationType.NssaPobsEmployee);
        Add("NSSA Employer", employee.NssaEmployerAmount, StatutoryObligationType.NssaPobsEmployer);
        Add("APWCS", employee.ApwcsAmount, StatutoryObligationType.Apwcs);

        return statuses;
    }

    /// <summary>
    /// Issues a numbered payslip. A development-mode run cannot produce an issuable payslip, and
    /// re-issuing supersedes the previous revision rather than replacing it.
    /// </summary>
    public async Task<OperationResult<Payslip>> GenerateAsync(
        Guid runEmployeeId, string? supersedeReason = null,
        CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.PayrollFinalise);

        var validation = ValidationResult.Success();
        var runEmployee = await _context.PayrollRunEmployees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == runEmployeeId, cancellationToken)
            .ConfigureAwait(false);

        if (runEmployee is null)
        {
            return OperationResult<Payslip>.Failed(
                validation.Add("Payslip", "Payroll result not found."));
        }

        var run = await _context.PayrollRuns.AsNoTracking()
            .Include(r => r.PayrollPeriod)
            .FirstAsync(r => r.Id == runEmployee.PayrollRunId, cancellationToken)
            .ConfigureAwait(false);

        validation.AddIf(run.Status < PayrollRunStatus.Finalised, "Payslip",
            $"A payslip can only be issued from a finalised run. This run is {run.Status}.");

        validation.AddIf(!runEmployee.IsCalculated, "Payslip",
            "This employee's payroll could not be fully calculated, so no payslip can be issued.");

        if (!validation.IsValid)
        {
            return OperationResult<Payslip>.Failed(validation);
        }

        var existing = await _context.Payslips
            .Where(p => p.PayrollRunEmployeeId == runEmployeeId && p.SupersededByPayslipId == null)
            .OrderByDescending(p => p.Revision)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            validation.Require(supersedeReason, nameof(supersedeReason),
                "A reason is required to re-issue a payslip that has already been generated.");
            if (!validation.IsValid)
            {
                return OperationResult<Payslip>.Failed(validation);
            }
        }

        var payslip = new Payslip
        {
            PayrollRunEmployeeId = runEmployeeId,
            PayslipNumber = existing?.PayslipNumber
                            ?? await NextNumberAsync(run, cancellationToken).ConfigureAwait(false),
            Revision = (existing?.Revision ?? 0) + 1,
            GeneratedAt = _clock.Now,
            GeneratedBy = _currentUser.UserId,
            IsDevelopmentCopy = run.Mode == PayrollMode.Development,
            TemplateVersion = PayslipDocument.TemplateVersion,
            SupersedeReason = supersedeReason
        };

        _context.Payslips.Add(payslip);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            // The superseded revision is retained: a payslip already handed to an employee must
            // remain traceable.
            existing.SupersededByPayslipId = payslip.Id;
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return OperationResult<Payslip>.Success(payslip);
    }

    private async Task<string> NextNumberAsync(PayrollRun run, CancellationToken cancellationToken)
    {
        var period = run.PayrollPeriod!;
        var prefix = $"PS-{period.PayDate:yyyy-MM}-";

        var count = await _context.Payslips
            .CountAsync(p => p.PayslipNumber.StartsWith(prefix), cancellationToken)
            .ConfigureAwait(false);

        return $"{prefix}{count + 1:D4}";
    }
}
