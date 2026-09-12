using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Employees;
using Tawaka.Application.Security;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Security;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>Shared set-up: a seeded company with one employment type resolved.</summary>
public abstract class EmployeeTestBase
{
    protected static async Task<(TestDatabase Db, Guid CompanyId, Guid PermanentTypeId, Guid CasualTypeId)>
        SetUpAsync(ICurrentUser? user = null)
    {
        var db = new TestDatabase(user);
        var outcome = await db.SeedAllAsync();

        var permanent = await db.Context.EmploymentTypes.AsNoTracking()
            .SingleAsync(t => t.Code == "Permanent");
        var casual = await db.Context.EmploymentTypes.AsNoTracking()
            .SingleAsync(t => t.Code == "Contract");

        return (db, outcome.Company.Id, permanent.Id, casual.Id);
    }

    protected static Employee NewEmployee(Guid companyId, string number = "EMP-0031") => new()
    {
        CompanyId = companyId,
        EmployeeNumber = number,
        FirstName = "John",
        LastName = "Moyo",
        NationalId = "63-1234567 X 42",
        DateOfBirth = new DateOnly(1988, 4, 2),
        HireDate = new DateOnly(2021, 3, 4),
        Status = EmployeeStatus.Active
    };

    protected static EmployeeContract NewContract(
        Guid companyId, Guid employeeId, Guid employmentTypeId,
        decimal salary = 850m, string currency = "USD") => new()
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            EmploymentTypeId = employmentTypeId,
            StartDate = new DateOnly(2021, 3, 4),
            PayrollCurrency = currency,
            PaymentFrequency = PaymentFrequency.Monthly,
            EarningsBasis = EarningsBasis.MonthlySalary,
            MonthlyRate = salary,
            StandardHoursPerDay = 8m,
            StandardDaysPerWeek = 5m
        };
}

public class EmployeeServiceTests : EmployeeTestBase
{
    [Fact]
    public async Task An_employee_can_be_created()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;
        var service = new EmployeeService(db.Context, db.User, db.Clock);

        var result = await service.CreateAsync(NewEmployee(companyId));

