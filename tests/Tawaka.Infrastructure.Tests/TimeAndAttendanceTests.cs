using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Employees;
using Tawaka.Application.Time;
using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Organisation;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Infrastructure.Interceptors;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// Time and attendance as a controlled payroll <b>input</b>.
/// <para>
/// The rule under test throughout: payroll consumes approved time and nothing else, and what an
/// hour is worth is decided by the engine, never here.
/// </para>
/// </summary>
public class TimeAndAttendanceTests : EmployeeTestBase
{
    protected sealed record Fixture(
        TestDatabase Db, Guid CompanyId, Employee Employee, PayrollPeriod Period,
        PayrollServices Services, Guid EmploymentTypeId) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Fixture> SetUpAsync(
        EarningsBasis basis = EarningsBasis.MonthlySalary, decimal rate = 850m)
    {
        var (db, companyId, permanentTypeId, _) = await EmployeeTestBase.SetUpAsync();

        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;

        var contract = NewContract(companyId, employee.Id, permanentTypeId, rate);
        contract.EarningsBasis = basis;
        contract.HourlyRate = basis == EarningsBasis.HourlyRate ? rate : null;
        contract.DailyRate = basis == EarningsBasis.DailyRate ? rate : null;
        contract.MonthlyRate = basis == EarningsBasis.MonthlySalary ? rate : null;
        await contracts.CreateInitialAsync(contract);

        var period = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-09", Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30), TaxYear = 2026, Mode = PayrollMode.Development
        };
        db.Context.PayrollPeriods.Add(period);
        await db.Context.SaveChangesAsync();

