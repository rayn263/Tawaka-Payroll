using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tawaka.Application.Employees;
using Tawaka.Application.Payroll;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory;
using Tawaka.Ui.Shared.Components.Pages;

namespace Tawaka.Ui.Tests;

/// <summary>
/// The payroll lifecycle driven from the screens rather than from the services: the buttons a
/// payroll officer and a manager actually press, in the order they press them, with the figures on
/// the screen checked against the figures in the database.
/// </summary>
public sealed class PayrollJourneyTests : PayrollUiScenario
{
    [Fact]
    public void The_payroll_screen_offers_a_new_run_only_to_a_user_who_may_create_one()
    {
        VerifyStatutoryRulesForTesting();
        CreatePeriod(PayrollMode.Live);

        Session.SignOut();
        SignInAsManager();
        Assert.DoesNotContain("New run", RenderComponent<PayrollRuns>().Markup);

        Session.SignOut();
        SignInAsOfficer();
        Assert.Contains("New run", RenderComponent<PayrollRuns>().Markup);
    }

    [Fact]
    public void A_payroll_period_can_be_created_from_the_screen()
    {
        SignInAsOfficer();
        var page = RenderComponent<PayrollRuns>();

        Field(page, "Code").Change("2026-10");
        Field(page, "Name").Change("October 2026");
        Field(page, "Start date").Change("2026-10-01");
        Field(page, "End date").Change("2026-10-31");
        Field(page, "Pay date").Change("2026-10-30");

        Button(page, "Create period").Click();

        var period = Db.PayrollPeriods.AsNoTracking().Single();
        Assert.Equal("2026-10", period.Code);
        Assert.Equal(new DateOnly(2026, 10, 31), period.EndDate);
        Assert.Contains("October 2026", page.Markup);
    }

    /// <summary>
    /// Calculate, then check that every figure on the preview is the figure that was stored. The
    /// screen must be displaying the engine's result, not arriving at one of its own.
    /// </summary>
    [Fact]
    public void The_preview_shows_the_stored_result_and_computes_nothing_itself()
    {
        VerifyStatutoryRulesForTesting();
        CreateEmployee("EMP-0001", 850m, "USD");
        var period = CreatePeriod(PayrollMode.Live);
        var run = CreateRun(period.Id);

        Session.SignOut();
        SignInAsOfficer();
        var page = RenderComponent<PayrollPreview>(p => p.Add(x => x.RunId, run.Id));
        Button(page, "Calculate").Click();

        var stored = Db.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.UnresolvedItems).Single(e => e.PayrollRunId == run.Id);
        Assert.True(stored.IsCalculated,
            string.Join(" | ", stored.UnresolvedItems.Select(u => $"{u.ItemKey}: {u.Message}")));

        foreach (var amount in new[]
                 {
                     stored.GrossEarningsAmount, stored.TaxableIncomeAmount,
                     stored.PayeAfterCreditsAmount, stored.NssaEmployeeAmount, stored.NetPayAmount
                 })
        {
            Assert.NotNull(amount);
            Assert.Contains(amount!.Value.ToString("N2"), page.Markup);
        }

