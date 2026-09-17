using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tawaka.Application.Employees;
using Tawaka.Application.Payroll;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory;

namespace Tawaka.Ui.Tests;

/// <summary>
/// Shared setup for the payroll screens: a verified installation, an employee on a contract, a
/// period and a run.
/// <para>
/// Deliberately holds no tests of its own. A base class carrying facts is re-run once per derived
/// class, which inflates the reported test count without testing anything more.
/// </para>
/// </summary>
public abstract class PayrollUiScenario : UiTestHost
{
    protected const string Officer = "Tapiwa Ncube";
    protected const string Manager = "Rudo Moyo";

    protected static readonly string[] OfficerPermissions =
    {
        Permissions.PayrollView, Permissions.PayrollCreate, Permissions.PayrollCalculate,
        Permissions.PayrollFinalise, Permissions.PayrollRecordPayment, Permissions.PayrollLock,
        Permissions.EmployeesView, Permissions.ReportsRun, Permissions.StatutoryView
    };

    protected static readonly string[] ManagerPermissions =
    {
        Permissions.PayrollView, Permissions.PayrollApprove, Permissions.EmployeesView,
        Permissions.ReportsRun, Permissions.StatutoryView
    };

    protected void SignInAsOfficer() => SignIn(Officer, RoleNames.PayrollOfficer, OfficerPermissions);

    protected void SignInAsManager() => SignIn(Manager, RoleNames.Manager, ManagerPermissions);

    /// <summary>
    /// Marks the seeded rules verified so a live run is possible at all. This says nothing about
    /// whether the figures are right — verification against ZIMRA and NSSA is a compliance
    /// question, not a test fixture — it only puts the application into the state a verified
    /// installation would be in.
    /// </summary>
    protected void VerifyStatutoryRulesForTesting()
    {
        foreach (var rule in Db.StatutoryRules.ToList())
        {
            if (rule.VerificationStatus != VerificationStatus.Disabled)
            {
                rule.VerificationStatus = VerificationStatus.Verified;
            }
        }

        foreach (var type in Db.EarningTypes.ToList())
        {
            type.TreatmentVerificationStatus = VerificationStatus.Verified;
        }

        foreach (var nssa in Db.NssaRules.ToList())
        {
            nssa.CeilingApplication = CeilingApplicationMethod.ProRataByPeriodLength;
        }

        // The seed ships no APWCS rule, because the assessed rate is set per employer by NSSA and
        // nobody can seed it honestly (compliance question Q26). This row is scaffolding so the
        // screens can be driven through a complete live run; the rate is not a claim about what
        // any employer is assessed at.
        if (!Db.ApwcsRules.Any(r => r.Currency == "USD"))
        {
            Db.StatutoryRules.Add(new ApwcsRule
            {
                RuleId = "TEST-APWCS-2026-USD",
                Name = "Test scaffolding: APWCS assessed rate",
                Currency = "USD",
                IndustryClassification = "Construction",
                IndustryCode = "CON",
                Rate = 0.025m,
                Base = ApwcsBase.BasicEarnings,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                EffectiveTo = new DateOnly(2026, 12, 31),
                VerificationStatus = VerificationStatus.Verified,
                Source = new RuleSource { Source = "Test scaffolding" }
            });
        }

        var company = Db.Companies.Single();
        company.TaxNumber = "BP1234567";
        company.NssaEmployerNumber = "NSSA-0099";

        Db.SaveChanges();
        Db.ChangeTracker.Clear();
    }

    protected Employee CreateEmployee(string number, decimal monthly, string currency)
    {
        // Fixture setup, not part of what is under test: done as an administrator so the screens
        // below start from a company that already has people in it.
        SignInWithEverything("QA setup");

        var employees = Services.GetRequiredService<EmployeeService>();
        var contracts = Services.GetRequiredService<EmployeeContractService>();
        var permanent = Db.EmploymentTypes.AsNoTracking().Single(t => t.Code == "Permanent");

        var employee = employees.CreateAsync(new Employee
        {
            CompanyId = CompanyId,
            EmployeeNumber = number,
            FirstName = "Tendai",
            LastName = number,
            NationalId = $"63-{number} X 42",
            HireDate = new DateOnly(2024, 1, 8),
            Status = EmployeeStatus.Active
        }).GetAwaiter().GetResult().Value!;

        contracts.CreateInitialAsync(new EmployeeContract
        {
            CompanyId = CompanyId,
            EmployeeId = employee.Id,
            EmploymentTypeId = permanent.Id,
            StartDate = new DateOnly(2024, 1, 8),
            PayrollCurrency = currency,
            PaymentFrequency = PaymentFrequency.Monthly,
            EarningsBasis = EarningsBasis.MonthlySalary,
            MonthlyRate = monthly,
            StandardHoursPerDay = 8m,
            StandardDaysPerWeek = 5m
        }).GetAwaiter().GetResult();

        Db.EmployeeStatutoryProfiles.Add(new EmployeeStatutoryProfile
        {
            EmployeeId = employee.Id, TaxNumber = $"BP{number}", NssaNumber = $"NSSA{number}"
        });
        Db.SaveChanges();
        Db.ChangeTracker.Clear();

        return employee;
    }

    protected PayrollPeriod CreatePeriod(PayrollMode mode)
    {
        var period = new PayrollPeriod
        {
            CompanyId = CompanyId,
            Code = "2026-09",
            Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30),
            TaxYear = 2026,
            Mode = mode
        };

        Db.PayrollPeriods.Add(period);
        Db.SaveChanges();
        Db.ChangeTracker.Clear();
        return period;
    }

    protected PayrollRun CreateRun(Guid periodId)
    {
        SignInWithEverything("QA setup");
        return Services.GetRequiredService<PayrollRunService>()
            .CreateRunAsync(periodId).GetAwaiter().GetResult().Value!;
    }

    protected PayrollRunStatus Status(Guid runId)
    {
        Db.ChangeTracker.Clear();
        return Db.PayrollRuns.AsNoTracking().Single(r => r.Id == runId).Status;
    }
}
