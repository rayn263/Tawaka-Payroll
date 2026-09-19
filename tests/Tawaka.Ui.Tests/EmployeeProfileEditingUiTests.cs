using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Employees;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Security;
using Tawaka.Ui.Shared.Components.Pages;

namespace Tawaka.Ui.Tests;

/// <summary>
/// Maintaining an employee after they have been created: their details, their pay, their statutory
/// identifiers, their standing earnings and deductions, where they are paid, and their leaving.
/// <para>
/// Without these the application could create an employee and never change anything about them
/// again — no pay rise, no new bank account, no tax number, no leaver.
/// </para>
/// </summary>
public sealed class EmployeeProfileEditingUiTests : UiTestHost
{
    private Guid _employeeId;

    private IRenderedComponent<EmployeeProfile> Profile(string tab)
    {
        var page = RenderComponent<EmployeeProfile>(p => p.Add(x => x.EmployeeId, _employeeId));
        page.FindAll("button.tab").First(t => t.TextContent.Trim() == tab).Click();
        return page;
    }

    private void GivenAnEmployee(decimal monthly = 850m)
    {
        SignInWithEverything();

        var employees = Services.GetRequiredService<EmployeeService>();
        var contracts = Services.GetRequiredService<EmployeeContractService>();
        var permanent = Db.EmploymentTypes.AsNoTracking().Single(t => t.Code == "Permanent");

        var employee = employees.CreateAsync(new Employee
        {
            CompanyId = CompanyId,
            EmployeeNumber = "EMP-0100",
            FirstName = "Tendai",
            LastName = "Moyo",
            NationalId = "63-1212121 A 42",
            HireDate = new DateOnly(2024, 3, 1),
            Status = EmployeeStatus.Active
        }).GetAwaiter().GetResult().Value!;

        contracts.CreateInitialAsync(new EmployeeContract
        {
            CompanyId = CompanyId,
            EmployeeId = employee.Id,
            EmploymentTypeId = permanent.Id,
            StartDate = new DateOnly(2024, 3, 1),
            PayrollCurrency = "USD",
            PaymentFrequency = PaymentFrequency.Monthly,
            EarningsBasis = EarningsBasis.MonthlySalary,
            MonthlyRate = monthly,
            StandardHoursPerDay = 8m,
            StandardDaysPerWeek = 5m
        }).GetAwaiter().GetResult();

        Db.ChangeTracker.Clear();
        _employeeId = employee.Id;
    }

    [Fact]
    public void An_employees_details_can_be_corrected()
    {
        GivenAnEmployee();
        var page = Profile("Personal");

        Button(page, "Edit details").Click();

        Field(page, "Phone").Change("+263 77 123 4567");
        Field(page, "Email").Change("tendai.moyo@example.co.zw");
        Field(page, "City").Change("Mutare");

        Button(page, "Save details").Click();

        Db.ChangeTracker.Clear();
        var stored = Db.Employees.AsNoTracking().Single(e => e.Id == _employeeId);

        Assert.Equal("+263 77 123 4567", stored.Phone);
        Assert.Equal("tendai.moyo@example.co.zw", stored.Email);
        Assert.Equal("Mutare", stored.City);
        Assert.Contains("Mutare", page.Markup);
    }

    [Fact]
    public void Cancelling_an_edit_writes_nothing()
    {
        GivenAnEmployee();
        var page = Profile("Personal");

        Button(page, "Edit details").Click();
        Field(page, "Phone").Change("+263 00 000 0000");
        Button(page, "Cancel").Click();

        Db.ChangeTracker.Clear();
        Assert.Null(Db.Employees.AsNoTracking().Single(e => e.Id == _employeeId).Phone);
    }

    [Fact]
    public void A_pay_rise_creates_a_new_contract_version_and_keeps_the_old_one()
    {
        GivenAnEmployee();
        var page = Profile("Contract");

        Field(page, "Effective from").Change("2026-10-01");
        Field(page, "Reason").Change("Annual review");
        Field(page, "Monthly rate").Change("1150");

        Button(page, "Create new version").Click();

        Db.ChangeTracker.Clear();
        var versions = Db.EmployeeContracts.AsNoTracking()
            .Where(c => c.EmployeeId == _employeeId)
            .OrderBy(c => c.VersionNumber)
            .ToList();

        Assert.Equal(2, versions.Count);
        Assert.Equal(850m, versions[0].MonthlyRate);
        Assert.False(versions[0].IsCurrent);
        Assert.Equal(1150m, versions[1].MonthlyRate);
        Assert.True(versions[1].IsCurrent);
        Assert.Equal("Annual review", versions[1].ChangeReason);
        Assert.Equal(new DateOnly(2026, 10, 1), versions[1].StartDate);
    }

