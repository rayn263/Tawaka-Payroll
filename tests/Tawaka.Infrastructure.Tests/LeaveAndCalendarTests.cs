using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Calendars;
using Tawaka.Application.Employees;
using Tawaka.Application.Leave;
using Tawaka.Domain.Calendars;
using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Leave;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// Leave and the holiday calendar as payroll inputs.
/// <para>
/// Two rules under test throughout: an entitlement that has not been established is
/// <b>undetermined</b>, never zero days; and no public holiday is shipped in code.
/// </para>
/// </summary>
public class LeaveAndCalendarTests : EmployeeTestBase
{
    protected sealed record Fixture(
        TestDatabase Db, Guid CompanyId, Employee Employee, PayrollPeriod Period,
        PayrollServices Services) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Fixture> SetUpAsync()
    {
        var (db, companyId, permanentTypeId, _) = await EmployeeTestBase.SetUpAsync();

        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        await contracts.CreateInitialAsync(NewContract(companyId, employee.Id, permanentTypeId, 880m));

        var period = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-09", Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30), TaxYear = 2026, Mode = PayrollMode.Development
        };
        db.Context.PayrollPeriods.Add(period);
        await db.Context.SaveChangesAsync();

        return new Fixture(db, companyId, employee, period, PayrollServices.For(db));
    }

    private static Task<LeaveType> TypeAsync(Fixture fixture, string code) =>
        fixture.Db.Context.LeaveTypes.AsNoTracking().SingleAsync(t => t.Code == code);

    // ---- Entitlements are undetermined, not zero ---------------------------------------------

    [Fact]
    public async Task No_seeded_leave_type_claims_an_entitlement_it_cannot_evidence()
    {
        using var fixture = await SetUpAsync();

        var types = await fixture.Db.Context.LeaveTypes.AsNoTracking().ToListAsync();

        Assert.NotEmpty(types);
        Assert.All(types, t => Assert.Null(t.EntitlementDays));
        Assert.All(types, t => Assert.Null(t.StatutoryEntitlementDays));
        Assert.All(types, t =>
            Assert.Equal(VerificationStatus.Unverified, t.EntitlementVerificationStatus));
    }

    [Fact]
    public async Task Statutory_leave_types_carry_the_compliance_question_blocking_them()
    {
        using var fixture = await SetUpAsync();

        var annual = await TypeAsync(fixture, "ANNUAL");
        var study = await TypeAsync(fixture, "STUDY");

        Assert.Equal("Q32", annual.ComplianceQuestion);

        // Study leave is company policy, so it has no statutory question outstanding.
        Assert.Null(study.ComplianceQuestion);
    }

    [Fact]
    public async Task A_balance_is_undetermined_where_the_entitlement_has_not_been_established()
    {
        using var fixture = await SetUpAsync();
        var annual = await TypeAsync(fixture, "ANNUAL");
        await ApprovedLeaveAsync(fixture, annual.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11));

        var entitlement = await fixture.Services.Leave
            .GetEntitlementAsync(fixture.Employee.Id, annual.Id, new DateOnly(2026, 9, 30));

        Assert.NotNull(entitlement);

        // Days taken are known; the balance is not, because nobody has established the entitlement.
        Assert.Equal(5m, entitlement!.DaysTaken);
        Assert.Null(entitlement.EntitlementDays);
        Assert.Null(entitlement.BalanceDays);
    }

    [Fact]
    public async Task A_balance_is_a_figure_once_the_entitlement_is_established()
    {
        using var fixture = await SetUpAsync();
        var annual = await fixture.Db.Context.LeaveTypes.SingleAsync(t => t.Code == "ANNUAL");
        annual.EntitlementDays = 22m;
        annual.EntitlementSource = "Contract of employment, clause 9";
        annual.EntitlementVerificationStatus = VerificationStatus.Verified;
        await fixture.Db.Context.SaveChangesAsync();
        fixture.Db.Context.ChangeTracker.Clear();

        await ApprovedLeaveAsync(fixture, annual.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11));

        var entitlement = await fixture.Services.Leave
            .GetEntitlementAsync(fixture.Employee.Id, annual.Id, new DateOnly(2026, 9, 30));

        Assert.Equal(22m, entitlement!.EntitlementDays);
        Assert.Equal(17m, entitlement.BalanceDays);
    }

    // ---- Requests and overlap ----------------------------------------------------------------

    [Fact]
    public async Task Overlapping_leave_is_refused()
    {
        using var fixture = await SetUpAsync();
        var annual = await TypeAsync(fixture, "ANNUAL");
        await ApprovedLeaveAsync(fixture, annual.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11));

        var clash = await fixture.Services.Leave.RequestAsync(new LeaveRequestCommand
        {
            EmployeeId = fixture.Employee.Id,
            LeaveTypeId = annual.Id,
            StartDate = new DateOnly(2026, 9, 10),
            EndDate = new DateOnly(2026, 9, 14)
        });

        Assert.False(clash.Succeeded);
        Assert.Contains(clash.Validation.Errors, e => e.Message.Contains("overlaps leave already requested"));
    }

    [Fact]
    public async Task Leave_that_merely_abuts_an_existing_request_is_accepted()
    {
        using var fixture = await SetUpAsync();
        var annual = await TypeAsync(fixture, "ANNUAL");
        await ApprovedLeaveAsync(fixture, annual.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11));

        // Starts the day after the first ends: adjacent, not overlapping.
        var next = await fixture.Services.Leave.RequestAsync(new LeaveRequestCommand
        {
            EmployeeId = fixture.Employee.Id,
            LeaveTypeId = annual.Id,
            StartDate = new DateOnly(2026, 9, 12),
            EndDate = new DateOnly(2026, 9, 15)
        });

        Assert.True(next.Succeeded);
    }

    [Fact]
    public async Task A_rejected_request_does_not_block_a_later_one_for_the_same_days()
    {
        using var fixture = await SetUpAsync();
        var annual = await TypeAsync(fixture, "ANNUAL");

        var first = (await fixture.Services.Leave.RequestAsync(new LeaveRequestCommand
        {
            EmployeeId = fixture.Employee.Id, LeaveTypeId = annual.Id,
            StartDate = new DateOnly(2026, 9, 7), EndDate = new DateOnly(2026, 9, 11)
        })).Value!;
        await fixture.Services.Leave.SubmitAsync(first.Id);

        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        await approver.Leave.RejectAsync(first.Id, "Site cover unavailable");
        fixture.Db.Context.ChangeTracker.Clear();

        var second = await fixture.Services.Leave.RequestAsync(new LeaveRequestCommand
        {
            EmployeeId = fixture.Employee.Id, LeaveTypeId = annual.Id,
            StartDate = new DateOnly(2026, 9, 7), EndDate = new DateOnly(2026, 9, 11)
        });

        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task The_user_who_submitted_leave_cannot_approve_it()
    {
        using var fixture = await SetUpAsync();
        var annual = await TypeAsync(fixture, "ANNUAL");
        var request = (await fixture.Services.Leave.RequestAsync(new LeaveRequestCommand
        {
            EmployeeId = fixture.Employee.Id, LeaveTypeId = annual.Id,
            StartDate = new DateOnly(2026, 9, 7), EndDate = new DateOnly(2026, 9, 11)
        })).Value!;
        await fixture.Services.Leave.SubmitAsync(request.Id);

        var result = await fixture.Services.Leave.ApproveAsync(request.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Segregation of duties"));
    }

    /// <summary>
    /// Withdrawing an approval must give the days back — by reversal, so the ledger keeps a record
    /// of both the taking and the giving back.
    /// </summary>
    [Fact]
    public async Task Withdrawing_an_approval_reverses_the_days_without_deleting_history()
    {
        using var fixture = await SetUpAsync();
        var annual = await TypeAsync(fixture, "ANNUAL");
        var requestId = await ApprovedLeaveAsync(
            fixture, annual.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11));

        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        Assert.True((await approver.Leave.ReturnAsync(requestId, "Dates were wrong")).IsValid);
        fixture.Db.Context.ChangeTracker.Clear();

        var transactions = await fixture.Db.Context.LeaveTransactions.AsNoTracking()
            .Where(t => t.LeaveRequestId == requestId).ToListAsync();

        Assert.Equal(2, transactions.Count);
        Assert.Contains(transactions, t => t.TransactionType == LeaveTransactionType.Taken && t.IsReversed);
        Assert.Contains(transactions, t => t.TransactionType == LeaveTransactionType.Reversal);

        // The reversed row no longer counts, and the reversal row is a record of the undoing
        // rather than a second movement, so the days are back exactly once.
        var entitlement = await fixture.Services.Leave
            .GetEntitlementAsync(fixture.Employee.Id, annual.Id, new DateOnly(2026, 9, 30));
        Assert.Equal(0m, entitlement!.DaysTaken);
    }

    [Fact]
    public async Task An_adjustment_is_a_ledger_row_with_a_reason_not_an_edit()
    {
        using var fixture = await SetUpAsync();
        var annual = await TypeAsync(fixture, "ANNUAL");
        await ApprovedLeaveAsync(fixture, annual.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11));

        var entitlement = await fixture.Services.Leave
            .GetEntitlementAsync(fixture.Employee.Id, annual.Id, new DateOnly(2026, 9, 30));

        var result = await fixture.Services.Leave.AdjustAsync(
            entitlement!.Id, 5m, LeaveTransactionType.OpeningBalance, "Brought forward from 2025");

        Assert.True(result.IsValid);

        var reloaded = await fixture.Services.Leave
            .GetEntitlementAsync(fixture.Employee.Id, annual.Id, new DateOnly(2026, 9, 30));
        Assert.Equal(5m, reloaded!.DaysAccrued);
    }

    [Fact]
    public async Task An_adjustment_without_a_reason_is_refused()
    {
        using var fixture = await SetUpAsync();
        var annual = await TypeAsync(fixture, "ANNUAL");
        await ApprovedLeaveAsync(fixture, annual.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11));
        var entitlement = await fixture.Services.Leave
            .GetEntitlementAsync(fixture.Employee.Id, annual.Id, new DateOnly(2026, 9, 30));

        var result = await fixture.Services.Leave.AdjustAsync(
            entitlement!.Id, 5m, LeaveTransactionType.Adjustment, string.Empty);

        Assert.False(result.IsValid);
    }

    // ---- What payroll sees --------------------------------------------------------------------

    [Fact]
    public async Task Payroll_does_not_consume_unapproved_leave()
    {
        using var fixture = await SetUpAsync();
        var unpaid = await TypeAsync(fixture, "UNPAID");
        var request = (await fixture.Services.Leave.RequestAsync(new LeaveRequestCommand
        {
            EmployeeId = fixture.Employee.Id, LeaveTypeId = unpaid.Id,
            StartDate = new DateOnly(2026, 9, 7), EndDate = new DateOnly(2026, 9, 8)
        })).Value!;
        await fixture.Services.Leave.SubmitAsync(request.Id);
        fixture.Db.Context.ChangeTracker.Clear();

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        Assert.Empty(snapshot!.LeaveEffects);
        Assert.Contains(snapshot.SkippedInputs, s => s.InputType == "LeaveRequest");
    }

    [Fact]
    public async Task Approved_unpaid_leave_reduces_pay_by_the_daily_rate()
    {
        using var fixture = await SetUpAsync();
        var unpaid = await TypeAsync(fixture, "UNPAID");
        await ApprovedLeaveAsync(fixture, unpaid.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 8));

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);
        var result = new Tawaka.Payroll.Engine.PayrollCalculator().Calculate(snapshot!);

        // The seeded divisor is 22 working days: 880 / 22 = 40.00 a day, two days = 80.00.
        var deduction = result.Deductions.Single(d => d.Code == "UNPAID_LEAVE");
        Assert.Equal(80m, deduction.Amount.Amount);
    }

    [Fact]
    public async Task Approved_paid_leave_produces_no_deduction_at_all()
    {
        using var fixture = await SetUpAsync();
        var annual = await TypeAsync(fixture, "ANNUAL");
        await ApprovedLeaveAsync(fixture, annual.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 11));

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);
        var result = new Tawaka.Payroll.Engine.PayrollCalculator().Calculate(snapshot!);

        Assert.Single(snapshot.LeaveEffects);
        Assert.DoesNotContain(result.Deductions, d => d.Code == "UNPAID_LEAVE");
    }

    /// <summary>
    /// Leave crossing a period boundary belongs partly to each period. Counting it whole in both
    /// would dock the employee twice.
    /// </summary>
    [Fact]
    public async Task Leave_spanning_a_period_boundary_is_split_between_the_periods()
    {
        using var fixture = await SetUpAsync();
        var unpaid = await TypeAsync(fixture, "UNPAID");

        // 28 September to 2 October: five calendar days, two of them in September.
        await ApprovedLeaveAsync(
            fixture, unpaid.Id, new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 2), days: 5m);

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        var effect = Assert.Single(snapshot!.LeaveEffects);
        Assert.Equal(3m, effect.Days);
    }

    [Fact]
    public async Task Reclassifying_a_leave_type_does_not_reprice_leave_already_taken()
    {
        using var fixture = await SetUpAsync();
        var study = await TypeAsync(fixture, "STUDY");
        await ApprovedLeaveAsync(fixture, study.Id, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 8));

        // The employer later decides study leave is paid after all. Leave already taken keeps the
        // treatment it was approved under.
        var type = await fixture.Db.Context.LeaveTypes.SingleAsync(t => t.Id == study.Id);
        type.IsPaid = true;
        await fixture.Db.Context.SaveChangesAsync();
        fixture.Db.Context.ChangeTracker.Clear();

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        Assert.False(Assert.Single(snapshot!.LeaveEffects).IsPaid);
    }

    // ---- Holiday calendar ---------------------------------------------------------------------

    [Fact]
    public async Task No_public_holiday_is_shipped_with_the_system()
    {
        using var fixture = await SetUpAsync();

        var calendar = await fixture.Db.Context.HolidayCalendars.AsNoTracking()
            .Include(c => c.Holidays)
            .SingleAsync(c => c.Code == "DEFAULT");

        // The calendar exists so holidays can be captured with their proclamation. It is empty,
        // because a compiled-in list of Zimbabwean holidays would be wrong within a year.
        Assert.Empty(calendar.Holidays);
        Assert.True(calendar.IsDefault);
    }

    [Fact]
    public async Task A_captured_public_holiday_starts_unverified_and_a_company_one_does_not()
    {
        using var fixture = await SetUpAsync();
        var calendar = await fixture.Db.Context.HolidayCalendars.AsNoTracking()
            .SingleAsync(c => c.Code == "DEFAULT");

        var publicHoliday = await fixture.Services.Calendars.AddHolidayAsync(calendar.Id,
            new HolidayCommand { Date = new DateOnly(2026, 4, 18), Name = "Independence Day" });
        var shutdown = await fixture.Services.Calendars.AddHolidayAsync(calendar.Id,
            new HolidayCommand
            {
                Date = new DateOnly(2026, 12, 28), Name = "Site shutdown",
                Kind = HolidayKind.CompanyHoliday
            });

        Assert.Equal(VerificationStatus.Unverified, publicHoliday.Value!.VerificationStatus);
        Assert.True(publicHoliday.Value.RequiresVerification);

        // A company holiday is established by the company deciding it, so it needs no citation.
        Assert.Equal(VerificationStatus.Verified, shutdown.Value!.VerificationStatus);
        Assert.False(shutdown.Value.RequiresVerification);
    }

    [Fact]
    public async Task Verifying_a_public_holiday_requires_a_source()
    {
        using var fixture = await SetUpAsync();
        var calendar = await fixture.Db.Context.HolidayCalendars.AsNoTracking()
            .SingleAsync(c => c.Code == "DEFAULT");
        var holiday = (await fixture.Services.Calendars.AddHolidayAsync(calendar.Id,
            new HolidayCommand { Date = new DateOnly(2026, 4, 18), Name = "Independence Day" })).Value!;

        Assert.False((await fixture.Services.Calendars.VerifyHolidayAsync(holiday.Id, string.Empty)).IsValid);
        Assert.True((await fixture.Services.Calendars
            .VerifyHolidayAsync(holiday.Id, "Public Holidays and Prohibition of Business Act")).IsValid);
    }

    [Fact]
    public async Task The_same_date_cannot_be_a_holiday_twice_on_one_calendar()
    {
        using var fixture = await SetUpAsync();
        var calendar = await fixture.Db.Context.HolidayCalendars.AsNoTracking()
            .SingleAsync(c => c.Code == "DEFAULT");

        await fixture.Services.Calendars.AddHolidayAsync(calendar.Id,
            new HolidayCommand { Date = new DateOnly(2026, 4, 18), Name = "Independence Day" });
        var duplicate = await fixture.Services.Calendars.AddHolidayAsync(calendar.Id,
            new HolidayCommand { Date = new DateOnly(2026, 4, 18), Name = "Independence Day (again)" });

        Assert.False(duplicate.Succeeded);
    }

    [Fact]
    public async Task Removing_a_holiday_deactivates_it_rather_than_deleting_it()
    {
        using var fixture = await SetUpAsync();
        var calendar = await fixture.Db.Context.HolidayCalendars.AsNoTracking()
            .SingleAsync(c => c.Code == "DEFAULT");
        var holiday = (await fixture.Services.Calendars.AddHolidayAsync(calendar.Id,
            new HolidayCommand { Date = new DateOnly(2026, 4, 18), Name = "Independence Day" })).Value!;

        await fixture.Services.Calendars.RemoveHolidayAsync(holiday.Id);
        fixture.Db.Context.ChangeTracker.Clear();

        // A payroll run may have priced a day against it; the run has to stay explicable.
        var stored = await fixture.Db.Context.PublicHolidays.AsNoTracking()
            .SingleAsync(h => h.Id == holiday.Id);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task The_snapshot_records_the_calendar_and_the_holiday_dates_it_used()
    {
        using var fixture = await SetUpAsync();
        var calendar = await fixture.Db.Context.HolidayCalendars.AsNoTracking()
            .SingleAsync(c => c.Code == "DEFAULT");
        await fixture.Services.Calendars.AddHolidayAsync(calendar.Id,
            new HolidayCommand { Date = new DateOnly(2026, 9, 14), Name = "Company shutdown",
                Kind = HolidayKind.CompanyHoliday });
        fixture.Db.Context.ChangeTracker.Clear();

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        Assert.NotNull(snapshot!.HolidayCalendar);
        Assert.Equal(calendar.Id, snapshot.HolidayCalendar!.CalendarId);
        Assert.Contains(new DateOnly(2026, 9, 14), snapshot.HolidayCalendar.HolidayDatesInPeriod);
        Assert.Contains(snapshot.ApprovedInputs, i => i.InputType == "HolidayCalendar");
    }

    [Fact]
    public async Task A_holiday_outside_the_calendars_effective_period_is_refused()
    {
        using var fixture = await SetUpAsync();
        var created = await fixture.Services.Calendars.CreateCalendarAsync(
            fixture.CompanyId, "2026", "2026 calendar",
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), isDefault: false);

        var result = await fixture.Services.Calendars.AddHolidayAsync(created.Value!.Id,
            new HolidayCommand { Date = new DateOnly(2027, 1, 1), Name = "New Year" });

        Assert.False(result.Succeeded);
    }

    private static async Task<Guid> ApprovedLeaveAsync(
        Fixture fixture, Guid leaveTypeId, DateOnly start, DateOnly end, decimal? days = null)
    {
        var request = (await fixture.Services.Leave.RequestAsync(new LeaveRequestCommand
        {
            EmployeeId = fixture.Employee.Id,
            LeaveTypeId = leaveTypeId,
            StartDate = start,
            EndDate = end,
            Days = days
        })).Value!;

        await fixture.Services.Leave.SubmitAsync(request.Id);

        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        var approval = await approver.Leave.ApproveAsync(request.Id);
        Assert.True(approval.IsValid, approval.ToString());

        fixture.Db.Context.ChangeTracker.Clear();
        return request.Id;
    }
}
