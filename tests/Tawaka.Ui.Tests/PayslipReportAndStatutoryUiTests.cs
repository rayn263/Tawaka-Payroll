using Bunit;
using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory.Obligations;
using Tawaka.Ui.Shared.Components.Pages;

namespace Tawaka.Ui.Tests;

/// <summary>
/// The screens that present a finished payroll: the payslip, the reports and the statutory
/// obligations. None of them is allowed to work out a figure of its own — every number here must
/// be one the engine already stored.
/// </summary>
public sealed class PayslipReportAndStatutoryUiTests : PayrollUiScenario
{
    private (PayrollRun Run, PayrollRunEmployee Employee) FinalisedPayroll()
    {
        VerifyStatutoryRulesForTesting();
        CreateEmployee("EMP-0001", 850m, "USD");
        var period = CreatePeriod(PayrollMode.Live);
        var run = CreateRun(period.Id);

        Session.SignOut();
        SignInAsOfficer();
        var preview = RenderComponent<PayrollPreview>(p => p.Add(x => x.RunId, run.Id));
        Button(preview, "Calculate").Click();

        Session.SignOut();
        SignInAsManager();
        var approval = RenderComponent<PayrollPreview>(p => p.Add(x => x.RunId, run.Id));
        Button(approval, "Approve").Click();

        Session.SignOut();
        SignInAsOfficer();
        Button(RenderComponent<PayrollRuns>(), "Finalise").Click();

        Db.ChangeTracker.Clear();
        return (
            Db.PayrollRuns.AsNoTracking().Single(r => r.Id == run.Id),
            Db.PayrollRunEmployees.AsNoTracking().Single(e => e.PayrollRunId == run.Id));
    }

    [Fact]
    public void The_payslip_shows_the_stored_figures_and_names_its_currency()
    {
        var (_, runEmployee) = FinalisedPayroll();

        var page = RenderComponent<PayslipView>(
            p => p.Add(x => x.RunEmployeeId, runEmployee.Id));

        Assert.Contains("PAYSLIP", page.Markup);
        Assert.Contains("CURRENCY: USD", page.Markup);
        Assert.Contains(runEmployee.EmployeeNumber, page.Markup);
        Assert.Contains(runEmployee.NetPayAmount!.Value.ToString("N2"), page.Markup);
        Assert.Contains(runEmployee.GrossEarningsAmount!.Value.ToString("N2"), page.Markup);
        Assert.Contains(runEmployee.PayeAfterCreditsAmount!.Value.ToString("N2"), page.Markup);

        // A live payslip carries no development watermark.
        Assert.DoesNotContain("NOT FOR STATUTORY USE", page.Markup);
    }

    [Fact]
    public void A_development_payslip_is_watermarked()
    {
        CreateEmployee("EMP-0009", 850m, "USD");
        var period = CreatePeriod(PayrollMode.Development);
        var run = CreateRun(period.Id);

        Session.SignOut();
        SignInAsOfficer();
        Button(RenderComponent<PayrollPreview>(p => p.Add(x => x.RunId, run.Id)), "Calculate")
            .Click();

        Db.ChangeTracker.Clear();
        var runEmployee = Db.PayrollRunEmployees.AsNoTracking().Single(e => e.PayrollRunId == run.Id);

        var page = RenderComponent<PayslipView>(
            p => p.Add(x => x.RunEmployeeId, runEmployee.Id));

        Assert.Contains("DEVELOPMENT — NOT FOR STATUTORY USE", page.Markup);
    }

    [Fact]
    public void Every_report_tab_renders_and_reconciles_with_what_was_stored()
    {
        var (run, runEmployee) = FinalisedPayroll();

        var page = RenderComponent<Reports>(p => p.Add(x => x.RunId, run.Id));

        Assert.Contains("September 2026", page.Markup);
        Assert.Contains("Currency summary", page.Markup);

        var tabs = page.FindAll("button.tab").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(14, tabs.Count);

        foreach (var label in tabs)
        {
            page.FindAll("button.tab").First(t => t.TextContent.Trim() == label).Click();

            Assert.DoesNotContain("Page not found", page.Markup);

            // Nothing in this payroll is in ZiG, so no ZiG section may appear on any tab: a report
            // never merges currencies and never invents a section for one that is not there.
            Assert.DoesNotContain("— ZiG", page.Markup);
        }

        // Back to the figures themselves: the register must show what was stored, to the cent.
        page.FindAll("button.tab").First(t => t.TextContent.Trim() == "Payroll register").Click();

        Assert.Contains("Payroll register — USD", page.Markup);
        Assert.Contains(runEmployee.NetPayAmount!.Value.ToString("N2"), page.Markup);
        Assert.Contains(runEmployee.GrossEarningsAmount!.Value.ToString("N2"), page.Markup);
        Assert.Contains(runEmployee.PayeAfterCreditsAmount!.Value.ToString("N2"), page.Markup);
    }

