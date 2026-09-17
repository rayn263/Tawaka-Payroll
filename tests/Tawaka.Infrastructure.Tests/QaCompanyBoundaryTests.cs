using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Administration;
using Tawaka.Application.Employees;
using Tawaka.Domain.Common;
using Tawaka.Domain.Companies;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Loans;
using Tawaka.Domain.Organisation;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// One database, two companies, and the question the brief asks: can company A see company B?
/// <para>
/// Tawaka is installed one company per database and there is no multi-company user interface. That
/// is a deployment convention, not a safeguard, so the boundary is tested where it actually has to
/// hold: in the queries.
/// </para>
/// </summary>
public class QaCompanyBoundaryTests : PayrollFixtureBase
{
    private sealed record SecondCompany(Guid CompanyId, Employee Employee, PayrollPeriod Period);

    /// <summary>
    /// A second company with its own employment type, employee, contract, project, loan and
    /// payroll period — enough for every scoped query to have something to leak.
    /// </summary>
    private static async Task<SecondCompany> AddSecondCompanyAsync(Fixture fixture)
    {
        var db = fixture.Db;

        var company = new Company
        {
            LegalName = "Second Company (Private) Limited",
            TradingName = "Second Company",
            TaxNumber = "BP7654321",
            NssaEmployerNumber = "NSSA-0100"
        };
        db.Context.Companies.Add(company);

        var employmentType = new EmploymentType
        {
            CompanyId = company.Id,
            Code = "Permanent",
            Name = "Permanent",
            DefaultPaymentFrequency = PaymentFrequency.Monthly,
            DefaultEarningsBasis = EarningsBasis.MonthlySalary,
            DisplayOrder = 1,
            IsActive = true
        };
        db.Context.EmploymentTypes.Add(employmentType);
        await db.Context.SaveChangesAsync();

        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);

        var employee = (await employees.CreateAsync(new Employee
        {
            CompanyId = company.Id,
            EmployeeNumber = "B-0001",
            FirstName = "Brian",
            LastName = "Chirwa",
            NationalId = "63-9999999 B 11",
            HireDate = new DateOnly(2023, 5, 2),
            Status = EmployeeStatus.Active
        })).Value!;

        await contracts.CreateInitialAsync(
            NewContract(company.Id, employee.Id, employmentType.Id, 1200m));

        db.Context.Projects.Add(new Project
        {
            CompanyId = company.Id, Code = "B-P1", Name = "Second company site",
            Status = ProjectStatus.Active
        });

        db.Context.EmployeeLoans.Add(new EmployeeLoan
        {
            CompanyId = company.Id,
            EmployeeId = employee.Id,
            LoanNumber = "B-LOAN-1",
            CurrencyCode = "USD",
            PrincipalAmount = 300m,
            InstalmentCount = 3,
            ApprovalStatus = InputApprovalStatus.Submitted
        });

