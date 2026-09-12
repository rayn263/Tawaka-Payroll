using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Employees;
using Tawaka.Application.Security;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Security;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// The guarantee this milestone exists to establish: a change in employment terms creates a new
/// contract version and never overwrites the previous one, so payroll for any past period can be
/// recalculated on the terms that actually applied then.
/// </summary>
public class ContractVersioningTests : EmployeeTestBase
{
    private static async Task<(TestDatabase Db, Guid CompanyId, Guid TypeId, Employee Employee)>
        WithEmployeeAsync()
    {
        var (db, companyId, typeId, _) = await SetUpAsync();
        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        return (db, companyId, typeId, employee);
    }

    [Fact]
    public async Task An_initial_contract_becomes_version_one_and_current()
    {
        var (db, companyId, typeId, employee) = await WithEmployeeAsync();
        using var _db = db;
        var service = new EmployeeContractService(db.Context, db.User);

        var result = await service.CreateInitialAsync(NewContract(companyId, employee.Id, typeId));

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.VersionNumber);
        Assert.True(result.Value.IsCurrent);
        Assert.Equal(ContractStatus.Active, result.Value.Status);
    }

    [Fact]
    public async Task A_second_initial_contract_is_refused()
    {
        var (db, companyId, typeId, employee) = await WithEmployeeAsync();
        using var _db = db;
        var service = new EmployeeContractService(db.Context, db.User);
        await service.CreateInitialAsync(NewContract(companyId, employee.Id, typeId));

        var result = await service.CreateInitialAsync(NewContract(companyId, employee.Id, typeId));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Message.Contains("supersede"));
    }

    /// <summary>A salary increase must leave the original terms intact.</summary>
    [Fact]
    public async Task A_salary_change_creates_a_new_version_and_preserves_the_old_one()
    {
        var (db, companyId, typeId, employee) = await WithEmployeeAsync();
        using var _db = db;
        var service = new EmployeeContractService(db.Context, db.User);
        var original = (await service.CreateInitialAsync(
            NewContract(companyId, employee.Id, typeId, 850m))).Value!;

        var newTerms = NewContract(companyId, employee.Id, typeId, 1000m);
        var result = await service.SupersedeAsync(
            employee.Id, newTerms, new DateOnly(2026, 7, 1), "Annual increase");

        Assert.True(result.Succeeded);

        var history = await service.GetHistoryAsync(employee.Id);
        Assert.Equal(2, history.Count);

        var version1 = history.Single(c => c.VersionNumber == 1);
        var version2 = history.Single(c => c.VersionNumber == 2);

        // The original terms are untouched.
        Assert.Equal(850m, version1.MonthlyRate);
        Assert.Equal(original.StartDate, version1.StartDate);
        Assert.Equal(ContractStatus.Superseded, version1.Status);
        Assert.False(version1.IsCurrent);
        Assert.Equal(new DateOnly(2026, 6, 30), version1.EndDate);
        Assert.Equal(version2.Id, version1.SupersededByContractId);

        // The new terms are current.
        Assert.Equal(1000m, version2.MonthlyRate);
        Assert.True(version2.IsCurrent);
        Assert.Equal("Annual increase", version2.ChangeReason);
        Assert.Equal(version1.Id, version2.PreviousContractId);
    }

    /// <summary>
    /// The reason this matters: payroll must resolve the contract by date, not take the latest.
    /// </summary>
    [Fact]
    public async Task The_contract_applying_on_a_past_date_is_still_resolvable()
    {
        var (db, companyId, typeId, employee) = await WithEmployeeAsync();
        using var _db = db;
        var service = new EmployeeContractService(db.Context, db.User);
        await service.CreateInitialAsync(NewContract(companyId, employee.Id, typeId, 850m));
        await service.SupersedeAsync(employee.Id, NewContract(companyId, employee.Id, typeId, 1000m),
            new DateOnly(2026, 7, 1), "Annual increase");

        var june = await service.GetContractOnAsync(employee.Id, new DateOnly(2026, 6, 30));
        var july = await service.GetContractOnAsync(employee.Id, new DateOnly(2026, 7, 31));

        Assert.Equal(850m, june!.MonthlyRate);
        Assert.Equal(1000m, july!.MonthlyRate);
    }

    [Fact]
    public async Task Three_versions_resolve_correctly_across_their_periods()
    {
        var (db, companyId, typeId, employee) = await WithEmployeeAsync();
        using var _db = db;
        var service = new EmployeeContractService(db.Context, db.User);
        await service.CreateInitialAsync(NewContract(companyId, employee.Id, typeId, 700m));
        await service.SupersedeAsync(employee.Id, NewContract(companyId, employee.Id, typeId, 850m),
            new DateOnly(2024, 1, 1), "Increase");
        await service.SupersedeAsync(employee.Id, NewContract(companyId, employee.Id, typeId, 1000m),
            new DateOnly(2026, 7, 1), "Increase");

        Assert.Equal(700m, (await service.GetContractOnAsync(employee.Id, new DateOnly(2022, 6, 1)))!.MonthlyRate);
        Assert.Equal(850m, (await service.GetContractOnAsync(employee.Id, new DateOnly(2025, 6, 1)))!.MonthlyRate);
        Assert.Equal(1000m, (await service.GetContractOnAsync(employee.Id, new DateOnly(2026, 9, 1)))!.MonthlyRate);
        Assert.Equal(3, (await service.GetHistoryAsync(employee.Id)).Count);
    }

    [Fact]
    public async Task Superseding_requires_a_reason()
    {
        var (db, companyId, typeId, employee) = await WithEmployeeAsync();
        using var _db = db;
        var service = new EmployeeContractService(db.Context, db.User);
        await service.CreateInitialAsync(NewContract(companyId, employee.Id, typeId));

        var result = await service.SupersedeAsync(
            employee.Id, NewContract(companyId, employee.Id, typeId, 1000m),
            new DateOnly(2026, 7, 1), "   ");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task A_new_version_cannot_start_before_the_one_it_supersedes()
    {
        var (db, companyId, typeId, employee) = await WithEmployeeAsync();
        using var _db = db;
        var service = new EmployeeContractService(db.Context, db.User);
        await service.CreateInitialAsync(NewContract(companyId, employee.Id, typeId));

        var result = await service.SupersedeAsync(
            employee.Id, NewContract(companyId, employee.Id, typeId, 1000m),
            new DateOnly(2020, 1, 1), "Backdated increase");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Only_one_contract_is_ever_current()
    {
        var (db, companyId, typeId, employee) = await WithEmployeeAsync();
        using var _db = db;
        var service = new EmployeeContractService(db.Context, db.User);
        await service.CreateInitialAsync(NewContract(companyId, employee.Id, typeId));
        await service.SupersedeAsync(employee.Id, NewContract(companyId, employee.Id, typeId, 1000m),
            new DateOnly(2026, 7, 1), "Increase");
        await service.SupersedeAsync(employee.Id, NewContract(companyId, employee.Id, typeId, 1100m),
            new DateOnly(2026, 8, 1), "Increase");

        var current = await db.Context.EmployeeContracts.AsNoTracking()
            .CountAsync(c => c.EmployeeId == employee.Id && c.IsCurrent);

        Assert.Equal(1, current);
    }

    /// <summary>Currency is part of the terms, so changing it is a new version, not an edit.</summary>
    [Fact]
    public async Task Changing_payroll_currency_creates_a_new_version()
    {
        var (db, companyId, typeId, employee) = await WithEmployeeAsync();
        using var _db = db;
        var service = new EmployeeContractService(db.Context, db.User);
        await service.CreateInitialAsync(NewContract(companyId, employee.Id, typeId, 850m));

        var newTerms = NewContract(companyId, employee.Id, typeId, 22000m, "ZWG");
        await service.SupersedeAsync(employee.Id, newTerms, new DateOnly(2026, 7, 1),
            "Moved to ZiG remuneration");

        Assert.Equal("USD",
            (await service.GetContractOnAsync(employee.Id, new DateOnly(2026, 6, 1)))!.PayrollCurrency);
        Assert.Equal("ZWG",
            (await service.GetContractOnAsync(employee.Id, new DateOnly(2026, 8, 1)))!.PayrollCurrency);
    }

    [Fact]
    public async Task A_fixed_term_contract_requires_an_end_date()
    {
        var (db, companyId, _, employee) = await WithEmployeeAsync();
        using var _db = db;
        var fixedTerm = await db.Context.EmploymentTypes.AsNoTracking()
            .SingleAsync(t => t.Code == "Contract");
        var service = new EmployeeContractService(db.Context, db.User);

        var withoutEnd = NewContract(companyId, employee.Id, fixedTerm.Id);
        var refused = await service.CreateInitialAsync(withoutEnd);

        Assert.False(refused.Succeeded);
        Assert.Contains(refused.Validation.Errors, e => e.Field == nameof(EmployeeContract.EndDate));

        var withEnd = NewContract(companyId, employee.Id, fixedTerm.Id);
        withEnd.EndDate = new DateOnly(2027, 3, 3);
        Assert.True((await service.CreateInitialAsync(withEnd)).Succeeded);
    }

    [Fact]
    public async Task An_unknown_currency_is_refused()
    {
        var (db, companyId, typeId, employee) = await WithEmployeeAsync();
        using var _db = db;
        var service = new EmployeeContractService(db.Context, db.User);

        var contract = NewContract(companyId, employee.Id, typeId, 850m, "GBP");
        var result = await service.CreateInitialAsync(contract);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors,
            e => e.Field == nameof(EmployeeContract.PayrollCurrency));
    }

    [Fact]
    public async Task Changing_salary_requires_the_salary_permission()
    {
        var (db, companyId, typeId, _) = await SetUpAsync(
            TestUser.WithPermissions(Permissions.EmployeesView, Permissions.EmployeesEdit));
        using var _db = db;
        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        var contracts = new EmployeeContractService(db.Context, db.User);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            contracts.CreateInitialAsync(NewContract(companyId, employee.Id, typeId)));
    }

    /// <summary>Every contract change is attributable: who made it, when, and why.</summary>
    [Fact]
    public async Task Contract_changes_are_audited()
    {
        var (db, companyId, typeId, employee) = await WithEmployeeAsync();
        using var _db = db;
        var service = new EmployeeContractService(db.Context, db.User);
        await service.CreateInitialAsync(NewContract(companyId, employee.Id, typeId, 850m));
        await service.SupersedeAsync(employee.Id, NewContract(companyId, employee.Id, typeId, 1000m),
            new DateOnly(2026, 7, 1), "Annual increase");

        var entries = await db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == nameof(EmployeeContract))
            .ToListAsync();

        Assert.Contains(entries, a => a.Action == Domain.Audit.AuditAction.Create);
        Assert.Contains(entries, a => a.FieldName == nameof(EmployeeContract.IsCurrent)
                                      && a.OldValue == "True" && a.NewValue == "False");
        Assert.All(entries, a => Assert.Equal(db.User.UserId, a.UserId));
    }
}
