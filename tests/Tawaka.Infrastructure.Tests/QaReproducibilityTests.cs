using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Employees;
using Tawaka.Application.Leave;
using Tawaka.Application.Loans;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// A payroll that has been run must stay reproducible while the business moves on around it.
/// <para>
/// Salaries change, people are renamed, loans are taken out, leave is booked and statutory rules
/// are superseded. None of that may alter what a past payroll produced, and re-running the stored
/// snapshot must still give the same figures to the cent — because that is what an auditor asking
/// "how did you arrive at this?" two years later is entitled to.
/// </para>
/// </summary>
public class QaReproducibilityTests : PayrollFixtureBase
{
    [Fact]
    public async Task A_historical_run_is_reproducible_after_everything_around_it_has_changed()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var services = PayrollServices.For(fixture.Db);

        var original = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.EarningLines)
            .Include(e => e.DeductionLines)
            .SingleAsync(e => e.PayrollRunId == runId);

        var snapshotRecord = await fixture.Db.Context.PayrollInputSnapshots.AsNoTracking()
            .SingleAsync(s => s.PayrollRunEmployeeId == original.Id);

        var originalHash = snapshotRecord.SnapshotHash;
        var originalGross = original.GrossEarningsAmount;
        var originalNet = original.NetPayAmount;
        var originalPaye = original.PayeAfterCreditsAmount;
        var originalNssa = original.NssaEmployeeAmount;
        var originalLineCount = original.EarningLines.Count + original.DeductionLines.Count;

        // ---- Now change everything the payroll was built from -----------------------------------
        fixture.Db.Context.ChangeTracker.Clear();

        // The employee's own details.
        var employee = await fixture.Db.Context.Employees.SingleAsync(e => e.Id == fixture.Employee.Id);
        employee.LastName = "Moyo-Ncube";
        employee.Phone = "+263 77 000 0000";
        await fixture.Db.Context.SaveChangesAsync();

        // A new contract version at a much higher salary.
        var contracts = new EmployeeContractService(fixture.Db.Context, fixture.Db.User);
        var newTerms = NewContract(
            fixture.CompanyId, fixture.Employee.Id, fixture.EmploymentTypeId, 2400m);
        var superseded = await contracts.SupersedeAsync(
            fixture.Employee.Id, newTerms, new DateOnly(2026, 10, 1), "Promotion.");
        Assert.True(superseded.Succeeded, superseded.Validation.ToString());

        // A loan, which changes what the employee owes from now on.
        fixture.Db.Context.ChangeTracker.Clear();
        var loan = (await services.Loans.CreateAsync(new LoanCommand
        {
            EmployeeId = fixture.Employee.Id,
            CurrencyCode = "USD",
            PrincipalAmount = 900m,
            InstalmentCount = 3,
            FirstInstalmentDate = new DateOnly(2026, 10, 30)
        })).Value!;
        Assert.True((await services.Loans.SubmitAsync(loan.Id)).IsValid);

        // Leave, which changes the balance the employee carries.
        fixture.Db.Context.ChangeTracker.Clear();
        var unpaid = await fixture.Db.Context.LeaveTypes.AsNoTracking().FirstAsync(t => t.Code == "UNPAID");
        var leave = (await services.Leave.RequestAsync(new LeaveRequestCommand
        {
            EmployeeId = fixture.Employee.Id,
            LeaveTypeId = unpaid.Id,
            StartDate = new DateOnly(2026, 11, 2),
            EndDate = new DateOnly(2026, 11, 6)
        })).Value!;
        Assert.True((await services.Leave.SubmitAsync(leave.Id)).IsValid);

        // A statutory rule superseded from a later date: permitted, and not retrospective.
        fixture.Db.Context.ChangeTracker.Clear();
        var currentPaye = await fixture.Db.Context.TaxRules
            .Include(r => r.Brackets)
            .FirstAsync(r => r.Currency == "USD" && r.PeriodBasis == PeriodBasis.Monthly);

        var replacement = new TaxRule
        {
            RuleId = $"{currentPaye.RuleId}-REVISED",
            Name = "Revised monthly table",
            Currency = "USD",
            TaxYear = 2027,
            PeriodBasis = PeriodBasis.Monthly,
            CalculationMethod = currentPaye.CalculationMethod,
            BracketApplication = currentPaye.BracketApplication,
            EffectiveFrom = new DateOnly(2027, 1, 1),
            VerificationStatus = VerificationStatus.Verified,
            Source = new RuleSource { Source = "QA scenario" }
        };
        replacement.Brackets.Add(new TaxBracket
        {
            Sequence = 1, LowerBound = 0m, UpperBound = null, Rate = 0.40m
        });
        fixture.Db.Context.StatutoryRules.Add(replacement);
        await fixture.Db.Context.SaveChangesAsync();
        fixture.Db.Context.ChangeTracker.Clear();

        // ---- The historical run must be exactly where it was -------------------------------------
        var afterwards = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.EarningLines)
            .Include(e => e.DeductionLines)
            .SingleAsync(e => e.PayrollRunId == runId);

        Assert.Equal(originalGross, afterwards.GrossEarningsAmount);
        Assert.Equal(originalNet, afterwards.NetPayAmount);
        Assert.Equal(originalPaye, afterwards.PayeAfterCreditsAmount);
        Assert.Equal(originalNssa, afterwards.NssaEmployeeAmount);
        Assert.Equal(
            originalLineCount,
            afterwards.EarningLines.Count + afterwards.DeductionLines.Count);

        // The name on the result is the name at the time, not today's name.
        Assert.Equal(original.EmployeeName, afterwards.EmployeeName);
        Assert.DoesNotContain("Moyo-Ncube", afterwards.EmployeeName);

        // The snapshot is byte-for-byte what it was.
        var storedNow = await fixture.Db.Context.PayrollInputSnapshots.AsNoTracking()
            .SingleAsync(s => s.PayrollRunEmployeeId == original.Id);
        Assert.Equal(originalHash, storedNow.SnapshotHash);
        Assert.Equal(
            Application.Payroll.PayrollSnapshotStore.Hash(storedNow.SnapshotJson),
            storedNow.SnapshotHash);

        // And re-running it reproduces the original figures exactly.
        var snapshot = await services.SnapshotStore.ReadAsync(original.Id);
        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot!.ContractVersion);

        var recalculated = new Payroll.Engine.PayrollCalculator().Calculate(snapshot);
        Assert.Equal(originalGross, recalculated.GrossEarnings?.Amount);
        Assert.Equal(originalNet, recalculated.NetPay?.Amount);
        Assert.Equal(originalPaye, recalculated.PayeAfterCredits?.Amount);
        Assert.Equal(originalNssa, recalculated.NssaEmployee?.Amount);
    }

    /// <summary>
    /// The next payroll, by contrast, must see the new terms. Reproducibility is not inertia.
    /// </summary>
    [Fact]
    public async Task The_following_period_uses_the_new_contract_version()
    {
        using var fixture = await SetUpAsync();
        await RunThroughFinaliseAsync(fixture);

        var contracts = new EmployeeContractService(fixture.Db.Context, fixture.Db.User);
        var newTerms = NewContract(
            fixture.CompanyId, fixture.Employee.Id, fixture.EmploymentTypeId, 2400m);
        Assert.True((await contracts.SupersedeAsync(
            fixture.Employee.Id, newTerms, new DateOnly(2026, 10, 1), "Promotion.")).Succeeded);

        fixture.Db.Context.ChangeTracker.Clear();

        var october = new Domain.Payroll.PayrollPeriod
        {
            CompanyId = fixture.CompanyId, Code = "2026-10", Name = "October 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31),
            PayDate = new DateOnly(2026, 10, 30), TaxYear = 2026,
            Mode = Domain.Payroll.PayrollMode.Live
        };
        fixture.Db.Context.PayrollPeriods.Add(october);
        await fixture.Db.Context.SaveChangesAsync();
        fixture.Db.Context.ChangeTracker.Clear();

        var run = (await fixture.Runs.CreateRunAsync(october.Id)).Value!;
        Assert.True((await fixture.Runs.CalculateAsync(run.Id)).Succeeded);

        var result = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.EarningLines)
            .SingleAsync(e => e.PayrollRunId == run.Id);

        Assert.Equal(2, result.ContractVersion);
        Assert.Equal(2400m, result.EarningLines.Single(l => l.Code == "BASIC").Amount);
    }
}