        Assert.True(result.Succeeded);
        Assert.Equal("John Moyo", result.Value!.FullName);
        Assert.Equal(1, await db.Context.Employees.CountAsync());
    }

    [Fact]
    public async Task A_duplicate_employee_number_is_refused()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;
        var service = new EmployeeService(db.Context, db.User, db.Clock);
        await service.CreateAsync(NewEmployee(companyId));

        var second = NewEmployee(companyId);
        second.NationalId = "63-7654321 A 11";
        var result = await service.CreateAsync(second);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors,
            e => e.Field == nameof(Employee.EmployeeNumber));
    }

    [Fact]
    public async Task A_duplicate_national_id_is_refused()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;
        var service = new EmployeeService(db.Context, db.User, db.Clock);
        await service.CreateAsync(NewEmployee(companyId));

        var result = await service.CreateAsync(NewEmployee(companyId, "EMP-0032"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Field == nameof(Employee.NationalId));
    }

    [Fact]
    public async Task Creating_an_employee_records_the_engagement_in_status_history()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;
        var service = new EmployeeService(db.Context, db.User, db.Clock);

        var result = await service.CreateAsync(NewEmployee(companyId));

        var history = await db.Context.EmployeeStatusHistory.AsNoTracking()
            .SingleAsync(h => h.EmployeeId == result.Value!.Id);
        Assert.Equal("Engaged", history.Reason);
        Assert.Equal(db.User.UserId, history.ChangedBy);
    }

    /// <summary>An employee who leaves is never deleted: the record and its history remain.</summary>
    [Fact]
    public async Task Terminating_an_employee_keeps_the_record_and_ends_the_contract()
    {
        var (db, companyId, typeId, _) = await SetUpAsync();
        using var _db = db;
        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);

        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        await contracts.CreateInitialAsync(NewContract(companyId, employee.Id, typeId));

        var result = await employees.ChangeStatusAsync(
            employee.Id, EmployeeStatus.Terminated, new DateOnly(2026, 6, 30),
            "Resigned to take up other employment");

        Assert.True(result.IsValid);

        var stored = await db.Context.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id);
        Assert.Equal(EmployeeStatus.Terminated, stored.Status);
        Assert.Equal(new DateOnly(2026, 6, 30), stored.TerminationDate);
        Assert.False(stored.IsPayrollEligible);

        var contract = await db.Context.EmployeeContracts.AsNoTracking()
            .SingleAsync(c => c.EmployeeId == employee.Id);
        Assert.Equal(ContractStatus.Ended, contract.Status);
        Assert.False(contract.IsCurrent);

        // The contract row itself survives, so past payroll stays reproducible.
        Assert.Equal(1, await db.Context.EmployeeContracts.CountAsync());
    }

    [Fact]
    public async Task A_status_change_requires_a_reason()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;
        var service = new EmployeeService(db.Context, db.User, db.Clock);
        var employee = (await service.CreateAsync(NewEmployee(companyId))).Value!;

        var result = await service.ChangeStatusAsync(
            employee.Id, EmployeeStatus.Suspended, new DateOnly(2026, 6, 30), "  ");

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Each_status_change_is_recorded()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;
        var service = new EmployeeService(db.Context, db.User, db.Clock);
        var employee = (await service.CreateAsync(NewEmployee(companyId))).Value!;

        await service.ChangeStatusAsync(employee.Id, EmployeeStatus.Suspended,
            new DateOnly(2026, 5, 1), "Pending disciplinary hearing");
        await service.ChangeStatusAsync(employee.Id, EmployeeStatus.Active,
            new DateOnly(2026, 5, 20), "Hearing concluded, no action");

        var history = await db.Context.EmployeeStatusHistory.AsNoTracking()
            .Where(h => h.EmployeeId == employee.Id)
            .OrderBy(h => h.EffectiveDate).ToListAsync();

        Assert.Equal(3, history.Count);
        Assert.Equal(EmployeeStatus.Suspended, history[1].ToStatus);
        Assert.Equal(EmployeeStatus.Active, history[2].ToStatus);
    }

    [Fact]
    public async Task Viewing_employees_requires_permission()
    {
        var (db, companyId, _, _) = await SetUpAsync(TestUser.WithPermissions(Permissions.PayrollView));
        using var _db = db;
        var service = new EmployeeService(db.Context, db.User, db.Clock);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            service.SearchAsync(companyId, new EmployeeFilter()));
    }

    [Fact]
    public async Task Editing_employees_requires_permission()
    {
        var (db, companyId, _, _) = await SetUpAsync(TestUser.WithPermissions(Permissions.EmployeesView));
        using var _db = db;
        var service = new EmployeeService(db.Context, db.User, db.Clock);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            service.CreateAsync(NewEmployee(companyId)));
    }

    [Fact]
    public async Task The_employee_list_can_be_searched_and_filtered()
    {
        var (db, companyId, typeId, _) = await SetUpAsync();
        using var _db = db;
        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);

        var moyo = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        await contracts.CreateInitialAsync(NewContract(companyId, moyo.Id, typeId));

        var dube = NewEmployee(companyId, "EMP-0032");
        dube.FirstName = "Peter";
        dube.LastName = "Dube";
        dube.NationalId = "63-7654321 A 11";
        var created = (await employees.CreateAsync(dube)).Value!;
        await contracts.CreateInitialAsync(
            NewContract(companyId, created.Id, typeId, 12500m, "ZWG"));

        var all = await employees.SearchAsync(companyId, new EmployeeFilter());
        var byName = await employees.SearchAsync(companyId, new EmployeeFilter { SearchTerm = "Dube" });
        var byCurrency = await employees.SearchAsync(companyId,
            new EmployeeFilter { PayrollCurrency = "ZWG" });
        var byNumber = await employees.SearchAsync(companyId,
            new EmployeeFilter { SearchTerm = "EMP-0031" });

        Assert.Equal(2, all.Count);
        Assert.Single(byName);
        Assert.Equal("Peter Dube", byName[0].FullName);
        Assert.Single(byCurrency);
        Assert.Equal("ZWG", byCurrency[0].PayrollCurrency);
        Assert.Single(byNumber);
    }

    /// <summary>
    /// Employees paid in different currencies coexist, and their amounts are never merged: the
    /// list reports each employee's own currency.
    /// </summary>
    [Fact]
    public async Task Employees_on_different_currencies_coexist()
    {
        var (db, companyId, typeId, _) = await SetUpAsync();
        using var _db = db;
        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);

        var usd = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        await contracts.CreateInitialAsync(NewContract(companyId, usd.Id, typeId, 850m, "USD"));

        var zigEmployee = NewEmployee(companyId, "EMP-0032");
        zigEmployee.NationalId = "63-7654321 A 11";
        var zig = (await employees.CreateAsync(zigEmployee)).Value!;
        await contracts.CreateInitialAsync(NewContract(companyId, zig.Id, typeId, 12500m, "ZWG"));

        var list = await employees.SearchAsync(companyId, new EmployeeFilter());

        Assert.Equal(2, list.Count);
        Assert.Contains(list, e => e.PayrollCurrency == "USD");
        Assert.Contains(list, e => e.PayrollCurrency == "ZWG");
    }
}