        var period = new PayrollPeriod
        {
            CompanyId = company.Id, Code = "2026-09", Name = "September 2026 (B)",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30), TaxYear = 2026, Mode = PayrollMode.Live
        };
        db.Context.PayrollPeriods.Add(period);

        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        return new SecondCompany(company.Id, employee, period);
    }

    [Fact]
    public async Task Company_A_never_sees_company_B_employees_projects_loans_or_payroll()
    {
        using var fixture = await SetUpAsync();
        var b = await AddSecondCompanyAsync(fixture);
        var services = PayrollServices.For(fixture.Db);

        var employees = new EmployeeService(
            fixture.Db.Context, fixture.Db.User, fixture.Db.Clock);

        var listedForA = await employees.SearchAsync(fixture.CompanyId, new EmployeeFilter());
        Assert.DoesNotContain(listedForA, e => e.EmployeeNumber == "B-0001");
        Assert.Contains(listedForA, e => e.EmployeeNumber == fixture.Employee.EmployeeNumber);

        var listedForB = await employees.SearchAsync(b.CompanyId, new EmployeeFilter());
        Assert.Single(listedForB);
        Assert.Equal("B-0001", listedForB[0].EmployeeNumber);

        var runsForA = await services.Runs.GetRunsAsync(fixture.CompanyId);
        Assert.All(runsForA, run => Assert.Equal(fixture.CompanyId, run.CompanyId));

        var loansForA = await services.Loans.GetLoansAsync(fixture.CompanyId);
        Assert.DoesNotContain(loansForA, l => l.LoanNumber == "B-LOAN-1");

        var loanQueueForA = await services.Loans.GetApprovalQueueAsync(fixture.CompanyId);
        Assert.DoesNotContain(loanQueueForA, l => l.LoanNumber == "B-LOAN-1");

        var timeQueueForA = await services.Timesheets.GetApprovalQueueAsync(fixture.CompanyId);
        Assert.All(timeQueueForA, t => Assert.Equal(fixture.CompanyId, t.CompanyId));

        var leaveForA = await services.Leave.GetRequestsAsync(fixture.CompanyId);
        Assert.All(leaveForA, r => Assert.Equal(fixture.CompanyId, r.CompanyId));

        var obligationsForA = await services.Obligations.GetRegisterAsync(fixture.CompanyId);
        Assert.All(obligationsForA, o => Assert.Equal(fixture.CompanyId, o.CompanyId));

        var projectsForA = await fixture.Db.Context.Projects.AsNoTracking()
            .Where(p => p.CompanyId == fixture.CompanyId).ToListAsync();
        Assert.DoesNotContain(projectsForA, p => p.Code == "B-P1");
    }

    /// <summary>
    /// The dashboard reports one company's position. Counting two companies' employees into one
    /// figure would be wrong on its face, and would disclose the second company besides.
    /// </summary>
    [Fact]
    public async Task The_dashboard_reports_one_company_and_not_the_other()
    {
        using var fixture = await SetUpAsync();
        var b = await AddSecondCompanyAsync(fixture);

        var directory = new UserDirectory(fixture.Db.Context);
        var dashboard = new DashboardService(
            fixture.Db.Context,
            new Tawaka.Application.Release.ReleaseReadinessService(fixture.Db.Context),
            directory);

        var forA = await dashboard.BuildAsync(fixture.CompanyId);
        var forB = await dashboard.BuildAsync(b.CompanyId);

        Assert.Equal(1, forA.EmployeesActive);
        Assert.Equal(1, forB.EmployeesActive);

        Assert.Equal("September 2026", forA.CurrentPeriod?.Name);
        Assert.Equal("September 2026 (B)", forB.CurrentPeriod?.Name);

        // Contracted pay is one company's commitment, never the two added together.
        Assert.Equal(850m, forA.ContractedBasicByCurrency.Single(r => r.CurrencyCode == "USD").Amount);
        Assert.Equal(1200m, forB.ContractedBasicByCurrency.Single(r => r.CurrencyCode == "USD").Amount);
    }

    /// <summary>
    /// Every row that belongs to a company says which one. A row that does not is a row no scoped
    /// query can exclude.
    /// </summary>
    [Fact]
    public async Task Every_company_scoped_row_created_for_the_second_company_carries_its_id()
    {
        using var fixture = await SetUpAsync();
        var b = await AddSecondCompanyAsync(fixture);

        Assert.Equal(
            b.CompanyId,
            (await fixture.Db.Context.Employees.AsNoTracking()
                .SingleAsync(e => e.EmployeeNumber == "B-0001")).CompanyId);

        Assert.Equal(
            b.CompanyId,
            (await fixture.Db.Context.EmployeeContracts.AsNoTracking()
                .SingleAsync(c => c.EmployeeId == b.Employee.Id)).CompanyId);

        Assert.Equal(
            b.CompanyId,
            (await fixture.Db.Context.EmployeeLoans.AsNoTracking()
                .SingleAsync(l => l.LoanNumber == "B-LOAN-1")).CompanyId);

        Assert.Equal(
            b.CompanyId,
            (await fixture.Db.Context.Projects.AsNoTracking()
                .SingleAsync(p => p.Code == "B-P1")).CompanyId);
    }
}
