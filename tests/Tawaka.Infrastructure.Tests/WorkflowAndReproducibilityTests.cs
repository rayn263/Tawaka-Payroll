using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Employees;
using Tawaka.Application.Security;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory;
using Tawaka.Domain.Statutory.Obligations;
using Tawaka.Infrastructure.Interceptors;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// The approve → finalise → paid → locked workflow, correction-run isolation, and the
/// reproducibility guarantees that make a historical payroll defensible.
/// </summary>
public class WorkflowTests : StatutoryObligationTests
{
    [Fact]
    public async Task A_run_cannot_be_finalised_before_it_is_approved()
    {
        using var fixture = await SetUpAsync();
        var run = (await fixture.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Runs.CalculateAsync(run.Id);

        var result = await fixture.Runs.FinaliseAsync(run.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("approved run can be finalised"));
    }

    [Fact]
    public async Task The_full_workflow_runs_through_to_locked()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        Assert.True((await fixture.Runs.MarkNetWagesPaidAsync(runId)).IsValid);
        Assert.True((await fixture.Runs.LockAsync(runId)).IsValid);

        var run = await fixture.Db.Context.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        Assert.Equal(PayrollRunStatus.Locked, run.Status);
        Assert.NotNull(run.ApprovedBy);
        Assert.NotNull(run.FinalisedBy);
        Assert.NotNull(run.PaidBy);
        Assert.NotNull(run.LockedBy);
    }

    [Fact]
    public async Task Net_wages_cannot_be_marked_paid_before_finalisation()
    {
        using var fixture = await SetUpAsync();
        var run = (await fixture.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Runs.CalculateAsync(run.Id);

        var result = await fixture.Runs.MarkNetWagesPaidAsync(run.Id);

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// Paying employees their net wages says nothing about the authorities: the obligations stay
    /// outstanding until each is separately evidenced.
    /// </summary>
    [Fact]
    public async Task Marking_net_wages_paid_leaves_statutory_obligations_outstanding()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        await fixture.Runs.MarkNetWagesPaidAsync(runId);

        var obligations = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Where(o => o.PayrollRunId == runId).ToListAsync();

        Assert.All(obligations, o => Assert.False(o.IsPaid));
        Assert.All(obligations, o => Assert.True(o.Outstanding.Amount > 0m));
    }

    [Fact]
    public async Task A_locked_run_is_immutable_at_the_data_layer()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        await fixture.Runs.LockAsync(runId);
        fixture.Db.Context.ChangeTracker.Clear();

        var locked = await fixture.Db.Context.PayrollRuns.SingleAsync(r => r.Id == runId);
        locked.Notes = "Tampered";

        Assert.Throws<PeriodLockedException>(() => fixture.Db.Context.SaveChanges());
    }

    /// <summary>
    /// Locking the run header is not enough. The figures are in the result rows, and if those stay
    /// writable then the lock protects nothing that matters.
    /// </summary>
    [Fact]
    public async Task A_locked_runs_result_figures_cannot_be_edited()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        await fixture.Runs.LockAsync(runId);
        fixture.Db.Context.ChangeTracker.Clear();

        var employee = await fixture.Db.Context.PayrollRunEmployees
            .FirstAsync(e => e.PayrollRunId == runId);
        employee.NetPayAmount = 1m;

