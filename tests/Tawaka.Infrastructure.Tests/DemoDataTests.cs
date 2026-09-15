using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Infrastructure.Seeding;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// The demonstration data set: useful, and impossible to mistake for a real payroll.
/// </summary>
public class DemoDataTests : EmployeeTestBase
{
    [Fact]
    public async Task The_demonstration_set_covers_the_cases_a_payroll_officer_must_understand()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        Assert.True(await new DemoDataSeeder(db.Context, db.Clock).SeedAsync());
        db.Context.ChangeTracker.Clear();

        var contracts = await db.Context.EmployeeContracts.AsNoTracking()
            .Where(c => c.IsCurrent)
            .ToListAsync();

        var types = await db.Context.EmploymentTypes.AsNoTracking()
            .ToDictionaryAsync(t => t.Id, t => t.Code);

        var codes = contracts.Select(c => types[c.EmploymentTypeId]).ToHashSet();

        Assert.Contains("Permanent", codes);
        Assert.Contains("Contract", codes);
        Assert.Contains("Temporary", codes);
        Assert.Contains("Casual", codes);
        Assert.Contains("HourlyPaid", codes);

        // Both currencies, as first-class payrolls rather than one with a footnote.
        Assert.Contains(contracts, c => c.PayrollCurrency == "USD");
        Assert.Contains(contracts, c => c.PayrollCurrency == "ZWG");

        // Every earnings basis the engine treats differently.
        Assert.Contains(contracts, c => c.EarningsBasis == EarningsBasis.MonthlySalary);
        Assert.Contains(contracts, c => c.EarningsBasis == EarningsBasis.DailyRate);
        Assert.Contains(contracts, c => c.EarningsBasis == EarningsBasis.HourlyRate);

        // An employee below the tax threshold, so a legitimate zero PAYE can be demonstrated.
        Assert.Contains(contracts, c => c.PayrollCurrency == "USD" && (c.MonthlyRate ?? 0m) < 300m);

        // Approved inputs, not just master data.
        Assert.NotEmpty(await db.Context.Timesheets.AsNoTracking()
            .Where(t => t.ApprovalStatus == InputApprovalStatus.Approved).ToListAsync());
        Assert.NotEmpty(await db.Context.LeaveRequests.AsNoTracking()
            .Where(r => r.ApprovalStatus == InputApprovalStatus.Approved).ToListAsync());
        Assert.NotEmpty(await db.Context.EmployeeLoans.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task The_installation_is_stamped_as_demonstration_data()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        Assert.False(await DemoDataSeeder.IsDemonstrationAsync(db.Context));

        await new DemoDataSeeder(db.Context, db.Clock).SeedAsync();
        db.Context.ChangeTracker.Clear();

        Assert.True(await DemoDataSeeder.IsDemonstrationAsync(db.Context));

        var company = await db.Context.Companies.AsNoTracking().FirstAsync();
        Assert.Contains("DEMONSTRATION", company.LegalName);
    }

    /// <summary>
    /// The guard that matters: a real company's database must never acquire fictional employees.
    /// </summary>
    [Fact]
    public async Task It_refuses_to_seed_a_database_that_already_holds_employees()
    {
        var (db, companyId, permanentTypeId, _) = await SetUpAsync();
        using var _db = db;

        var employees = new Application.Employees.EmployeeService(db.Context, db.User, db.Clock);
        await employees.CreateAsync(NewEmployee(companyId));
        db.Context.ChangeTracker.Clear();

        Assert.False(await new DemoDataSeeder(db.Context, db.Clock).SeedAsync());

        Assert.Equal(1, await db.Context.Employees.CountAsync());
        Assert.False(await DemoDataSeeder.IsDemonstrationAsync(db.Context));
    }

    /// <summary>
    /// The demonstration period runs in development mode. The live gate does not care what data is
    /// in front of it, and neither does this.
    /// </summary>
    [Fact]
    public async Task The_demonstration_payroll_period_is_development_mode()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        await new DemoDataSeeder(db.Context, db.Clock).SeedAsync();
        db.Context.ChangeTracker.Clear();

        var period = await db.Context.PayrollPeriods.AsNoTracking().SingleAsync();
        Assert.Equal(PayrollMode.Development, period.Mode);
    }

    [Fact]
    public async Task The_demonstration_seeds_no_public_holiday_it_cannot_evidence()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        await new DemoDataSeeder(db.Context, db.Clock).SeedAsync();
        db.Context.ChangeTracker.Clear();

        var holidays = await db.Context.PublicHolidays.AsNoTracking().ToListAsync();

        // A company shutdown, which the company decides, and not one public holiday, which would
        // be a claim about Zimbabwean law that this seeder is in no position to make.
        Assert.All(holidays,
            h => Assert.Equal(Domain.Calendars.HolidayKind.CompanyHoliday, h.Kind));
    }
}
