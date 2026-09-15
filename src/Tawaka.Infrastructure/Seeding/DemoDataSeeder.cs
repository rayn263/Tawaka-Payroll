using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Domain.Calendars;
using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Loans;
using Tawaka.Domain.Organisation;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Domain.Time;
using Tawaka.Infrastructure.Persistence;

namespace Tawaka.Infrastructure.Seeding;

/// <summary>
/// Creates a worked example: a construction business with a spread of employment types, both
/// currencies, and the awkward cases a payroll officer needs to see handled.
/// <para>
/// <b>Never runs by itself.</b> It is invoked explicitly, refuses to touch a database that already
/// holds employees, and stamps the installation as demonstration data so every screen can say so.
/// A payroll system that quietly seeded fictional employees into a real company's database would
/// be unforgivable.
/// </para>
/// </summary>
public sealed class DemoDataSeeder
{
    /// <summary>Set on an installation carrying demonstration data. Read by the UI banner.</summary>
    public const string DemoSettingKey = "Data.IsDemonstration";

    private readonly PayrollDbContext _context;
    private readonly IClock _clock;

    public DemoDataSeeder(PayrollDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public static async Task<bool> IsDemonstrationAsync(
        PayrollDbContext context, CancellationToken cancellationToken = default)
    {
        var setting = await context.AppSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == DemoSettingKey, cancellationToken)
            .ConfigureAwait(false);

        return string.Equals(setting?.Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Seeds the example. Returns false and changes nothing where employees already exist: this
    /// must never be the reason a real payroll acquires fictional people.
    /// </summary>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _context.Employees.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var company = await _context.Companies.FirstAsync(cancellationToken).ConfigureAwait(false);

        company.LegalName = "Tawaka Construction (Private) Limited [DEMONSTRATION]";
        company.TradingName = "Tawaka Construction";
        company.TaxNumber = "BP-DEMO-0001";
        company.NssaEmployerNumber = "NSSA-DEMO-0001";
        company.AddressLine1 = "14 Samora Machel Avenue";
        company.City = "Harare";
        company.Phone = "+263 24 279 0000";
        company.Email = "payroll@example.invalid";

        _context.AppSettings.Add(new AppSetting
        {
            Key = DemoSettingKey,
            Value = "true",
            Description =
                "This database holds demonstration data seeded on " +
                $"{_clock.Now:yyyy-MM-dd}. Do not use it for a real payroll."
        });

        var types = await _context.EmploymentTypes.AsNoTracking()
            .Where(t => t.CompanyId == company.Id)
            .ToDictionaryAsync(t => t.Code, cancellationToken)
            .ConfigureAwait(false);

        var department = new Department
        {
            CompanyId = company.Id, Code = "OPS", Name = "Operations", IsActive = true
        };
        _context.Departments.Add(department);

        var project = new Project
        {
            CompanyId = company.Id, Code = "NYA-01", Name = "Nyanga shop refurbishment",
            Status = ProjectStatus.Active,
            StartDate = new DateOnly(2026, 6, 1)
        };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var site = new ProjectSite
        {
            ProjectId = project.Id, Code = "NYA-01-A", Name = "Nyanga main site", IsActive = true
        };
        _context.ProjectSites.Add(site);

        var period = new PayrollPeriod
        {
            CompanyId = company.Id,
            Code = "2026-09",
            Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30),
            TaxYear = 2026,

            // Development, always. The demonstration exists to show the workings, and the live
            // gate stays shut whatever data is in front of it.
            Mode = PayrollMode.Development
        };
        _context.PayrollPeriods.Add(period);

        var calendar = await _context.HolidayCalendars
            .FirstOrDefaultAsync(c => c.CompanyId == company.Id && c.IsDefault, cancellationToken)
            .ConfigureAwait(false);

        if (calendar is not null)
        {
            // A company shutdown day, not a public holiday: a public holiday is a claim about the
            // law, and this seeder is in no position to make one (ADR-033).
            _context.PublicHolidays.Add(new PublicHoliday
            {
                HolidayCalendarId = calendar.Id,
                Date = new DateOnly(2026, 9, 14),
                Name = "Site shutdown (demonstration)",
                Kind = HolidayKind.CompanyHoliday,
                VerificationStatus = VerificationStatus.Verified,
                Source = "Demonstration data",
                IsActive = true
            });
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Each of these exists to show one thing a payroll officer has to understand.
        var people = new[]
        {
            Person("EMP-0001", "Tendai", "Chikore", "Permanent", EarningsBasis.MonthlySalary,
                PaymentFrequency.Monthly, 1200m, "USD",
                "A salaried permanent employee: the ordinary case."),
            Person("EMP-0002", "Rudo", "Makoni", "Permanent", EarningsBasis.MonthlySalary,
                PaymentFrequency.Monthly, 28000m, "ZWG",
                "The same, paid in ZiG: a separate payroll currency throughout."),
            Person("EMP-0003", "Farai", "Nyathi", "Contract", EarningsBasis.MonthlySalary,
                PaymentFrequency.Monthly, 900m, "USD",
                "A fixed-term contract employee with an end date."),
            Person("EMP-0004", "Blessing", "Dube", "Temporary", EarningsBasis.DailyRate,
                PaymentFrequency.Weekly, 22m, "USD",
                "Paid by the day: pay follows approved time, never an assumed full period."),
            Person("EMP-0005", "Tapiwa", "Moyo", "Casual", EarningsBasis.DailyRate,
                PaymentFrequency.Weekly, 18m, "USD",
                "Casual: the engagement tally warns before Labour Act s.12(3) deems permanence."),
            Person("EMP-0006", "Chipo", "Sibanda", "HourlyPaid", EarningsBasis.HourlyRate,
                PaymentFrequency.Weekly, 4.50m, "USD",
                "Hourly-paid, with overtime — priced by its own dated rule."),
            Person("EMP-0007", "Simba", "Marufu", "Permanent", EarningsBasis.MonthlySalary,
                PaymentFrequency.Monthly, 140m, "USD",
                "Earns below the tax threshold: a legitimate zero PAYE, shown as 0.00 not blank."),
            Person("EMP-0008", "Nyasha", "Gumbo", "PartTime", EarningsBasis.HourlyRate,
                PaymentFrequency.Monthly, 6m, "USD",
                "Part-time: the NSSA ceiling treatment for reduced hours is unresolved (Q4a/Q22).")
        };

        var created = new List<Employee>();

        foreach (var person in people)
        {
            if (!types.TryGetValue(person.TypeCode, out var type))
            {
                continue;
            }

            var employee = new Employee
            {
                CompanyId = company.Id,
                EmployeeNumber = person.Number,
                FirstName = person.FirstName,
                LastName = person.LastName,
                NationalId = $"63-{person.Number[^4..]}000 X 42",
                DateOfBirth = new DateOnly(1990, 5, 12),
                HireDate = new DateOnly(2024, 2, 1),
                Status = EmployeeStatus.Active,
                Notes = person.Note
            };
            _context.Employees.Add(employee);
            created.Add(employee);

            _context.EmployeeStatutoryProfiles.Add(new EmployeeStatutoryProfile
            {
                EmployeeId = employee.Id,
                TaxNumber = $"BP-DEMO-{person.Number[^4..]}",
                NssaNumber = $"NSSA-DEMO-{person.Number[^4..]}"
            });

            _context.EmployeeContracts.Add(new EmployeeContract
            {
                CompanyId = company.Id,
                EmployeeId = employee.Id,
                EmploymentTypeId = type.Id,
                DepartmentId = department.Id,
                ProjectId = person.TypeCode is "Casual" or "Temporary" ? project.Id : null,
                VersionNumber = 1,
                IsCurrent = true,
                Status = ContractStatus.Active,
                StartDate = new DateOnly(2024, 2, 1),
                EndDate = person.TypeCode == "Contract" ? new DateOnly(2026, 10, 31) : null,
                PayrollCurrency = person.Currency,
                PaymentFrequency = person.Frequency,
                EarningsBasis = person.Basis,
                MonthlyRate = person.Basis == EarningsBasis.MonthlySalary ? person.Rate : null,
                DailyRate = person.Basis == EarningsBasis.DailyRate ? person.Rate : null,
                HourlyRate = person.Basis == EarningsBasis.HourlyRate ? person.Rate : null,
                StandardHoursPerDay = 8m,
                StandardDaysPerWeek = 5m
            });
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await SeedInputsAsync(company.Id, created, period, project, site, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Approved time for the hourly and daily employees, a leave request, and a loan — so the
    /// demonstration shows inputs flowing into a calculation rather than an empty payroll.
    /// </summary>
    private async Task SeedInputsAsync(
        Guid companyId, List<Employee> employees, PayrollPeriod period, Project project,
        ProjectSite site, CancellationToken cancellationToken)
    {
        var hourly = employees.FirstOrDefault(e => e.EmployeeNumber == "EMP-0006");
        var casual = employees.FirstOrDefault(e => e.EmployeeNumber == "EMP-0005");

        foreach (var employee in new[] { hourly, casual }.Where(e => e is not null))
        {
            var timesheet = new Timesheet
            {
                CompanyId = companyId,
                EmployeeId = employee!.Id,
                PayrollPeriodId = period.Id,
                PeriodStart = period.StartDate,
                PeriodEnd = period.EndDate,

                // Approved, by a named demonstration approver, because payroll consumes nothing
                // that has not been approved by somebody.
                ApprovalStatus = InputApprovalStatus.Approved,
                SubmittedBy = "demo-officer",
                SubmittedAt = _clock.Now,
                ApprovedBy = "demo-manager",
                ApprovedAt = _clock.Now
            };

            for (var date = period.StartDate; date <= period.EndDate; date = date.AddDays(1))
            {
                if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                var entry = new TimeEntry
                {
                    WorkDate = date,
                    OrdinaryHours = 8m,
                    DaysWorked = 1m,
                    ProjectId = project.Id,
                    ProjectName = project.Name,
                    ProjectSiteId = site.Id,
                    ProjectSiteName = site.Name
                };

                // A little overtime for the hourly employee, so the demonstration shows a category
                // priced by its own rule.
                if (employee.EmployeeNumber == "EMP-0006" && date.Day is 3 or 10)
                {
                    entry.OvertimeLines.Add(new TimeEntryOvertimeLine
                    {
                        OvertimeCategoryCode = "OT_WEEKDAY",
                        Hours = 2m
                    });
                }

                timesheet.Entries.Add(entry);
            }

            _context.Timesheets.Add(timesheet);
        }

        var salaried = employees.FirstOrDefault(e => e.EmployeeNumber == "EMP-0001");
        if (salaried is not null)
        {
            var unpaid = await _context.LeaveTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == "UNPAID",
                    cancellationToken)
                .ConfigureAwait(false);

            if (unpaid is not null)
            {
                _context.LeaveRequests.Add(new Domain.Leave.LeaveRequest
                {
                    CompanyId = companyId,
                    EmployeeId = salaried.Id,
                    LeaveTypeId = unpaid.Id,
                    StartDate = new DateOnly(2026, 9, 24),
                    EndDate = new DateOnly(2026, 9, 25),
                    Days = 2m,
                    IsPaid = false,
                    Reason = "Demonstration: unpaid leave reduces pay at the daily rate.",
                    ApprovalStatus = InputApprovalStatus.Approved,
                    SubmittedBy = "demo-officer",
                    SubmittedAt = _clock.Now,
                    ApprovedBy = "demo-manager",
                    ApprovedAt = _clock.Now
                });
            }

            var loan = new EmployeeLoan
            {
                CompanyId = companyId,
                EmployeeId = salaried.Id,
                LoanNumber = "LN-DEMO-0001",
                Kind = LoanKind.Loan,
                CurrencyCode = "USD",
                PrincipalAmount = 600m,
                InstalmentCount = 6,
                InstalmentAmount = 100m,
                FirstInstalmentDate = period.PayDate,
                DisbursementDate = new DateOnly(2026, 9, 1),
                DisbursementReference = "DEMO-DISB-0001",
                Status = LoanStatus.Disbursed,
                ApprovalStatus = InputApprovalStatus.Approved,
                SubmittedBy = "demo-officer",
                SubmittedAt = _clock.Now,
                ApprovedBy = "demo-manager",
                ApprovedAt = _clock.Now,
                Purpose = "Demonstration: recovered through payroll, capped at the balance."
            };

            for (var number = 1; number <= 6; number++)
            {
                loan.Instalments.Add(new LoanInstalment
                {
                    InstalmentNumber = number,
                    DueDate = period.PayDate.AddMonths(number - 1),
                    Amount = 100m,
                    PrincipalPortion = 100m,
                    Status = LoanInstalmentStatus.Scheduled
                });
            }

            loan.Transactions.Add(new LoanTransaction
            {
                TransactionType = LoanTransactionType.Disbursement,
                Amount = 600m,
                CurrencyCode = "USD",
                TransactionDate = new DateOnly(2026, 9, 1),
                Reference = "DEMO-DISB-0001"
            });

            _context.EmployeeLoans.Add(loan);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static (string Number, string FirstName, string LastName, string TypeCode,
        EarningsBasis Basis, PaymentFrequency Frequency, decimal Rate, string Currency, string Note)
        Person(string number, string firstName, string lastName, string typeCode,
            EarningsBasis basis, PaymentFrequency frequency, decimal rate, string currency,
            string note) =>
        (number, firstName, lastName, typeCode, basis, frequency, rate, currency, note);
}
