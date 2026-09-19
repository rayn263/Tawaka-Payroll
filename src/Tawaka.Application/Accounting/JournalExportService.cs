using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Security;
using Tawaka.Domain.Accounting;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;

namespace Tawaka.Application.Accounting;

/// <summary>One line of a payroll journal.</summary>
public sealed record JournalLine(
    string AccountCode, string AccountName, string? CostCentre, string Narrative,
    decimal Debit, decimal Credit, GlMappingType MappingType);

/// <summary>
/// A payroll journal for exactly one currency.
/// <para>
/// One currency, always. A journal that mixed USD and ZiG would not balance in any meaningful
/// sense, and an accountant posting it would have no way to correct it (ADR-036).
/// </para>
/// </summary>
public sealed record PayrollJournal
{
    public required string CurrencyCode { get; init; }
    public required string PeriodName { get; init; }
    public required DateOnly PostingDate { get; init; }
    public required string Reference { get; init; }
    public IReadOnlyList<JournalLine> Lines { get; init; } = Array.Empty<JournalLine>();

    /// <summary>Amounts payroll could not map, with the reason. Never silently dropped.</summary>
    public IReadOnlyList<string> Unmapped { get; init; } = Array.Empty<string>();

    public string CurrencyLabel => CurrencyCode == "ZWG" ? "ZiG" : CurrencyCode;

    public decimal TotalDebits => Lines.Sum(l => l.Debit);

    public decimal TotalCredits => Lines.Sum(l => l.Credit);

    /// <summary>
    /// A journal that does not balance is not postable. It is reported rather than corrected: an
    /// imbalance means something is wrong upstream, and quietly plugging it would hide that.
    /// </summary>
    public bool IsBalanced => TotalDebits == TotalCredits;

    public decimal Imbalance => TotalDebits - TotalCredits;
}