    /// <summary>
    /// Finalising creates the obligations. Approving one, then paying part of it, must leave the
    /// balance outstanding — and paying the employees must never mark the authorities paid.
    /// </summary>
    [Fact]
    public void An_obligation_paid_in_part_stays_outstanding_on_the_screen()
    {
        var (run, _) = FinalisedPayroll();

        Session.SignOut();
        SignIn("Anesu Chirwa", Tawaka.Domain.Security.RoleNames.Administrator,
            Tawaka.Domain.Security.Permissions.StatutoryView,
            Tawaka.Domain.Security.Permissions.StatutoryRecordPayment);

        var page = RenderComponent<StatutoryObligations>();
        Assert.Contains("USD obligations", page.Markup);

        var paye = Db.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Single(o => o.PayrollRunId == run.Id &&
                         o.ObligationType == StatutoryObligationType.Paye);

        Assert.False(paye.IsPaid);

        Button(page, "Approve").Click();
        page.Render();

        Button(page, "Record payment").Click();

        var half = decimal.Round(paye.CalculatedAmount / 2m, 2);
        Field(page, "Amount").Change(half.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Field(page, "Payment date").Change("2026-10-08");
        Field(page, "Payment reference").Change("FBC-RTGS-90114");

        // The panel's own button, not the one in the table row that opened it.
        page.FindAll("button").Last(b => b.TextContent.Trim() == "Record payment").Click();

        Db.ChangeTracker.Clear();
        var after = Db.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Single(o => o.Id == paye.Id);

        Assert.False(after.IsPaid);
        Assert.Equal(half, after.PaidAmount.Amount);
        Assert.Equal(paye.CalculatedAmount - half, after.Outstanding.Amount);
    }

    /// <summary>
    /// A payment recorded in error has to be undoable, and undoing it has to put the money back on
    /// the balance without erasing that it was recorded.
    /// </summary>
    [Fact]
    public void A_payment_can_be_reversed_from_the_screen_and_the_balance_comes_back()
    {
        var (run, _) = FinalisedPayroll();

        Session.SignOut();
        SignIn("Anesu Chirwa", Tawaka.Domain.Security.RoleNames.Administrator,
            Tawaka.Domain.Security.Permissions.StatutoryView,
            Tawaka.Domain.Security.Permissions.StatutoryRecordPayment);

        var page = RenderComponent<StatutoryObligations>();

        var paye = Db.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Single(o => o.PayrollRunId == run.Id &&
                         o.ObligationType == StatutoryObligationType.Paye);

        Button(page, "Approve").Click();
        page.Render();
        Button(page, "Record payment").Click();

        Field(page, "Amount").Change(
            paye.CalculatedAmount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Field(page, "Payment date").Change("2026-10-08");
        Field(page, "Payment reference").Change("RTGS-WRONG");
        page.FindAll("button").Last(b => b.TextContent.Trim() == "Record payment").Click();

        Db.ChangeTracker.Clear();
        Assert.True(Db.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments).Single(o => o.Id == paye.Id).IsPaid);

        // Now undo it.
        Button(page, "Payments").Click();
        Button(page, "Reverse").Click();
        Field(page, "Reason").Change("Paid against the wrong obligation.");
        Button(page, "Reverse payment").Click();

        Db.ChangeTracker.Clear();
        var after = Db.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Single(o => o.Id == paye.Id);

        Assert.False(after.IsPaid);
        Assert.Equal(paye.CalculatedAmount, after.Outstanding.Amount);

        var payment = Assert.Single(after.Payments);
        Assert.True(payment.IsReversed);
        Assert.Equal("Paid against the wrong obligation.", payment.ReversalReason);
    }

    [Fact]
    public void A_user_without_the_payment_permission_is_not_offered_the_payment_button()
    {
        FinalisedPayroll();

        Session.SignOut();
        SignIn("Viewer", Tawaka.Domain.Security.RoleNames.Viewer,
            Tawaka.Domain.Security.Permissions.StatutoryView);

        var page = RenderComponent<StatutoryObligations>();

        Assert.DoesNotContain("Record payment", page.Markup);
        Assert.DoesNotContain(">Approve<", page.Markup);
    }
}