        Assert.Contains("USD payroll — 1 employees", page.Markup);
        Assert.DoesNotContain("unresolved-row", page.Markup);
    }

    [Fact]
    public void Whoever_calculated_a_run_is_refused_when_they_try_to_approve_it()
    {
        VerifyStatutoryRulesForTesting();
        CreateEmployee("EMP-0001", 850m, "USD");
        var period = CreatePeriod(PayrollMode.Live);
        var run = CreateRun(period.Id);

        Session.SignOut();
        SignInAsOfficer();
        var calculating = RenderComponent<PayrollPreview>(p => p.Add(x => x.RunId, run.Id));
        Button(calculating, "Calculate").Click();

        // The same person, now also holding the approval permission, must still be refused.
        Session.SignOut();
        SignIn(Officer, RoleNames.PayrollOfficer,
            OfficerPermissions.Append(Permissions.PayrollApprove).ToArray());

        Assert.Equal(PayrollRunStatus.Review, Status(run.Id));
        var approving = RenderComponent<PayrollPreview>(p => p.Add(x => x.RunId, run.Id));
        Button(approving, "Approve").Click();

        Assert.Contains("Segregation of duties", approving.Markup);
        Assert.Equal(
            PayrollRunStatus.Review,
            Db.PayrollRuns.AsNoTracking().Single(r => r.Id == run.Id).Status);
    }

    /// <summary>
    /// The whole lifecycle from the screens: calculate, approve as a second person, finalise, pay
    /// the wages, lock — and at each step check the state that step is supposed to have produced.
    /// </summary>
    [Fact]
    public void The_lifecycle_runs_from_the_screens_and_ends_locked()
    {
        VerifyStatutoryRulesForTesting();
        CreateEmployee("EMP-0001", 850m, "USD");
        var period = CreatePeriod(PayrollMode.Live);
        var run = CreateRun(period.Id);

        Session.SignOut();
        SignInAsOfficer();
        var preview = RenderComponent<PayrollPreview>(p => p.Add(x => x.RunId, run.Id));
        Button(preview, "Calculate").Click();
        Assert.Equal(PayrollRunStatus.Review, Status(run.Id));

        Session.SignOut();
        SignInAsManager();
        var approval = RenderComponent<PayrollPreview>(p => p.Add(x => x.RunId, run.Id));
        Button(approval, "Approve").Click();
        Assert.Equal(PayrollRunStatus.Approved, Status(run.Id));

        Session.SignOut();
        SignInAsOfficer();

        // Finalising is what creates the statutory obligations.
        Assert.Empty(Db.StatutoryObligations.AsNoTracking().ToList());
        var runs = RenderComponent<PayrollRuns>();
        Button(runs, "Finalise").Click();
        Assert.Equal(PayrollRunStatus.Finalised, Status(run.Id));

        var obligations = Db.StatutoryObligations.AsNoTracking().Include(o => o.Payments).ToList();
        Assert.NotEmpty(obligations);
        Assert.All(obligations, o => Assert.False(o.IsPaid));

        // Paying the employees says nothing about the authorities.
        Button(runs, "Net wages paid").Click();
        Assert.Equal(PayrollRunStatus.Paid, Status(run.Id));
        Assert.All(
            Db.StatutoryObligations.AsNoTracking().Include(o => o.Payments).ToList(),
            o => Assert.False(o.IsPaid));

        Button(runs, "Lock").Click();
        Assert.Equal(PayrollRunStatus.Locked, Status(run.Id));

        // Once locked the screen stops offering anything that would change the figures.
        var afterLock = RenderComponent<PayrollRuns>();
        Assert.DoesNotContain(">Finalise<", afterLock.Markup);
        Assert.DoesNotContain(">Net wages paid<", afterLock.Markup);
        Assert.DoesNotContain(">Lock<", afterLock.Markup);
    }

    /// <summary>
    /// A payroll the rules cannot support must show the gap, not a zero. This renders a ZiG
    /// payroll, for which the seed ships no verified monthly table (compliance question Q29).
    /// </summary>
    [Fact]
    public void A_figure_that_cannot_be_calculated_is_shown_as_unresolved_and_never_as_zero()
    {
        CreateEmployee("EMP-0002", 15000m, "ZWG");
        var period = CreatePeriod(PayrollMode.Development);
        var run = CreateRun(period.Id);

        Session.SignOut();
        SignInAsOfficer();
        var page = RenderComponent<PayrollPreview>(p => p.Add(x => x.RunId, run.Id));
        Button(page, "Calculate").Click();

        var stored = Db.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.UnresolvedItems)
            .Single(e => e.PayrollRunId == run.Id);

        Assert.NotEmpty(stored.UnresolvedItems);
        Assert.Null(stored.NetPayAmount);
        Assert.Contains("unresolved", page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ZiG payroll", page.Markup);
        Assert.Contains("DEVELOPMENT CALCULATION", page.Markup);
    }

}