    [Fact]
    public void A_contract_change_without_a_reason_is_refused()
    {
        GivenAnEmployee();
        var page = Profile("Contract");

        Field(page, "Effective from").Change("2026-10-01");
        Field(page, "Monthly rate").Change("1150");
        Button(page, "Create new version").Click();

        Assert.Contains("reason", page.Markup, StringComparison.OrdinalIgnoreCase);
        Db.ChangeTracker.Clear();
        Assert.Single(Db.EmployeeContracts.AsNoTracking().Where(c => c.EmployeeId == _employeeId).ToList());
    }

    [Fact]
    public void Statutory_identifiers_can_be_captured()
    {
        GivenAnEmployee();
        var page = Profile("Statutory");

        Assert.Contains("No tax number is recorded", page.Markup);

        Field(page, "Tax number (ZIMRA)").Change("BP1234567");
        Field(page, "NSSA number").Change("NSSA-0001");
        Button(page, "Save statutory details").Click();

        Db.ChangeTracker.Clear();
        var profile = Db.EmployeeStatutoryProfiles.AsNoTracking()
            .Single(p => p.EmployeeId == _employeeId);

        Assert.Equal("BP1234567", profile.TaxNumber);
        Assert.Equal("NSSA-0001", profile.NssaNumber);
        Assert.DoesNotContain("No tax number is recorded", page.Markup);
    }

    [Fact]
    public void A_standing_allowance_can_be_added_in_the_employees_own_currency()
    {
        GivenAnEmployee();
        var page = Profile("Earnings");

        var housing = Db.EarningTypes.AsNoTracking()
            .First(t => t.CompanyId == CompanyId && t.Code != "BASIC");

        Field(page, "Earning").Change(housing.Id.ToString());
        Field(page, "Amount").Change("120");
        Field(page, "From").Change("2026-09-01");

        Button(page, "Add earning").Click();

        Db.ChangeTracker.Clear();
        var earning = Db.EmployeeRecurringEarnings.AsNoTracking()
            .Single(e => e.EmployeeId == _employeeId);

        Assert.Equal(housing.Id, earning.EarningTypeId);
        Assert.Equal(120m, earning.Amount);
        Assert.Equal("USD", earning.CurrencyCode);
        Assert.Equal(new DateOnly(2026, 9, 1), earning.EffectiveFrom);
    }

    [Fact]
    public void A_payment_account_can_be_added_in_a_different_currency_from_the_pay()
    {
        GivenAnEmployee();
        var page = Profile("Payment");

        Field(page, "Currency").Change("ZWG");
        Field(page, "Account name").Change("T Moyo");
        Field(page, "Bank").Change("CBZ");
        Field(page, "Account number").Change("0123456789");

        Button(page, "Add account").Click();

        Db.ChangeTracker.Clear();
        var account = Db.EmployeePaymentAccounts.AsNoTracking()
            .Single(a => a.EmployeeId == _employeeId);

        Assert.Equal("ZWG", account.CurrencyCode);
        Assert.True(account.IsPrimary);

        // And the employee is still paid in USD.
        Assert.Equal(
            "USD",
            Db.EmployeeContracts.AsNoTracking()
                .Single(c => c.EmployeeId == _employeeId && c.IsCurrent).PayrollCurrency);
    }

    [Fact]
    public void An_employee_can_be_ended_and_is_never_deleted()
    {
        GivenAnEmployee();
        var page = Profile("Employment");

        Field(page, "Effective date").Change("2026-09-30");
        Field(page, "Reason").Change("Resigned.");
        Button(page, "Record status change").Click();

        Db.ChangeTracker.Clear();
        var employee = Db.Employees.AsNoTracking().Single(e => e.Id == _employeeId);

        Assert.Equal(EmployeeStatus.Terminated, employee.Status);
        Assert.Equal(new DateOnly(2026, 9, 30), employee.TerminationDate);
        Assert.Equal("Resigned.", employee.TerminationReason);

        // The contract is closed off, and the record and its history remain.
        var contract = Db.EmployeeContracts.AsNoTracking().Single(c => c.EmployeeId == _employeeId);
        Assert.False(contract.IsCurrent);
        Assert.NotEmpty(Db.EmployeeStatusHistory.AsNoTracking()
            .Where(h => h.EmployeeId == _employeeId).ToList());
    }

    [Fact]
    public void A_viewer_is_offered_none_of_it()
    {
        GivenAnEmployee();
        Session.SignOut();
        SignIn("Viewer", RoleNames.Viewer, Permissions.EmployeesView, Permissions.EmployeesViewSalary);

        foreach (var tab in new[] { "Personal", "Employment", "Contract", "Statutory", "Payment" })
        {
            var page = Profile(tab);

            Assert.DoesNotContain("Edit details", page.Markup);
            Assert.DoesNotContain("Create new version", page.Markup);
            Assert.DoesNotContain("Save statutory details", page.Markup);
            Assert.DoesNotContain("Add account", page.Markup);
            Assert.DoesNotContain("Record status change", page.Markup);
        }
    }
}
