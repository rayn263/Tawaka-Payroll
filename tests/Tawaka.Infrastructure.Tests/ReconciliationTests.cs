using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Accounting;
using Tawaka.Application.Reports;
using Tawaka.Domain.Accounting;
using Tawaka.Domain.Payroll;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// Everything that must agree, agrees.
/// <para>
/// A payroll system's individual parts can each be right while the whole is wrong: a payslip that
/// disagrees with the register, a journal that does not balance, obligations that do not sum to
/// what was withheld. These are the cross-checks a business, an auditor and ZIMRA would each
/// perform, expressed once, in code.
/// </para>
/// </summary>
public class ReconciliationTests : PayrollFixtureBase
{
    private static PayrollServices Services(Fixture fixture) => PayrollServices.For(fixture.Db);

    [Fact]
    public async Task The_payslip_reconciles_to_the_persisted_payroll_result()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var result = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.EarningLines)
            .Include(e => e.DeductionLines)
            .SingleAsync(e => e.PayrollRunId == runId);

        var payslip = await Services(fixture).Payslips.BuildAsync(result.Id);

        Assert.NotNull(payslip);
        Assert.Equal(result.GrossEarningsAmount, payslip!.GrossEarnings.Value?.Amount);
        Assert.Equal(result.TotalDeductionsAmount, payslip.TotalDeductions.Value?.Amount);
        Assert.Equal(result.NetPayAmount, payslip.NetPay.Value?.Amount);

        // And the payslip's own lines add up to its own totals, so a reader can check it by hand.
        Assert.Equal(payslip.GrossEarnings.Value?.Amount, payslip.EarningLineTotal);
        Assert.Equal(payslip.TotalDeductions.Value?.Amount, payslip.DeductionLineTotal);
    }

    [Fact]
    public async Task Every_report_reconciles_to_the_persisted_payroll_result()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var results = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Where(e => e.PayrollRunId == runId && !e.IsExcluded)
            .ToListAsync();

        var reports = await Services(fixture).Reports.BuildAsync(runId);
        var usd = reports!.Summary.Single(s => s.CurrencyCode == "USD").Totals;

        var expectedGross = results.Sum(r => r.GrossEarningsAmount ?? 0m);
        var expectedNet = results.Sum(r => r.NetPayAmount ?? 0m);
        var expectedPaye = results.Sum(r => r.PayeAfterCreditsAmount ?? 0m);

        Assert.Equal(expectedGross, usd.Gross);
        Assert.Equal(expectedNet, usd.NetPay);
        Assert.Equal(expectedPaye, usd.Paye);

        // The register, the tax report, the NSSA report and the currency summary all describe the
        // same payroll, so they all carry the same totals.
        Assert.Equal(expectedGross, reports.Register.Single(s => s.CurrencyCode == "USD").Totals.Gross);
        Assert.Equal(expectedPaye,
            reports.Tax.Single(s => s.CurrencyCode == "USD").Totals.PayeAfterCredits);
        Assert.Equal(results.Sum(r => r.NssaEmployeeAmount ?? 0m),
            reports.Nssa.Single(s => s.CurrencyCode == "USD").Totals.EmployeeContribution);
        Assert.Equal(expectedGross,
            reports.CurrencySummary.Single(s => s.CurrencyCode == "USD").Gross);
    }

    [Fact]
    public async Task The_employee_earnings_report_reconciles_to_gross()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var reports = await Services(fixture).Reports.BuildAsync(runId);
        var earnings = reports!.EmployeeEarnings.Single(s => s.CurrencyCode == "USD");
        var summary = reports.Summary.Single(s => s.CurrencyCode == "USD").Totals;

        // Every earning line, summed, is gross. Lines excluded from gross — reimbursements —
        // would break this, which is why the engine flags them rather than netting them off.
        Assert.Equal(summary.Gross, earnings.Totals.Amount);
    }

    [Fact]
    public async Task Statutory_obligations_reconcile_to_the_payroll_result_and_to_their_own_lines()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var results = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Where(e => e.PayrollRunId == runId && !e.IsExcluded)
            .ToListAsync();

        var obligations = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.PayrollRunId == runId)
            .ToListAsync();

        var paye = obligations.Single(o =>
            o.ObligationType == Domain.Statutory.Obligations.StatutoryObligationType.Paye);
        Assert.Equal(results.Sum(r => r.PayeAfterCreditsAmount ?? 0m), paye.CalculatedAmount);

        var aids = obligations.Single(o =>
            o.ObligationType == Domain.Statutory.Obligations.StatutoryObligationType.AidsLevy);
        Assert.Equal(results.Sum(r => r.AidsLevyAmount ?? 0m), aids.CalculatedAmount);

        // And each obligation equals the sum of the individuals behind it, so a remittance can be
        // taken apart again when an authority queries one employee.
        foreach (var obligation in obligations.Where(o => o.Lines.Count > 0))
        {
            Assert.Equal(obligation.CalculatedAmount, obligation.Lines.Sum(l => l.Amount));
        }
    }

    [Fact]
    public async Task The_statutory_report_reconciles_to_the_obligation_register()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var obligations = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Where(o => o.PayrollRunId == runId && o.CurrencyCode == "USD")
            .ToListAsync();

        var reports = await Services(fixture).Reports.BuildAsync(runId);
        var statutory = reports!.Statutory.Single(s => s.CurrencyCode == "USD");

        Assert.Equal(obligations.Sum(o => o.CalculatedAmount), statutory.Totals.Calculated);
        Assert.Equal(obligations.Sum(o => o.Outstanding.Amount), statutory.Totals.Outstanding);
    }

    [Fact]
    public async Task Employer_cost_reconciles_to_gross_plus_employer_contributions()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var reports = await Services(fixture).Reports.BuildAsync(runId);
        var employer = reports!.EmployerCosts.Single(s => s.CurrencyCode == "USD");
        var currency = reports.CurrencySummary.Single(s => s.CurrencyCode == "USD");

        Assert.Equal(currency.Gross + currency.EmployerContributions, currency.TotalEmployerCost);
        Assert.Equal(currency.TotalEmployerCost, employer.Totals.Amount);
    }

    [Fact]
    public async Task Project_allocations_sum_to_the_total_employer_cost()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var results = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.CostAllocations)
            .Where(e => e.PayrollRunId == runId && !e.IsExcluded)
            .ToListAsync();

        foreach (var result in results.Where(r => r.CostAllocations.Count > 0))
        {
            // Allocations split one employee's cost; they must not create or lose any of it.
            Assert.Equal(100m, result.CostAllocations.Sum(a => a.Percent));
            Assert.Equal(
                result.TotalEmployerCostAmount,
                result.CostAllocations.Sum(a => a.AllocatedCostAmount));
        }
    }

    [Fact]
    public async Task Deductions_reconcile_to_the_total_deductions_on_the_result()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var results = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.DeductionLines)
            .Where(e => e.PayrollRunId == runId && !e.IsExcluded)
            .ToListAsync();

        foreach (var result in results)
        {
            Assert.Equal(result.TotalDeductionsAmount, result.DeductionLines.Sum(l => l.Amount));

            // Gross less deductions is net, on every employee, without exception.
            Assert.Equal(
                result.GrossEarningsAmount - result.TotalDeductionsAmount,
                result.NetPayAmount);
        }
    }

    [Fact]
    public async Task The_accounting_journal_balances_and_never_spans_currencies()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        await MapAccountsAsync(fixture, "USD");

        var journals = await Services(fixture).Journals.BuildAsync(runId);
        var journal = Assert.Single(journals);

        Assert.Equal("USD", journal.CurrencyCode);
        Assert.Empty(journal.Unmapped);
        Assert.True(journal.IsBalanced,
            $"Journal is out of balance by {journal.Imbalance:N2}.");
        Assert.Equal(0m, journal.Imbalance);

        // Every line belongs to this journal's currency by construction: there is no field on a
        // journal that could hold a line from another one.
        Assert.NotEmpty(journal.Lines);
    }

    [Fact]
    public async Task The_journal_reconciles_to_the_payroll_result()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        await MapAccountsAsync(fixture, "USD");

        var results = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Where(e => e.PayrollRunId == runId && !e.IsExcluded && e.IsCalculated)
            .ToListAsync();

        var journal = (await Services(fixture).Journals.BuildAsync(runId)).Single();

        var wages = journal.Lines.Single(l => l.MappingType == GlMappingType.WagesExpense);
        Assert.Equal(results.Sum(r => r.GrossEarningsAmount ?? 0m), wages.Debit);

        var paye = journal.Lines.Single(l => l.MappingType == GlMappingType.PayeLiability);
        Assert.Equal(results.Sum(r => r.PayeAfterCreditsAmount ?? 0m), paye.Credit);

        var net = journal.Lines.Single(l => l.MappingType == GlMappingType.NetPayLiability);
        Assert.Equal(results.Sum(r => r.NetPayAmount ?? 0m), net.Credit);
    }

    [Fact]
    public async Task An_unmapped_amount_is_reported_rather_than_posted_to_a_default()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        // No accounts mapped at all: every amount should be listed, and nothing invented.
        var journal = (await Services(fixture).Journals.BuildAsync(runId)).Single();

        Assert.Empty(journal.Lines);
        Assert.NotEmpty(journal.Unmapped);
        Assert.Contains(journal.Unmapped, u => u.Contains("Gross wages"));
    }

    /// <summary>
    /// The snapshot a run calculated from still hashes to what was stored, and still describes the
    /// figures the run produced. This is what lets an auditor reconstruct an old payroll.
    /// </summary>
    [Fact]
    public async Task The_stored_snapshot_still_matches_the_result_it_produced()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var result = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == runId);

        var record = await fixture.Db.Context.PayrollInputSnapshots.AsNoTracking()
            .SingleAsync(s => s.PayrollRunEmployeeId == result.Id);

        Assert.Equal(
            Application.Payroll.PayrollSnapshotStore.Hash(record.SnapshotJson),
            record.SnapshotHash);

        var snapshot = await Services(fixture).SnapshotStore.ReadAsync(result.Id);
        Assert.NotNull(snapshot);
        Assert.Equal(result.EmployeeId, snapshot!.EmployeeId);
        Assert.Equal(result.CurrencyCode, snapshot.PayrollCurrency.Value);
        Assert.Equal(result.ContractVersion, snapshot.ContractVersion);

        // Recalculating from the stored snapshot reproduces the stored figures exactly.
        var recalculated = new Payroll.Engine.PayrollCalculator().Calculate(snapshot);
        Assert.Equal(result.GrossEarningsAmount, recalculated.GrossEarnings?.Amount);
        Assert.Equal(result.NetPayAmount, recalculated.NetPay?.Amount);
        Assert.Equal(result.PayeAfterCreditsAmount, recalculated.PayeAfterCredits?.Amount);
    }

    private static async Task MapAccountsAsync(Fixture fixture, string currency)
    {
        var journals = Services(fixture).Journals;

        foreach (var type in Enum.GetValues<GlMappingType>())
        {
            await journals.SetMappingAsync(
                fixture.CompanyId, type, currency, $"{(int)type + 2000}", type.ToString(), null);
        }

        fixture.Db.Context.ChangeTracker.Clear();
    }
}