/// <summary>
/// Turns a finalised payroll run into a journal per currency.
/// <para>
/// It reads persisted results and nothing else — no payroll arithmetic happens here, only the
/// summation and the debit/credit sides an accountant expects. Where an amount has no mapped
/// account it is listed as unmapped rather than posted to a default, because a suspense posting
/// nobody asked for is how payroll errors reach the trial balance and stay there.
/// </para>
/// </summary>
public sealed class JournalExportService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;

    public JournalExportService(IPayrollDataContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<PayrollJournal>> BuildAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.ReportsExport);

        var run = await _context.PayrollRuns.AsNoTracking()
            .Include(r => r.PayrollPeriod)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            .ConfigureAwait(false);

        if (run is null)
        {
            return Array.Empty<PayrollJournal>();
        }

        var employees = await _context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.DeductionLines)
            .Include(e => e.EmployerCostLines)
            .Where(e => e.PayrollRunId == runId && !e.IsExcluded && e.IsCalculated)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var mappings = await _context.GlAccountMappings.AsNoTracking()
            .Where(m => m.CompanyId == run.CompanyId && m.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var postingDate = run.PayrollPeriod?.PayDate ?? DateOnly.FromDateTime(DateTime.Today);
        var periodName = run.PayrollPeriod?.Name ?? "Payroll run";

        // Grouped by currency before anything is summed, exactly as every report is.
        return employees
            .GroupBy(e => e.CurrencyCode)
            .OrderBy(g => g.Key)
            .Select(group => BuildForCurrency(
                group.Key, group.ToList(), mappings, periodName, postingDate,
                $"PAY-{run.RunNumber:D3}-{postingDate:yyyyMM}"))
            .ToList();
    }

    private static PayrollJournal BuildForCurrency(
        string currency, List<PayrollRunEmployee> employees, List<GlAccountMapping> mappings,
        string periodName, DateOnly postingDate, string reference)
    {
        var lines = new List<JournalLine>();
        var unmapped = new List<string>();

        void Post(GlMappingType type, decimal amount, string narrative, bool debit)
        {
            if (amount == 0m)
            {
                return;
            }

            var mapping = mappings.FirstOrDefault(m =>
                m.MappingType == type &&
                string.Equals(m.CurrencyCode, currency, StringComparison.OrdinalIgnoreCase));

            if (mapping is null)
            {
                unmapped.Add(
                    $"{narrative}: {currency} {amount:N2} has no {type} account mapped for " +
                    $"{currency}. Configure it in Settings before posting this journal.");
                return;
            }

            lines.Add(new JournalLine(
                mapping.AccountCode, mapping.AccountName, mapping.CostCentre, narrative,
                debit ? amount : 0m, debit ? 0m : amount, type));
        }

        var gross = employees.Sum(e => e.GrossEarningsAmount ?? 0m);
        var paye = employees.Sum(e => e.PayeAfterCreditsAmount ?? 0m);
        var aids = employees.Sum(e => e.AidsLevyAmount ?? 0m);
        var nssaEmployee = employees.Sum(e => e.NssaEmployeeAmount ?? 0m);
        var netPay = employees.Sum(e => e.NetPayAmount ?? 0m);

        // Employee-borne deductions that are not statutory, split from loan recoveries because a
        // loan repayment settles a receivable rather than creating a liability to a third party.
        var otherDeductions = employees
            .SelectMany(e => e.DeductionLines)
            .Where(l => !l.IsStatutory)
            .ToList();

        var loanRecovery = otherDeductions
            .Where(l => l.Code is "LOAN" or "ADVANCE")
            .Sum(l => l.Amount);

        var thirdPartyDeductions = otherDeductions
            .Where(l => l.Code is not ("LOAN" or "ADVANCE" or "UNPAID_LEAVE"))
            .Sum(l => l.Amount);

        // Unpaid leave is owed to nobody — not the employee, not a third party, not the employer's
        // own balance sheet. The engine records it as a deduction from gross rather than a smaller
        // gross, so the journal credits it back against the wages expense: the expense ends up at
        // what the employer actually bears. Leaving it out altogether, as this once did, left the
        // journal out of balance by exactly the amount of the leave.
        var unpaidLeave = otherDeductions
            .Where(l => l.Code == "UNPAID_LEAVE")
            .Sum(l => l.Amount);

        Post(GlMappingType.WagesExpense, gross, $"Gross wages — {periodName}", debit: true);
        Post(GlMappingType.WagesExpense, unpaidLeave, $"Unpaid leave not earned — {periodName}",
            debit: false);
        Post(GlMappingType.PayeLiability, paye, $"PAYE withheld — {periodName}", debit: false);
        Post(GlMappingType.AidsLevyLiability, aids, $"AIDS Levy withheld — {periodName}", debit: false);
        Post(GlMappingType.NssaEmployeeLiability, nssaEmployee,
            $"NSSA employee contribution withheld — {periodName}", debit: false);
        Post(GlMappingType.OtherDeductionLiability, thirdPartyDeductions,
            $"Other deductions withheld — {periodName}", debit: false);
        Post(GlMappingType.LoanRecoveryReceivable, loanRecovery,
            $"Loan and advance recoveries — {periodName}", debit: false);
        Post(GlMappingType.NetPayLiability, netPay, $"Net pay owed to employees — {periodName}",
            debit: false);

        // Employer-borne costs: an expense and a matching liability, never an employee deduction.
        var employerCosts = employees
            .SelectMany(e => e.EmployerCostLines)
            .GroupBy(l => l.Code)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));

        var nssaEmployer = employerCosts.GetValueOrDefault("NSSA_POBS_ER");
        Post(GlMappingType.NssaEmployerExpense, nssaEmployer,
            $"NSSA employer contribution — {periodName}", debit: true);
        Post(GlMappingType.NssaEmployerLiability, nssaEmployer,
            $"NSSA employer contribution payable — {periodName}", debit: false);

        var otherEmployer = employerCosts
            .Where(c => c.Key != "NSSA_POBS_ER")
            .Sum(c => c.Value);

        Post(GlMappingType.EmployerStatutoryExpense, otherEmployer,
            $"Employer statutory costs (APWCS, ZIMDEF, SDF) — {periodName}", debit: true);
        Post(GlMappingType.EmployerStatutoryLiability, otherEmployer,
            $"Employer statutory costs payable — {periodName}", debit: false);

        return new PayrollJournal
        {
            CurrencyCode = currency,
            PeriodName = periodName,
            PostingDate = postingDate,
            Reference = reference,
            Lines = lines,
            Unmapped = unmapped
        };
    }

    // ---- Mapping configuration ---------------------------------------------------------------

    public Task<List<GlAccountMapping>> GetMappingsAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.CompanyView);

        return _context.GlAccountMappings.AsNoTracking()
            .Where(m => m.CompanyId == companyId)
            .OrderBy(m => m.CurrencyCode).ThenBy(m => m.MappingType)
            .ToListAsync(cancellationToken);
    }

    public async Task<ValidationResult> SetMappingAsync(
        Guid companyId, GlMappingType type, string currencyCode, string accountCode,
        string accountName, string? costCentre, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.SettingsEdit);

        var validation = ValidationResult.Success();
        validation.Require(currencyCode, "Currency");
        validation.Require(accountCode, "Account code");
        validation.Require(accountName, "Account name");

        if (!validation.IsValid)
        {
            return validation;
        }

        var existing = await _context.GlAccountMappings
            .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.MappingType == type &&
                                      m.CurrencyCode == currencyCode, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new GlAccountMapping
            {
                CompanyId = companyId,
                MappingType = type,
                CurrencyCode = currencyCode
            };
            _context.GlAccountMappings.Add(existing);
        }

        existing.AccountCode = accountCode;
        existing.AccountName = accountName;
        existing.CostCentre = costCentre;
        existing.IsActive = true;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }
}
