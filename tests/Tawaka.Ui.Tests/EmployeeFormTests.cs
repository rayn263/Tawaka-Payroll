using Bunit;
using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Security;
using Tawaka.Ui.Shared.Components.Pages;

namespace Tawaka.Ui.Tests;

/// <summary>
/// Fills in the employee form the way a payroll officer would: type into the boxes, press the
/// button, and see what the screen says and what reached the database.
/// </summary>
public sealed class EmployeeFormTests : UiTestHost
{
    public EmployeeFormTests() => SignInWithEverything();

    /// <summary>Permanent employment: the one seeded type that needs no contract end date.</summary>
    private EmploymentType PermanentType() =>
        Db.EmploymentTypes.AsNoTracking().Single(t => t.Code == "Permanent");

    [Fact]
    public void The_form_renders_every_field_it_needs_and_offers_both_currencies()
    {
        var page = RenderComponent<EmployeeNew>();

        Assert.NotNull(Field(page, "Employee number"));
        Assert.NotNull(Field(page, "First name"));
        Assert.NotNull(Field(page, "Last name"));
        Assert.NotNull(Field(page, "Engagement date"));
        Assert.NotNull(Field(page, "Employment type"));

        var currencies = Field(page, "Payroll currency").QuerySelectorAll("option")
            .Select(o => o.GetAttribute("value")).ToList();

        Assert.Contains("USD", currencies);
        Assert.Contains("ZWG", currencies);
    }

    [Fact]
    public void Saving_an_empty_form_shows_the_validation_errors_and_stores_nothing()
    {
        var page = RenderComponent<EmployeeNew>();

        Button(page, "Save employee").Click();

        Assert.Contains("employee number", page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, Db.Employees.Count());
    }

    [Fact]
    public void A_completed_form_creates_the_employee_and_the_first_contract_version()
    {
        var page = RenderComponent<EmployeeNew>();
        var employmentType = PermanentType();

        Field(page, "Employee number").Change("EMP-1001");
        Field(page, "First name").Change("Tendai");
        Field(page, "Last name").Change("Moyo");
        Field(page, "Engagement date").Change("2026-01-05");
        Field(page, "Employment type").Change(employmentType.Id.ToString());
        Field(page, "Payroll currency").Change("USD");
        Field(page, "Monthly rate").Change("950");

        Button(page, "Save employee").Click();

        var employee = Db.Employees.AsNoTracking().Single();
        Assert.Equal("EMP-1001", employee.EmployeeNumber);
        Assert.Equal("Tendai", employee.FirstName);
        Assert.Equal(new DateOnly(2026, 1, 5), employee.HireDate);

        var contract = Db.EmployeeContracts.AsNoTracking().Single(c => c.EmployeeId == employee.Id);
        Assert.Equal(1, contract.VersionNumber);
        Assert.True(contract.IsCurrent);
        Assert.Equal("USD", contract.PayrollCurrency);
        Assert.Equal(950m, contract.MonthlyRate);
    }

    [Fact]
    public void A_contract_in_ZiG_is_stored_in_ZiG_and_never_converted_to_USD()
    {
        var page = RenderComponent<EmployeeNew>();
        var employmentType = PermanentType();

        Field(page, "Employee number").Change("EMP-2001");
        Field(page, "First name").Change("Rudo");
        Field(page, "Last name").Change("Chikore");
        Field(page, "Engagement date").Change("2026-02-01");
        Field(page, "Employment type").Change(employmentType.Id.ToString());
        Field(page, "Payroll currency").Change("ZWG");
        Field(page, "Monthly rate").Change("12500");

        Button(page, "Save employee").Click();

        var contract = Db.EmployeeContracts.AsNoTracking().Single();
        Assert.Equal("ZWG", contract.PayrollCurrency);
        Assert.Equal(12500m, contract.MonthlyRate);
    }

    /// <summary>
    /// A duplicate employee number has to be refused on the screen, not swallowed and not turned
    /// into a second record for the same person.
    /// </summary>
    [Fact]
    public void A_duplicate_employee_number_is_refused_on_screen()
    {
        var employmentType = PermanentType();

        void Complete(IRenderedComponent<EmployeeNew> form, string first)
        {
            Field(form, "Employee number").Change("EMP-3001");
            Field(form, "First name").Change(first);
            Field(form, "Last name").Change("Ncube");
            Field(form, "Engagement date").Change("2026-03-01");
            Field(form, "Employment type").Change(employmentType.Id.ToString());
            Field(form, "Monthly rate").Change("800");
            Button(form, "Save employee").Click();
        }

        Complete(RenderComponent<EmployeeNew>(), "Farai");
        Assert.Equal(1, Db.Employees.Count());

        var second = RenderComponent<EmployeeNew>();
        Complete(second, "Blessing");

        Assert.Equal(1, Db.Employees.Count());
        Assert.Contains("already", second.Markup, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A fixed-term engagement with no end date must be refused — and refusing it must not leave
    /// half a record behind. If the employee were written before the contract was validated, the
    /// officer correcting the date would be told the employee number already exists and could
    /// never finish the record.
    /// </summary>
    [Fact]
    public void A_contract_rejected_by_validation_leaves_no_half_created_employee()
    {
        var fixedTerm = Db.EmploymentTypes.AsNoTracking().Single(t => t.Code == "Contract");
        var page = RenderComponent<EmployeeNew>();

        Field(page, "Employee number").Change("EMP-5001");
        Field(page, "First name").Change("Tapiwa");
        Field(page, "Last name").Change("Sibanda");
        Field(page, "Engagement date").Change("2026-04-01");
        Field(page, "Employment type").Change(fixedTerm.Id.ToString());
        Field(page, "Monthly rate").Change("700");

        Button(page, "Save employee").Click();

        Assert.Contains("contract end date is required", page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Db.Employees.AsNoTracking().ToList());
        Assert.Empty(Db.EmployeeContracts.AsNoTracking().ToList());

        // Correcting the mistake on the same screen has to work.
        Field(page, "Contract end date").Change("2027-03-31");
        Button(page, "Save employee").Click();

        var employee = Db.Employees.AsNoTracking().Single();
        Assert.Equal("EMP-5001", employee.EmployeeNumber);
        Assert.Equal(
            new DateOnly(2027, 3, 31),
            Db.EmployeeContracts.AsNoTracking().Single().EndDate);
    }

    [Fact]
    public void The_employee_list_shows_ZiG_rather_than_the_ISO_code()
    {
        var employmentType = PermanentType();

        var form = RenderComponent<EmployeeNew>();
        Field(form, "Employee number").Change("EMP-4001");
        Field(form, "First name").Change("Nyasha");
        Field(form, "Last name").Change("Dube");
        Field(form, "Engagement date").Change("2026-01-05");
        Field(form, "Employment type").Change(employmentType.Id.ToString());
        Field(form, "Payroll currency").Change("ZWG");
        Field(form, "Monthly rate").Change("9000");
        Button(form, "Save employee").Click();

        var list = RenderComponent<EmployeeList>();

        Assert.Contains("Nyasha Dube", list.Markup);
        Assert.Contains(">ZiG<", list.Markup);
    }

    [Fact]
    public void A_user_without_the_edit_permission_is_not_offered_the_add_button()
    {
        Session.SignOut();
        SignIn("Viewer", RoleNames.Viewer, Permissions.EmployeesView);

        var list = RenderComponent<EmployeeList>();

        Assert.DoesNotContain("Add employee", list.Markup);
    }
}
