using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Loans;
using Tawaka.Domain.Employees;
using Tawaka.Payroll.Engine.Results;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// The guardrails that stop a payroll producing something that is arithmetically consistent and
/// operationally wrong.
/// </summary>
public class QaPayrollGuardrailTests : PayrollFixtureBase
{
    /// <summary>
    /// A loan instalment larger than the pay it comes out of. The loan ledger allows it — the
    /// balance is there to recover — but the payroll must not publish a negative net pay, and the
    /// run must not be approvable until somebody reduces the recovery.
    /// </summary>
    [Fact]
    public async Task A_recovery_larger_than_the_pay_stops_the_run_being_approved()
    {
        using var fixture = await SetUpAsync();
        var services = PayrollServices.For(fixture.Db);

        // The fixture employee earns 850 a month. One instalment of 2,000 cannot come out of it.
        var loan = (await services.Loans.CreateAsync(new LoanCommand
        {
            EmployeeId = fixture.Employee.Id,
            CurrencyCode = "USD",
            PrincipalAmount = 2000m,
            InstalmentCount = 1,
            FirstInstalmentDate = fixture.Period.PayDate
        })).Value!;

        Assert.True((await services.Loans.SubmitAsync(loan.Id)).IsValid);

        fixture.Db.Context.ChangeTracker.Clear();
        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Rudo Moyo"));
        Assert.True((await approver.Loans.ApproveAsync(loan.Id)).IsValid);

        fixture.Db.Context.ChangeTracker.Clear();
        Assert.True((await approver.Loans
            .DisburseAsync(loan.Id, new DateOnly(2026, 9, 1), "FBC-RTGS-2")).IsValid);

        fixture.Db.Context.ChangeTracker.Clear();
        var run = (await fixture.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        Assert.True((await fixture.Runs.CalculateAsync(run.Id)).Succeeded);

        var result = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.UnresolvedItems)
            .SingleAsync(e => e.PayrollRunId == run.Id);

        // No net pay, and the reason is on the record rather than a negative figure.
        Assert.Null(result.NetPayAmount);
        Assert.Contains(
            result.UnresolvedItems,
            u => u.Code == UnresolvedCodes.DeductionsExceedEarnings);
        Assert.NotNull(result.GrossEarningsAmount);

        // And the run cannot be approved while it stands.
        fixture.Db.Context.ChangeTracker.Clear();
        var stored = await fixture.Db.Context.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        stored.CalculatedBy = "u-officer";
        await fixture.Db.Context.SaveChangesAsync();
        fixture.Db.Context.ChangeTracker.Clear();

        var approval = await approver.Runs.ApproveAsync(run.Id);
        Assert.False(approval.IsValid);
        Assert.Contains(approval.Errors, e =>
            e.Message.Contains("could not be calculated", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Where the money is sent is a banking arrangement, not a payroll one. An employee paid in
    /// USD who banks in ZiG is still paid in USD, and no figure on the payroll changes.
    /// </summary>
    [Fact]
    public async Task A_payment_account_in_another_currency_changes_no_payroll_figure()
    {
        using var fixture = await SetUpAsync();

        var withoutAccount = await CalculateOnceAsync(fixture);

        fixture.Db.Context.EmployeePaymentAccounts.Add(new EmployeePaymentAccount
        {
            EmployeeId = fixture.Employee.Id,
            PaymentMethod = PaymentMethod.BankTransfer,
            CurrencyCode = "ZWG",
            AccountName = "T Ncube",
            BankName = "CBZ",
            AccountNumber = "0123456789",
            AllocationType = AllocationType.FullBalance,
            IsActive = true
        });
        await fixture.Db.Context.SaveChangesAsync();
        fixture.Db.Context.ChangeTracker.Clear();

        var withAccount = await CalculateOnceAsync(fixture);

        Assert.Equal("USD", withoutAccount.CurrencyCode);
        Assert.Equal("USD", withAccount.CurrencyCode);
        Assert.Equal(withoutAccount.GrossEarningsAmount, withAccount.GrossEarningsAmount);
        Assert.Equal(withoutAccount.NetPayAmount, withAccount.NetPayAmount);
        Assert.Equal(withoutAccount.PayeAfterCreditsAmount, withAccount.PayeAfterCreditsAmount);

        // The payslip says what the employee is paid in, not what their bank account is in.
        var payslip = await PayrollServices.For(fixture.Db).Payslips.BuildAsync(withAccount.Id);
        Assert.Equal("USD", payslip!.Currency.Value);
    }

    private static async Task<Domain.Payroll.PayrollRunEmployee> CalculateOnceAsync(Fixture fixture)
    {
        var run = (await fixture.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        Assert.True((await fixture.Runs.CalculateAsync(run.Id)).Succeeded);

        fixture.Db.Context.ChangeTracker.Clear();
        return await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == run.Id);
    }
}