        return new Fixture(db, companyId, employee, period, PayrollServices.For(db), permanentTypeId);
    }

    private static TimeEntryRequest Day(int day, decimal hours = 8m, decimal days = 1m) => new()
    {
        WorkDate = new DateOnly(2026, 9, day),
        OrdinaryHours = hours,
        DaysWorked = days
    };

    // ---- Capture and validation ------------------------------------------------------------

    [Fact]
    public async Task A_timesheet_moves_from_draft_to_submitted_to_approved()
    {
        using var fixture = await SetUpAsync();
        var timesheets = fixture.Services.Timesheets;

        var created = await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id);
        Assert.True(created.Succeeded);
        Assert.Equal(InputApprovalStatus.Draft, created.Value!.ApprovalStatus);

        Assert.True((await timesheets.SetEntriesAsync(created.Value.Id, new[] { Day(1), Day(2) })).IsValid);
        Assert.True((await timesheets.SubmitAsync(created.Value.Id)).IsValid);

        // A different person approves. The submitter cannot, and that is the point of approval.
        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        Assert.True((await approver.Timesheets.ApproveAsync(created.Value.Id)).IsValid);

        var stored = await fixture.Db.Context.Timesheets.AsNoTracking()
            .SingleAsync(t => t.Id == created.Value.Id);
        Assert.Equal(InputApprovalStatus.Approved, stored.ApprovalStatus);
        Assert.Equal("u-manager", stored.ApprovedBy);
        Assert.NotNull(stored.ApprovedAt);
    }

    [Fact]
    public async Task The_user_who_submitted_a_timesheet_cannot_approve_it()
    {
        using var fixture = await SetUpAsync();
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;
        await timesheets.SetEntriesAsync(sheet.Id, new[] { Day(1) });
        await timesheets.SubmitAsync(sheet.Id);

        var result = await timesheets.ApproveAsync(sheet.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Segregation of duties"));
    }

    [Fact]
    public async Task A_day_outside_the_payroll_period_is_refused()
    {
        using var fixture = await SetUpAsync();
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;

        // 31 September does not exist; 1 October is the boundary case that matters.
        var result = await timesheets.SetEntriesAsync(sheet.Id, new[]
        {
            new TimeEntryRequest { WorkDate = new DateOnly(2026, 10, 1), OrdinaryHours = 8m }
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("outside the payroll period"));
    }

    [Theory]
    [InlineData(9, 1)]
    [InlineData(9, 30)]
    public async Task The_first_and_last_day_of_the_period_are_accepted(int month, int day)
    {
        using var fixture = await SetUpAsync();
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;

        var result = await timesheets.SetEntriesAsync(sheet.Id, new[]
        {
            new TimeEntryRequest { WorkDate = new DateOnly(2026, month, day), OrdinaryHours = 8m }
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task The_same_date_cannot_appear_twice_on_one_timesheet()
    {
        using var fixture = await SetUpAsync();
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;

        var result = await timesheets.SetEntriesAsync(sheet.Id, new[] { Day(3), Day(3) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("appears 2 times"));
    }

    [Fact]
    public async Task More_than_twenty_four_hours_cannot_be_claimed_on_one_day()
    {
        using var fixture = await SetUpAsync();
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;

        var result = await timesheets.SetEntriesAsync(sheet.Id, new[]
        {
            new TimeEntryRequest
            {
                WorkDate = new DateOnly(2026, 9, 3),
                OrdinaryHours = 20m,
                Overtime = new Dictionary<string, decimal> { ["OT_WEEKDAY"] = 6m }
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("26.00 hours claimed"));
    }

    [Fact]
    public async Task A_second_live_timesheet_for_the_same_employee_and_period_is_refused()
    {
        using var fixture = await SetUpAsync();
        var timesheets = fixture.Services.Timesheets;
        await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id);

        var second = await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id);

        Assert.False(second.Succeeded);
        Assert.Contains(second.Validation.Errors, e => e.Message.Contains("already has a timesheet"));
    }

    [Fact]
    public async Task An_empty_timesheet_cannot_be_submitted()
    {
        using var fixture = await SetUpAsync();
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;

        var result = await timesheets.SubmitAsync(sheet.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("empty timesheet"));
    }

    [Fact]
    public async Task An_approved_timesheet_cannot_be_edited()
    {
        using var fixture = await SetUpAsync();
        var sheetId = await ApprovedTimesheetAsync(fixture, new[] { Day(1) });

        var result = await fixture.Services.Timesheets.SetEntriesAsync(sheetId, new[] { Day(2) });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("cannot be edited"));
    }

    [Fact]
    public async Task A_returned_timesheet_becomes_editable_again_and_keeps_its_reason()
    {
        using var fixture = await SetUpAsync();
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;
        await timesheets.SetEntriesAsync(sheet.Id, new[] { Day(1) });
        await timesheets.SubmitAsync(sheet.Id);

        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        Assert.True((await approver.Timesheets.ReturnAsync(sheet.Id, "Saturday hours missing")).IsValid);

        var stored = await fixture.Db.Context.Timesheets.AsNoTracking().SingleAsync(t => t.Id == sheet.Id);
        Assert.Equal(InputApprovalStatus.Returned, stored.ApprovalStatus);
        Assert.Equal("Saturday hours missing", stored.DecisionReason);

        Assert.True((await timesheets.SetEntriesAsync(sheet.Id, new[] { Day(1), Day(5) })).IsValid);
    }

    [Fact]
    public async Task Returning_a_timesheet_without_a_reason_is_refused()
    {
        using var fixture = await SetUpAsync();
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;
        await timesheets.SetEntriesAsync(sheet.Id, new[] { Day(1) });
        await timesheets.SubmitAsync(sheet.Id);

        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        var result = await approver.Timesheets.ReturnAsync(sheet.Id, string.Empty);

        Assert.False(result.IsValid);
    }

    // ---- Consumption by payroll -------------------------------------------------------------

    [Fact]
    public async Task Payroll_does_not_consume_an_unapproved_timesheet()
    {
        using var fixture = await SetUpAsync(EarningsBasis.HourlyRate, 5m);
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;
        await timesheets.SetEntriesAsync(sheet.Id, new[] { Day(1), Day(2), Day(3) });
        await timesheets.SubmitAsync(sheet.Id);

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.Timesheet);
        Assert.Contains(snapshot.SkippedInputs,
            s => s.InputType == "Timesheet" && s.Reason.Contains("Submitted"));
    }

    /// <summary>
    /// An hourly employee with no approved time is not paid zero: the quantity is unknown, and
    /// unknown is reported as unresolved so somebody looks at it.
    /// </summary>
    [Fact]
    public async Task An_hourly_employee_without_approved_time_is_unresolved_not_zero()
    {
        using var fixture = await SetUpAsync(EarningsBasis.HourlyRate, 5m);

        var run = (await fixture.Services.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Services.Runs.CalculateAsync(run.Id);

        var row = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.UnresolvedItems)
            .SingleAsync(e => e.PayrollRunId == run.Id);

        Assert.Null(row.GrossEarningsAmount);
        Assert.Contains(row.UnresolvedItems, u => u.Code == "TIMESHEET_MISSING");
    }

    [Fact]
    public async Task Approved_hours_drive_an_hourly_employees_basic_pay()
    {
        using var fixture = await SetUpAsync(EarningsBasis.HourlyRate, 5m);
        await ApprovedTimesheetAsync(fixture, new[] { Day(1), Day(2), Day(3) });

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        Assert.NotNull(snapshot!.Timesheet);
        Assert.Equal(24m, snapshot.Timesheet!.HoursWorked);

        var calculator = new Tawaka.Payroll.Engine.PayrollCalculator();
        var result = calculator.Calculate(snapshot);

        // 24 hours at 5.00 = 120.00, and no assumption of a full period anywhere.
        var basic = result.Earnings.Single(e => e.Code == "BASIC");
        Assert.Equal(120m, basic.Amount.Amount);
        Assert.Equal(24m, basic.Quantity);
    }

    [Fact]
    public async Task The_snapshot_names_the_approved_timesheet_it_consumed()
    {
        using var fixture = await SetUpAsync(EarningsBasis.HourlyRate, 5m);
        var sheetId = await ApprovedTimesheetAsync(fixture, new[] { Day(1) });

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        var reference = Assert.Single(snapshot!.ApprovedInputs.Where(i => i.InputType == "Timesheet"));
        Assert.Equal(sheetId, reference.InputId);
        Assert.Equal("u-manager", reference.ApprovedBy);
        Assert.Equal(sheetId, snapshot.Timesheet!.TimesheetId);
    }

    // ---- Overtime ---------------------------------------------------------------------------

    [Fact]
    public async Task Overtime_is_priced_by_its_own_dated_rule()
    {
        using var fixture = await SetUpAsync(EarningsBasis.HourlyRate, 10m);
        await ApprovedTimesheetAsync(fixture, new[]
        {
            new TimeEntryRequest
            {
                WorkDate = new DateOnly(2026, 9, 6),
                OrdinaryHours = 8m,
                Overtime = new Dictionary<string, decimal> { ["OT_SUNDAY"] = 4m }
            }
        });

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);
        var result = new Tawaka.Payroll.Engine.PayrollCalculator().Calculate(snapshot!);

        // The seeded Sunday rule is 2.0x: 4 hours x 10.00 x 2.0 = 80.00, and the multiplier came
        // from the rule rather than from anything in this test.
        var overtime = result.Earnings.Single(e => e.Code == "OT_SUNDAY");
        Assert.Equal(80m, overtime.Amount.Amount);
    }

    [Fact]
    public async Task Overtime_categories_are_never_merged()
    {
        using var fixture = await SetUpAsync(EarningsBasis.HourlyRate, 10m);
        await ApprovedTimesheetAsync(fixture, new[]
        {
            new TimeEntryRequest
            {
                WorkDate = new DateOnly(2026, 9, 5),
                OrdinaryHours = 8m,
                Overtime = new Dictionary<string, decimal> { ["OT_WEEKDAY"] = 2m }
            },
            new TimeEntryRequest
            {
                WorkDate = new DateOnly(2026, 9, 6),
                Overtime = new Dictionary<string, decimal> { ["OT_SUNDAY"] = 4m }
            }
        });

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);
        var result = new Tawaka.Payroll.Engine.PayrollCalculator().Calculate(snapshot!);

        Assert.Equal(30m, result.Earnings.Single(e => e.Code == "OT_WEEKDAY").Amount.Amount);
        Assert.Equal(80m, result.Earnings.Single(e => e.Code == "OT_SUNDAY").Amount.Amount);
    }

    /// <summary>
    /// The refusal that matters: hours claimed in a category with no established rate produce an
    /// unresolved item naming the category, not a silent 1.0x.
    /// </summary>
    [Fact]
    public async Task Overtime_in_a_category_with_no_rule_is_unresolved_not_paid_at_plain_time()
    {
        using var fixture = await SetUpAsync(EarningsBasis.HourlyRate, 10m);
        await ApprovedTimesheetAsync(fixture, new[]
        {
            new TimeEntryRequest
            {
                WorkDate = new DateOnly(2026, 9, 5),
                OrdinaryHours = 8m,
                Overtime = new Dictionary<string, decimal> { ["OT_NIGHT_SHIFT"] = 3m }
            }
        });

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);
        var result = new Tawaka.Payroll.Engine.PayrollCalculator().Calculate(snapshot!);

        Assert.DoesNotContain(result.Earnings, e => e.Code == "OT_NIGHT_SHIFT");
        Assert.Contains(result.Unresolved, u => u.ItemKey == "OT_NIGHT_SHIFT");
    }

    [Fact]
    public async Task Overtime_for_a_salaried_employee_without_a_divisor_rule_is_unresolved()
    {
        using var fixture = await SetUpAsync();

        // Remove the divisor rule: the question of how a monthly salary becomes an hourly rate
        // then has no answer, and the engine must say so rather than pick one.
        var divisors = await fixture.Db.Context.StatutoryRules
            .Where(r => r.RuleId.StartsWith("PAY-DIVISOR")).ToListAsync();
        fixture.Db.Context.StatutoryRules.RemoveRange(divisors);
        await fixture.Db.Context.SaveChangesAsync();

        await ApprovedTimesheetAsync(fixture, new[]
        {
            new TimeEntryRequest
            {
                WorkDate = new DateOnly(2026, 9, 5),
                OrdinaryHours = 8m,
                Overtime = new Dictionary<string, decimal> { ["OT_WEEKDAY"] = 3m }
            }
        });

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);
        var result = new Tawaka.Payroll.Engine.PayrollCalculator().Calculate(snapshot!);

        Assert.DoesNotContain(result.Earnings, e => e.Code == "OT_WEEKDAY");
        Assert.Contains(result.Unresolved, u => u.ComplianceQuestion == "Q33");
    }

    // ---- Project allocation ------------------------------------------------------------------

    [Fact]
    public async Task Project_allocations_follow_the_hours_actually_booked_and_sum_to_one_hundred()
    {
        using var fixture = await SetUpAsync(EarningsBasis.HourlyRate, 10m);

        var nyanga = new Project
        {
            CompanyId = fixture.CompanyId, Code = "P-001", Name = "Nyanga shop",
            Status = ProjectStatus.Active
        };
        var mutare = new Project
        {
            CompanyId = fixture.CompanyId, Code = "P-002", Name = "Mutare depot",
            Status = ProjectStatus.Active
        };
        fixture.Db.Context.Projects.AddRange(nyanga, mutare);
        await fixture.Db.Context.SaveChangesAsync();

        await ApprovedTimesheetAsync(fixture, new[]
        {
            new TimeEntryRequest { WorkDate = new DateOnly(2026, 9, 1), OrdinaryHours = 8m, ProjectId = nyanga.Id },
            new TimeEntryRequest { WorkDate = new DateOnly(2026, 9, 2), OrdinaryHours = 8m, ProjectId = nyanga.Id },
            new TimeEntryRequest { WorkDate = new DateOnly(2026, 9, 3), OrdinaryHours = 8m, ProjectId = mutare.Id }
        });

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        Assert.Equal(100m, snapshot!.CostAllocations.Sum(a => a.Percent));
        Assert.Equal(66.67m, snapshot.CostAllocations.Single(a => a.ProjectId == nyanga.Id).Percent);
        Assert.Equal(33.33m, snapshot.CostAllocations.Single(a => a.ProjectId == mutare.Id).Percent);
    }

    [Fact]
    public async Task Allocation_rounding_never_leaves_the_total_off_one_hundred()
    {
        using var fixture = await SetUpAsync(EarningsBasis.HourlyRate, 10m);

        var projects = new List<Project>();
        for (var i = 1; i <= 3; i++)
        {
            projects.Add(new Project
            {
                CompanyId = fixture.CompanyId, Code = $"P-{i:D3}", Name = $"Site {i}",
                Status = ProjectStatus.Active
            });
        }

        fixture.Db.Context.Projects.AddRange(projects);
        await fixture.Db.Context.SaveChangesAsync();

        // One day each: three equal thirds, which do not round to 100 without correction.
        await ApprovedTimesheetAsync(fixture, projects.Select((p, index) => new TimeEntryRequest
        {
            WorkDate = new DateOnly(2026, 9, index + 1),
            OrdinaryHours = 8m,
            ProjectId = p.Id
        }).ToList());

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        Assert.Equal(100m, snapshot!.CostAllocations.Sum(a => a.Percent));
    }

    // ---- Locking and corrections ---------------------------------------------------------------

    [Fact]
    public async Task A_locked_timesheet_cannot_be_altered_at_the_data_layer()
    {
        using var fixture = await SetUpAsync();
        var sheetId = await ApprovedTimesheetAsync(fixture, new[] { Day(1) });

        await fixture.Services.Timesheets.LockForRunAsync(new[] { sheetId }, Guid.NewGuid());
        fixture.Db.Context.ChangeTracker.Clear();

        var locked = await fixture.Db.Context.Timesheets.SingleAsync(t => t.Id == sheetId);
        locked.Notes = "Tampered";

        Assert.Throws<PeriodLockedException>(() => fixture.Db.Context.SaveChanges());
    }

    [Fact]
    public async Task A_correction_timesheet_leaves_the_original_exactly_as_it_was()
    {
        using var fixture = await SetUpAsync();
        var originalId = await ApprovedTimesheetAsync(fixture, new[] { Day(1), Day(2) });

        var correction = await fixture.Services.Timesheets
            .CreateCorrectionAsync(originalId, "Two days were captured against the wrong site.");

        Assert.True(correction.Succeeded);
        Assert.Equal(originalId, correction.Value!.CorrectsTimesheetId);

        // The correction starts as a copy and is independently editable.
        Assert.True((await fixture.Services.Timesheets
            .SetEntriesAsync(correction.Value.Id, new[] { Day(1), Day(2), Day(3) })).IsValid);

        var original = await fixture.Db.Context.Timesheets.AsNoTracking()
            .Include(t => t.Entries)
            .SingleAsync(t => t.Id == originalId);

        Assert.Equal(2, original.Entries.Count);
        Assert.Equal(InputApprovalStatus.Approved, original.ApprovalStatus);
    }

    [Fact]
    public async Task An_editable_timesheet_is_corrected_directly_rather_than_superseded()
    {
        using var fixture = await SetUpAsync();
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;
        await timesheets.SetEntriesAsync(sheet.Id, new[] { Day(1) });

        var result = await timesheets.CreateCorrectionAsync(sheet.Id, "Wrong hours");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Message.Contains("still editable"));
    }

    [Fact]
    public async Task An_approved_correction_supersedes_the_original_for_payroll()
    {
        using var fixture = await SetUpAsync(EarningsBasis.HourlyRate, 10m);
        var originalId = await ApprovedTimesheetAsync(fixture, new[] { Day(1), Day(2) });

        var correction = (await fixture.Services.Timesheets
            .CreateCorrectionAsync(originalId, "A third day was worked.")).Value!;
        await fixture.Services.Timesheets
            .SetEntriesAsync(correction.Id, new[] { Day(1), Day(2), Day(3) });
        await fixture.Services.Timesheets.SubmitAsync(correction.Id);

        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        await approver.Timesheets.ApproveAsync(correction.Id);

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        Assert.Equal(correction.Id, snapshot!.Timesheet!.TimesheetId);
        Assert.Equal(24m, snapshot.Timesheet.HoursWorked);
    }

    private static async Task<Guid> ApprovedTimesheetAsync(
        Fixture fixture, IReadOnlyList<TimeEntryRequest> entries)
    {
        var timesheets = fixture.Services.Timesheets;
        var sheet = (await timesheets.CreateAsync(fixture.Employee.Id, fixture.Period.Id)).Value!;
        await timesheets.SetEntriesAsync(sheet.Id, entries);
        await timesheets.SubmitAsync(sheet.Id);

        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        var approval = await approver.Timesheets.ApproveAsync(sheet.Id);
        Assert.True(approval.IsValid, approval.ToString());

        fixture.Db.Context.ChangeTracker.Clear();
        return sheet.Id;
    }
}

