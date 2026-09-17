using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Employees;
using Tawaka.Domain.Employees;
using Xunit;
using Xunit.Abstractions;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// How the application behaves at the sizes it is actually for: a Zimbabwean contractor with ten,
/// fifty or a hundred people on the payroll.
/// <para>
/// The bounds here are deliberately loose. The point is not to hold the code to a millisecond
/// budget on a shared build machine, it is to catch the kind of regression that turns a payroll
/// run into a coffee break — an N+1 query, or work that grows with the square of the headcount.
/// The measured figures are written to the test output and quoted in the QA report.
/// </para>
/// </summary>
public class QaPerformanceTests : PayrollFixtureBase
{
    private readonly ITestOutputHelper _output;

    public QaPerformanceTests(ITestOutputHelper output) => _output = output;

    private static async Task AddEmployeesAsync(Fixture fixture, int count)
    {
        var employees = new EmployeeService(
            fixture.Db.Context, fixture.Db.User, fixture.Db.Clock);
        var contracts = new EmployeeContractService(fixture.Db.Context, fixture.Db.User);

        for (var i = 1; i <= count; i++)
        {
            var employee = (await employees.CreateAsync(new Employee
            {
                CompanyId = fixture.CompanyId,
                EmployeeNumber = $"PERF-{i:D4}",
                FirstName = "Perf",
                LastName = $"Employee {i:D4}",
                NationalId = $"63-{i:D7} P {i % 90:D2}",
                HireDate = new DateOnly(2024, 1, 8),
                Status = EmployeeStatus.Active
            })).Value!;

            await contracts.CreateInitialAsync(NewContract(
                fixture.CompanyId, employee.Id, fixture.EmploymentTypeId, 700m + i));

            fixture.Db.Context.EmployeeStatutoryProfiles.Add(new EmployeeStatutoryProfile
            {
                EmployeeId = employee.Id,
                TaxNumber = $"BP{i:D5}",
                NssaNumber = $"NSSA{i:D5}"
            });
        }

        await fixture.Db.Context.SaveChangesAsync();
        fixture.Db.Context.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task A_payroll_of_this_size_calculates_and_reports_in_reasonable_time(int headcount)
    {
        using var fixture = await SetUpAsync();

        // The fixture already has one employee; top up to the headcount under test.
        await AddEmployeesAsync(fixture, headcount - 1);

        var services = PayrollServices.For(fixture.Db);
        var run = (await fixture.Runs.CreateRunAsync(fixture.Period.Id)).Value!;

        var calculating = Stopwatch.StartNew();
        var calculated = await fixture.Runs.CalculateAsync(run.Id);
        calculating.Stop();

        Assert.True(calculated.Succeeded, calculated.Validation.ToString());
        Assert.Equal(
            headcount,
            await fixture.Db.Context.PayrollRunEmployees.CountAsync(e => e.PayrollRunId == run.Id));

        fixture.Db.Context.ChangeTracker.Clear();

        // Approval must be a different person from whoever calculated it.
        var stored = await fixture.Db.Context.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        stored.CalculatedBy = "u-officer";
        await fixture.Db.Context.SaveChangesAsync();
        Assert.True((await fixture.Runs.ApproveAsync(run.Id)).IsValid);

        fixture.Db.Context.ChangeTracker.Clear();
        var finalising = Stopwatch.StartNew();
        Assert.True((await fixture.Runs.FinaliseAsync(run.Id)).IsValid);
        finalising.Stop();

        fixture.Db.Context.ChangeTracker.Clear();
        var reporting = Stopwatch.StartNew();
        var reports = await services.Reports.BuildAsync(run.Id);
        reporting.Stop();

        Assert.NotNull(reports);

        var payslipping = Stopwatch.StartNew();
        var firstEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .FirstAsync(e => e.PayrollRunId == run.Id);
        var payslip = await services.Payslips.BuildAsync(firstEmployee.Id);
        payslipping.Stop();

        Assert.NotNull(payslip);

        _output.WriteLine(
            $"{headcount,4} employees | calculate {calculating.ElapsedMilliseconds,6} ms | " +
            $"finalise {finalising.ElapsedMilliseconds,6} ms | " +
            $"reports {reporting.ElapsedMilliseconds,6} ms | " +
            $"payslip {payslipping.ElapsedMilliseconds,5} ms");

        // Loose ceilings: a hundred-person payroll that takes a minute to calculate is broken,
        // whatever machine it is running on.
        Assert.True(calculating.ElapsedMilliseconds < 60_000,
            $"Calculating {headcount} employees took {calculating.ElapsedMilliseconds} ms.");
        Assert.True(reporting.ElapsedMilliseconds < 30_000,
            $"Reporting on {headcount} employees took {reporting.ElapsedMilliseconds} ms.");
        Assert.True(payslipping.ElapsedMilliseconds < 5_000,
            $"One payslip took {payslipping.ElapsedMilliseconds} ms.");
    }
}
