using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Employees;
using Tawaka.Application.Loans;
using Tawaka.Application.Payroll;
using Tawaka.Application.Time;
using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// The stored input snapshot: what the engine was actually given, kept.
/// <para>
/// Milestone 3 made the snapshot immutable in memory. Once payroll consumes approved time, leave
/// and a loan balance that is <em>not</em> enough, because those inputs move on. These tests hold
/// the line that a completed run reproduces against the inputs it had (ADR-035).
/// </para>
/// </summary>
public class InputSnapshotTests : EmployeeTestBase
{
    protected sealed record Fixture(
        TestDatabase Db, Guid CompanyId, Employee Employee, PayrollPeriod Period,
        PayrollServices Services) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Fixture> SetUpAsync()
    {
        var (db, companyId, permanentTypeId, _) = await EmployeeTestBase.SetUpAsync();

        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        await contracts.CreateInitialAsync(NewContract(companyId, employee.Id, permanentTypeId, 850m));

        var period = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-09", Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30), TaxYear = 2026, Mode = PayrollMode.Development
        };
        db.Context.PayrollPeriods.Add(period);
        await db.Context.SaveChangesAsync();

        return new Fixture(db, companyId, employee, period, PayrollServices.For(db));
    }

    [Fact]
    public async Task Calculating_stores_and_seals_the_snapshot_it_ran_on()
    {
        using var fixture = await SetUpAsync();

        var run = (await fixture.Services.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Services.Runs.CalculateAsync(run.Id);

        var record = await fixture.Db.Context.PayrollInputSnapshots.AsNoTracking()
            .SingleAsync(s => s.PayrollRunId == run.Id);

        Assert.True(record.IsSealed);
        Assert.NotNull(record.SealedAt);
        Assert.NotEmpty(record.SnapshotJson);
        Assert.Equal(64, record.SnapshotHash.Length);
        Assert.Equal(PayrollSnapshotStore.Hash(record.SnapshotJson), record.SnapshotHash);
    }

    [Fact]
    public async Task A_stored_snapshot_round_trips_with_its_figures_and_currency_intact()
    {
        using var fixture = await SetUpAsync();
        var run = (await fixture.Services.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Services.Runs.CalculateAsync(run.Id);

        var runEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == run.Id);

        var restored = await fixture.Services.SnapshotStore.ReadAsync(runEmployee.Id);

        Assert.NotNull(restored);
        Assert.Equal(fixture.Employee.Id, restored!.EmployeeId);
        Assert.Equal("USD", restored.PayrollCurrency.Value);
        Assert.Equal(850m, restored.ContractRate!.Value.Amount);

        // The currency travels with every amount, through serialisation as everywhere else.
        Assert.All(restored.Earnings, e => Assert.Equal("USD", e.Amount.Currency.Value));
    }

    /// <summary>
    /// A stored snapshot whose content no longer matches its hash has been altered outside the
    /// application. Calculating from it would produce a figure nobody could account for.
    /// </summary>
    [Fact]
    public async Task A_tampered_snapshot_is_refused_rather_than_used()
    {
        using var fixture = await SetUpAsync();
        var run = (await fixture.Services.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Services.Runs.CalculateAsync(run.Id);
        fixture.Db.Context.ChangeTracker.Clear();

        var record = await fixture.Db.Context.PayrollInputSnapshots
            .SingleAsync(s => s.PayrollRunId == run.Id);
        record.SnapshotJson = record.SnapshotJson.Replace("850", "8500");
        await fixture.Db.Context.SaveChangesAsync();
        fixture.Db.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Services.SnapshotStore.ReadAsync(record.PayrollRunEmployeeId));
    }

    [Fact]
    public async Task The_same_snapshot_always_serialises_to_the_same_bytes()
    {
        using var fixture = await SetUpAsync();
        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        var first = PayrollSnapshotStore.Serialise(snapshot!);
        var second = PayrollSnapshotStore.Serialise(snapshot!);

        // Without this the hash would be meaningless and tamper detection would fire at random.
        Assert.Equal(first, second);
        Assert.Equal(PayrollSnapshotStore.Hash(first), PayrollSnapshotStore.Hash(second));
    }

    [Fact]
    public async Task Every_approved_input_the_run_consumed_is_recorded_and_queryable()
    {
        using var fixture = await SetUpAsync();
        var timesheetId = await ApprovedTimesheetAsync(fixture);

        var run = (await fixture.Services.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Services.Runs.CalculateAsync(run.Id);

        var sources = await fixture.Services.SnapshotStore.GetInputSourcesAsync(run.Id);
        Assert.Contains(sources, s => s.InputType == "Timesheet" && s.InputId == timesheetId);
        Assert.Contains(sources, s => s.InputType == "HolidayCalendar");

        // And the reverse question: which runs consumed this timesheet?
        var runs = await fixture.Services.SnapshotStore
            .GetRunsConsumingAsync("Timesheet", timesheetId);
        Assert.Equal(run.Id, Assert.Single(runs).PayrollRunId);
    }

    /// <summary>
    /// The case the stored snapshot exists for: a loan balance moves after the run, and the run
    /// must still show the balance it deducted against.
    /// </summary>
    [Fact]
    public async Task A_later_loan_repayment_does_not_change_what_a_completed_run_recorded()
    {
        using var fixture = await SetUpAsync();
        var loanId = await DisbursedLoanAsync(fixture);

        var run = (await fixture.Services.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Services.Runs.CalculateAsync(run.Id);
        var runEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == run.Id);

        await fixture.Services.Loans.RecordManualRepaymentAsync(
            loanId, 400m, new DateOnly(2026, 10, 15), "CASH-009", isEarlySettlement: false);
        fixture.Db.Context.ChangeTracker.Clear();

        var stored = await fixture.Services.SnapshotStore.ReadAsync(runEmployee.Id);
        var loanInput = Assert.Single(stored!.LoanDeductions);

        Assert.Equal(600m, loanInput.OutstandingBefore.Amount);
        Assert.Equal(100m, loanInput.Amount.Amount);
    }

    [Fact]
    public async Task Approved_time_that_is_later_corrected_does_not_change_a_completed_run()
    {
        using var fixture = await SetUpAsync();
        var timesheetId = await ApprovedTimesheetAsync(fixture);

        var run = (await fixture.Services.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Services.Runs.CalculateAsync(run.Id);
        var runEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == run.Id);

        var correction = (await fixture.Services.Timesheets
            .CreateCorrectionAsync(timesheetId, "More days were worked.")).Value!;
        await fixture.Services.Timesheets.SetEntriesAsync(correction.Id, new[]
        {
            Day(1), Day(2), Day(3), Day(4), Day(5)
        });
        await fixture.Services.Timesheets.SubmitAsync(correction.Id);
        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        await approver.Timesheets.ApproveAsync(correction.Id);
        fixture.Db.Context.ChangeTracker.Clear();

        var stored = await fixture.Services.SnapshotStore.ReadAsync(runEmployee.Id);

        // The original run still shows the timesheet it consumed, with the hours it consumed.
        Assert.Equal(timesheetId, stored!.Timesheet!.TimesheetId);
        Assert.Equal(16m, stored.Timesheet.HoursWorked);
    }

    [Fact]
    public async Task A_second_run_over_the_same_period_reads_the_corrected_time()
    {
        using var fixture = await SetUpAsync();
        var timesheetId = await ApprovedTimesheetAsync(fixture);

        var first = (await fixture.Services.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Services.Runs.CalculateAsync(first.Id);

        var correction = (await fixture.Services.Timesheets
            .CreateCorrectionAsync(timesheetId, "More days were worked.")).Value!;
        await fixture.Services.Timesheets.SetEntriesAsync(correction.Id, new[]
        {
            Day(1), Day(2), Day(3), Day(4), Day(5)
        });
        await fixture.Services.Timesheets.SubmitAsync(correction.Id);
        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        await approver.Timesheets.ApproveAsync(correction.Id);
        fixture.Db.Context.ChangeTracker.Clear();

        var second = (await fixture.Services.Runs
            .CreateRunAsync(fixture.Period.Id, PayrollRunType.Correction)).Value!;
        await fixture.Services.Runs.CalculateAsync(second.Id);

        var secondEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == second.Id);
        var storedSecond = await fixture.Services.SnapshotStore.ReadAsync(secondEmployee.Id);

        Assert.Equal(correction.Id, storedSecond!.Timesheet!.TimesheetId);
        Assert.Equal(40m, storedSecond.Timesheet.HoursWorked);

        // And the first run is untouched by any of it.
        var firstEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == first.Id);
        var storedFirst = await fixture.Services.SnapshotStore.ReadAsync(firstEmployee.Id);
        Assert.Equal(timesheetId, storedFirst!.Timesheet!.TimesheetId);
        Assert.Equal(16m, storedFirst.Timesheet.HoursWorked);
    }

    [Fact]
    public async Task The_snapshot_carries_the_rule_versions_it_resolved()
    {
        using var fixture = await SetUpAsync();
        await ApprovedTimesheetAsync(fixture, overtimeHours: 4m);

        var run = (await fixture.Services.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Services.Runs.CalculateAsync(run.Id);
        var runEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == run.Id);

        var stored = await fixture.Services.SnapshotStore.ReadAsync(runEmployee.Id);

        Assert.NotNull(stored!.Rules.PayDivisor);
        Assert.Equal("PAY-DIVISOR-2026-MONTHLY", stored.Rules.PayDivisor!.RuleId);
        Assert.Contains(stored.Rules.Overtime, r => r.CategoryCode == "OT_WEEKDAY");
    }

    private static TimeEntryRequest Day(int day, decimal overtimeHours = 0m) => new()
    {
        WorkDate = new DateOnly(2026, 9, day),
        OrdinaryHours = 8m,
        DaysWorked = 1m,
        Overtime = overtimeHours == 0m
            ? new Dictionary<string, decimal>()
            : new Dictionary<string, decimal> { ["OT_WEEKDAY"] = overtimeHours }
    };

    private static async Task<Guid> ApprovedTimesheetAsync(
        Fixture fixture, decimal overtimeHours = 0m)
    {
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;
        await timesheets.SetEntriesAsync(sheet.Id, new[] { Day(1, overtimeHours), Day(2) });
        await timesheets.SubmitAsync(sheet.Id);

        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        Assert.True((await approver.Timesheets.ApproveAsync(sheet.Id)).IsValid);

        fixture.Db.Context.ChangeTracker.Clear();
        return sheet.Id;
    }

    private static async Task<Guid> DisbursedLoanAsync(Fixture fixture)
    {
        var loan = (await fixture.Services.Loans.CreateAsync(new LoanCommand
        {
            EmployeeId = fixture.Employee.Id,
            CurrencyCode = "USD",
            PrincipalAmount = 600m,
            InstalmentCount = 6,
            FirstInstalmentDate = new DateOnly(2026, 9, 30)
        })).Value!;

        await fixture.Services.Loans.SubmitAsync(loan.Id);
        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        await approver.Loans.ApproveAsync(loan.Id);
        await fixture.Services.Loans
            .DisburseAsync(loan.Id, new DateOnly(2026, 8, 31), "FBC-RTGS-77012");

        fixture.Db.Context.ChangeTracker.Clear();
        return loan.Id;
    }
}