/// <summary>
/// TC-33 and TC-20 from the compliance catalogue, which needed timesheets and loans before they
/// could be exercised against the real engine.
/// </summary>
public class CasualEngagementAndAdvanceTests : EmployeeTestBase
{
    /// <summary>
    /// TC-33. The system warns and changes nothing: not the employment type, not leave accrual,
    /// not NSSA treatment. A silent reclassification would be the software making a legal
    /// determination it has no standing to make.
    /// </summary>
    [Fact]
    public async Task Casual_engagement_past_the_threshold_warns_without_reclassifying()
    {
        var (db, companyId, _, _) = await EmployeeTestBase.SetUpAsync();
        using var _db = db;

        var casual = await db.Context.EmploymentTypes.AsNoTracking()
            .SingleAsync(t => t.Code == "Casual");
        Assert.NotNull(casual.EngagementWarningDays);

        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;

        var contract = NewContract(companyId, employee.Id, casual.Id, 20m);
        contract.EarningsBasis = EarningsBasis.DailyRate;
        contract.DailyRate = 20m;
        contract.MonthlyRate = null;
        await contracts.CreateInitialAsync(contract);

        var period = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-09", Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30), TaxYear = 2026, Mode = PayrollMode.Development
        };
        db.Context.PayrollPeriods.Add(period);
        await db.Context.SaveChangesAsync();