        Assert.Throws<PeriodLockedException>(() => fixture.Db.Context.SaveChanges());
    }

    /// <summary>The same protection reaches the lines underneath the result row.</summary>
    [Fact]
    public async Task A_locked_runs_earning_lines_cannot_be_edited()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        await fixture.Runs.LockAsync(runId);
        fixture.Db.Context.ChangeTracker.Clear();

        var employeeIds = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Where(e => e.PayrollRunId == runId).Select(e => e.Id).ToListAsync();
        var line = await fixture.Db.Context.PayrollEarningLines
            .FirstAsync(l => employeeIds.Contains(l.PayrollRunEmployeeId));
        line.Amount = 1m;

        Assert.Throws<PeriodLockedException>(() => fixture.Db.Context.SaveChanges());
    }

    /// <summary>
    /// The guard must key on the run's lock, not on the entity type: an unlocked run's figures are
    /// still correctable, which is the whole point of the Review stage.
    /// </summary>
    [Fact]
    public async Task An_unlocked_runs_result_figures_can_still_be_edited()
    {
        using var fixture = await SetUpAsync();
        var run = (await fixture.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Runs.CalculateAsync(run.Id);
        fixture.Db.Context.ChangeTracker.Clear();

        var employee = await fixture.Db.Context.PayrollRunEmployees
            .FirstAsync(e => e.PayrollRunId == run.Id);
        employee.ExclusionReason = "Checked against the timesheet";

        fixture.Db.Context.SaveChanges();
    }

    /// <summary>A statutory payment is a later event and remains possible after the run is locked.</summary>
    [Fact]
    public async Task A_statutory_payment_can_still_be_recorded_after_the_run_is_locked()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var paye = await fixture.Db.Context.StatutoryObligations
            .SingleAsync(o => o.PayrollRunId == runId && o.ObligationType == StatutoryObligationType.Paye);
        await fixture.Obligations.ApproveAsync(paye.Id);
        await fixture.Runs.LockAsync(runId);
        fixture.Db.Context.ChangeTracker.Clear();

        var result = await fixture.Obligations.RecordPaymentAsync(
            new Application.Statutory.Obligations.RecordPaymentRequest
            {
                ObligationId = paye.Id, Amount = paye.CalculatedAmount, CurrencyCode = "USD",
                PaymentDate = new DateOnly(2026, 10, 8), PaymentReference = "RTGS-AFTER-LOCK"
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Finalising_requires_the_finalise_permission()
    {
        using var fixture = await SetUpAsync(
            TestUser.WithPermissions(Permissions.PayrollView, Permissions.PayrollCreate,
                Permissions.PayrollCalculate, Permissions.PayrollApprove,
                Permissions.EmployeesView, Permissions.EmployeesEdit,
                Permissions.EmployeesEditSalary, Permissions.StatutoryView));

        var run = (await fixture.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Runs.CalculateAsync(run.Id);

        var stored = await fixture.Db.Context.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        stored.CalculatedBy = "someone-else";
        await fixture.Db.Context.SaveChangesAsync();
        await fixture.Runs.ApproveAsync(run.Id);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => fixture.Runs.FinaliseAsync(run.Id));
    }
}

/// <summary>
/// Reproducibility: a historical payroll must survive later changes to salaries, tax rules and
/// exchange rates, and a correction run must not disturb the original.
/// </summary>
public class ReproducibilityTests : StatutoryObligationTests
{
    [Fact]
    public async Task The_rule_snapshot_used_by_a_run_is_retained_and_readable()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var run = await fixture.Db.Context.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId);

        Assert.False(string.IsNullOrWhiteSpace(run.RuleSnapshot));
        Assert.Contains("PAYE-USD-2026-MONTHLY", run.RuleSnapshot!);
        Assert.Contains("NSSA-POBS-2026-USD", run.RuleSnapshot!);
        Assert.False(string.IsNullOrWhiteSpace(run.EngineVersion));
    }

    /// <summary>
    /// A correction run for the same period is a separate run with its own results and its own
    /// obligations. The original is untouched.
    /// </summary>
    [Fact]
    public async Task A_correction_run_does_not_disturb_the_original()
    {
        using var fixture = await SetUpAsync();
        var originalRunId = await RunThroughFinaliseAsync(fixture);

        var originalFigures = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == originalRunId);
        var originalObligations = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Where(o => o.PayrollRunId == originalRunId)
            .Select(o => new { o.ObligationType, o.CalculatedAmount }).ToListAsync();

        // A correction run, after a salary change effective within the same period.
        var contracts = new EmployeeContractService(fixture.Db.Context, fixture.Db.User);
        await contracts.SupersedeAsync(fixture.Employee.Id,
            NewContract(fixture.CompanyId, fixture.Employee.Id, fixture.EmploymentTypeId, 1200m),
            new DateOnly(2026, 9, 15), "Backpay correction");

        var correction = (await fixture.Runs.CreateRunAsync(
            fixture.Period.Id, PayrollRunType.Correction)).Value!;
        await fixture.Runs.CalculateAsync(correction.Id);

        // The correction sees the new terms.
        var correctionFigures = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == correction.Id);
        Assert.Equal(1200m, correctionFigures.GrossEarningsAmount);

        // The original run is untouched — figures and obligations both.
        var originalAfter = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == originalRunId);
        Assert.Equal(originalFigures.GrossEarningsAmount, originalAfter.GrossEarningsAmount);
        Assert.Equal(originalFigures.NetPayAmount, originalAfter.NetPayAmount);
        Assert.Equal(850m, originalAfter.GrossEarningsAmount);

        var obligationsAfter = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Where(o => o.PayrollRunId == originalRunId)
            .Select(o => new { o.ObligationType, o.CalculatedAmount }).ToListAsync();
        Assert.Equal(originalObligations.Count, obligationsAfter.Count);
        foreach (var original in originalObligations)
        {
            Assert.Contains(obligationsAfter,
                o => o.ObligationType == original.ObligationType &&
                     o.CalculatedAmount == original.CalculatedAmount);
        }

        Assert.Equal(2, correction.RunNumber);
    }

    /// <summary>A correction run creates its own obligations, separate from the original's.</summary>
    [Fact]
    public async Task A_correction_run_has_its_own_obligations()
    {
        using var fixture = await SetUpAsync();
        var originalRunId = await RunThroughFinaliseAsync(fixture);

        var correction = (await fixture.Runs.CreateRunAsync(
            fixture.Period.Id, PayrollRunType.Correction)).Value!;
        await fixture.Runs.CalculateAsync(correction.Id);

        var stored = await fixture.Db.Context.PayrollRuns.SingleAsync(r => r.Id == correction.Id);
        stored.CalculatedBy = "u-officer";
        await fixture.Db.Context.SaveChangesAsync();
        await fixture.Runs.ApproveAsync(correction.Id);
        await fixture.Runs.FinaliseAsync(correction.Id);

        var originalCount = await fixture.Db.Context.StatutoryObligations
            .CountAsync(o => o.PayrollRunId == originalRunId);
        var correctionCount = await fixture.Db.Context.StatutoryObligations
            .CountAsync(o => o.PayrollRunId == correction.Id);

        Assert.True(originalCount > 0);
        Assert.True(correctionCount > 0);

        // Each obligation belongs to exactly one run; the unique index prevents double-counting.
        var all = await fixture.Db.Context.StatutoryObligations.AsNoTracking().ToListAsync();
        Assert.Equal(all.Count, all.Select(o => new { o.PayrollRunId, o.ObligationType, o.CurrencyCode })
            .Distinct().Count());
    }

    /// <summary>Changing an exchange rate today cannot alter a historical payroll.</summary>
    [Fact]
    public async Task Changing_an_exchange_rate_does_not_alter_a_finalised_run()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var before = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == runId);

        fixture.Db.Context.ExchangeRates.Add(new Domain.Currencies.ExchangeRate
        {
            FromCurrency = "USD", ToCurrency = "ZWG", Rate = 99m, Source = "Test",
            RateDate = new DateOnly(2026, 9, 30), EffectiveFrom = new DateOnly(2026, 9, 1)
        });
        await fixture.Db.Context.SaveChangesAsync();

        var after = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == runId);

        Assert.Equal(before.GrossEarningsAmount, after.GrossEarningsAmount);
        Assert.Equal(before.NetPayAmount, after.NetPayAmount);
    }

    /// <summary>The trace of a finalised run is retained in full.</summary>
    [Fact]
    public async Task The_calculation_trace_survives_finalisation()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var runEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.TraceEntries)
            .SingleAsync(e => e.PayrollRunId == runId);

        Assert.NotEmpty(runEmployee.TraceEntries);
        var paye = runEmployee.TraceEntries.Single(t => t.ItemKey == "PAYE");
        Assert.Equal("PAYE-USD-2026-MONTHLY", paye.RuleId);
        Assert.Equal(nameof(VerificationStatus.Verified), paye.VerificationStatus);
    }

    /// <summary>
    /// Everything a future ZIMRA or NSSA return needs is already queryable, which is what keeps the
    /// submission work a formatting exercise rather than a remodelling one.
    /// </summary>
    [Fact]
    public async Task The_data_a_statutory_return_needs_is_queryable()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var returnData = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.PayrollRunId == runId && o.ObligationType == StatutoryObligationType.Paye)
            .SelectMany(o => o.Lines)
            .Select(l => new
            {
                l.EmployeeNumber, l.EmployeeName, l.TaxNumber, l.Amount, l.CurrencyCode
            })
            .ToListAsync();

        Assert.NotEmpty(returnData);
        Assert.All(returnData, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.EmployeeNumber));
            Assert.False(string.IsNullOrWhiteSpace(row.TaxNumber));
            Assert.False(string.IsNullOrWhiteSpace(row.CurrencyCode));
            Assert.True(row.Amount > 0m);
        });

        var company = await fixture.Db.Context.Companies.AsNoTracking().FirstAsync();
        Assert.NotNull(company);
    }
}