        // The seeded threshold is 42 days in four consecutive months. The tally is rolling, so it
        // has to reach across periods: July, August and September together carry this worker past
        // it, and no single month would.
        var july = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-07", Name = "July 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 7, 31),
            PayDate = new DateOnly(2026, 7, 31), TaxYear = 2026, Mode = PayrollMode.Development
        };
        var august = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-08", Name = "August 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 8, 1), EndDate = new DateOnly(2026, 8, 31),
            PayDate = new DateOnly(2026, 8, 31), TaxYear = 2026, Mode = PayrollMode.Development
        };
        db.Context.PayrollPeriods.AddRange(july, august);
        await db.Context.SaveChangesAsync();

        var services = PayrollServices.For(db);

        foreach (var (target, month, lastDay) in new[]
                 {
                     (july, 7, 31), (august, 8, 31), (period, 9, 30)
                 })
        {
            var sheet = (await services.Timesheets.CreateAsync(employee.Id, target.Id)).Value!;

            var entries = Enumerable.Range(1, lastDay)
                .Select(day => new DateOnly(2026, month, day))
                .Where(date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                .Select(date => new TimeEntryRequest
                {
                    WorkDate = date, OrdinaryHours = 8m, DaysWorked = 1m
                })
                .ToList();

            await services.Timesheets.SetEntriesAsync(sheet.Id, entries);
            await services.Timesheets.SubmitAsync(sheet.Id);
            await PayrollServices.For(db, new TestUser("u-manager", "Manager"))
                .Timesheets.ApproveAsync(sheet.Id);
            db.Context.ChangeTracker.Clear();
        }

        var snapshot = await services.Snapshots
            .BuildAsync(employee.Id, period, PayrollMode.Development);
        var result = new Tawaka.Payroll.Engine.PayrollCalculator().Calculate(snapshot!);

        Assert.NotNull(snapshot!.CasualEngagement);
        Assert.True(snapshot.CasualEngagement!.HasPassed);
        var warning = Assert.Single(result.Warnings
            .Where(w => w.Code == "CASUAL_ENGAGEMENT_THRESHOLD"));
        Assert.Contains("s.12(3)", warning.Message);
        Assert.Contains("has not changed anything", warning.Message);

        // Nothing was changed by the warning. The contract is still on the casual type.
        var stored = await db.Context.EmployeeContracts.AsNoTracking()
            .SingleAsync(c => c.EmployeeId == employee.Id);
        Assert.Equal(casual.Id, stored.EmploymentTypeId);
    }

    [Fact]
    public async Task A_short_engagement_produces_no_warning()
    {
        var (db, companyId, _, _) = await EmployeeTestBase.SetUpAsync();
        using var _db = db;

        var casual = await db.Context.EmploymentTypes.AsNoTracking()
            .SingleAsync(t => t.Code == "Casual");

        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;

        var contract = NewContract(companyId, employee.Id, casual.Id, 20m);
        contract.EarningsBasis = EarningsBasis.DailyRate;
        contract.DailyRate = 20m;
        contract.MonthlyRate = null;
        await contracts.CreateInitialAsync(contract);

        var period = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-09", Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30), TaxYear = 2026, Mode = PayrollMode.Development
        };
        db.Context.PayrollPeriods.Add(period);
        await db.Context.SaveChangesAsync();

        var services = PayrollServices.For(db);
        var sheet = (await services.Timesheets.CreateAsync(employee.Id, period.Id)).Value!;
        await services.Timesheets.SetEntriesAsync(sheet.Id, new[]
        {
            new TimeEntryRequest { WorkDate = new DateOnly(2026, 9, 1), OrdinaryHours = 8m, DaysWorked = 1m },
            new TimeEntryRequest { WorkDate = new DateOnly(2026, 9, 2), OrdinaryHours = 8m, DaysWorked = 1m }
        });
        await services.Timesheets.SubmitAsync(sheet.Id);
        await PayrollServices.For(db, new TestUser("u-manager", "Manager"))
            .Timesheets.ApproveAsync(sheet.Id);
        db.Context.ChangeTracker.Clear();

        var snapshot = await services.Snapshots
            .BuildAsync(employee.Id, period, PayrollMode.Development);
        var result = new Tawaka.Payroll.Engine.PayrollCalculator().Calculate(snapshot!);

        Assert.False(snapshot!.CasualEngagement!.IsApproaching);
        Assert.DoesNotContain(result.Warnings, w => w.Code == "CASUAL_ENGAGEMENT_THRESHOLD");
    }

    /// <summary>
    /// TC-20. A salary advance is recovered from net pay: it is a repayment of money already
    /// given, not an expense, so it does not reduce taxable income.
    /// </summary>
    [Fact]
    public async Task An_advance_is_recovered_after_tax_and_does_not_reduce_taxable_income()
    {
        var (db, companyId, permanentTypeId, _) = await EmployeeTestBase.SetUpAsync();
        using var _db = db;

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

        var services = PayrollServices.For(db);
        var advance = (await services.Loans.CreateAsync(new Tawaka.Application.Loans.LoanCommand
        {
            EmployeeId = employee.Id,
            Kind = Tawaka.Domain.Loans.LoanKind.SalaryAdvance,
            CurrencyCode = "USD",
            PrincipalAmount = 200m,
            InstalmentCount = 1,
            FirstInstalmentDate = new DateOnly(2026, 9, 30)
        })).Value!;

        await services.Loans.SubmitAsync(advance.Id);
        await PayrollServices.For(db, new TestUser("u-manager", "Manager"))
            .Loans.ApproveAsync(advance.Id);
        await services.Loans.DisburseAsync(advance.Id, new DateOnly(2026, 9, 10), "CASH-ADV-1");
        db.Context.ChangeTracker.Clear();

        var snapshot = await services.Snapshots
            .BuildAsync(employee.Id, period, PayrollMode.Development);
        var result = new Tawaka.Payroll.Engine.PayrollCalculator().Calculate(snapshot!);

        var deduction = result.Deductions.Single(d => d.Code == "ADVANCE");
        Assert.Equal(200m, deduction.Amount.Amount);
        Assert.False(deduction.ReducesTaxableIncome);

        // Taxable income is unaffected by the recovery: the employee was taxed on the pay when it
        // was earned, and an advance is not a second event.
        var withoutAdvance = result.GrossEarnings!.Value.Amount - result.NssaEmployee!.Value.Amount;
        Assert.Equal(withoutAdvance, result.TaxableIncome!.Value.Amount);
    }
}
