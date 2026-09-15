using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Employees;
using Tawaka.Application.Loans;
using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Loans;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// Loans and advances, and their recovery through payroll.
/// <para>
/// The rules under test: the outstanding balance is always the ledger, and a deduction never takes
/// more than is owed unless somebody named has approved that it may.
/// </para>
/// </summary>
public class LoanTests : EmployeeTestBase
{
    protected sealed record Fixture(
        TestDatabase Db, Guid CompanyId, Employee Employee, PayrollPeriod Period,
        PayrollServices Services) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Fixture> SetUpAsync(string currency = "USD")
    {
        var (db, companyId, permanentTypeId, _) = await EmployeeTestBase.SetUpAsync();

        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        await contracts.CreateInitialAsync(
            NewContract(companyId, employee.Id, permanentTypeId, 850m, currency));

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

    private static LoanCommand Command(Fixture fixture, decimal principal = 600m,
        int instalments = 6, decimal interest = 0m, string currency = "USD") => new()
    {
        EmployeeId = fixture.Employee.Id,
        CurrencyCode = currency,
        PrincipalAmount = principal,
        InstalmentCount = instalments,
        InterestAmount = interest,
        FirstInstalmentDate = new DateOnly(2026, 9, 30),
        Purpose = "School fees"
    };

    // ---- Schedule -----------------------------------------------------------------------------

    [Fact]
    public async Task A_schedule_is_built_and_its_instalments_sum_to_exactly_what_is_repayable()
    {
        using var fixture = await SetUpAsync();

        var loan = (await fixture.Services.Loans.CreateAsync(Command(fixture))).Value!;

        Assert.Equal(6, loan.Instalments.Count);
        Assert.Equal(600m, loan.Instalments.Sum(i => i.Amount));
        Assert.Equal(loan.TotalRepayable.Amount, loan.Instalments.Sum(i => i.Amount));
    }

    /// <summary>
    /// A schedule that does not add up is a dispute waiting to happen: 100 over 3 instalments
    /// cannot be three equal thirds, and the last instalment must absorb the difference.
    /// </summary>
    [Fact]
    public async Task Rounding_is_absorbed_by_the_last_instalment_so_the_schedule_still_adds_up()
    {
        using var fixture = await SetUpAsync();

        var loan = (await fixture.Services.Loans
            .CreateAsync(Command(fixture, principal: 100m, instalments: 3))).Value!;

        Assert.Equal(100m, loan.Instalments.Sum(i => i.Amount));
        Assert.Equal(33.33m, loan.Instalments.First().Amount);
        Assert.Equal(33.34m, loan.Instalments.Last().Amount);
    }

    [Fact]
    public async Task Interest_is_carried_on_the_schedule_and_included_in_what_is_repayable()
    {
        using var fixture = await SetUpAsync();

        var loan = (await fixture.Services.Loans
            .CreateAsync(Command(fixture, principal: 600m, instalments: 6, interest: 60m))).Value!;

        Assert.Equal(660m, loan.TotalRepayable.Amount);
        Assert.Equal(660m, loan.Instalments.Sum(i => i.Amount));
        Assert.Equal(600m, loan.Instalments.Sum(i => i.PrincipalPortion));
    }

    // ---- Lifecycle ----------------------------------------------------------------------------

    [Fact]
    public async Task The_user_who_raised_a_loan_cannot_approve_it()
    {
        using var fixture = await SetUpAsync();
        var loan = (await fixture.Services.Loans.CreateAsync(Command(fixture))).Value!;
        await fixture.Services.Loans.SubmitAsync(loan.Id);

        var result = await fixture.Services.Loans.ApproveAsync(loan.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("Segregation of duties"));
    }

    [Fact]
    public async Task A_disbursement_requires_a_reference()
    {
        using var fixture = await SetUpAsync();
        var loanId = await ApprovedLoanAsync(fixture);

        var result = await fixture.Services.Loans
            .DisburseAsync(loanId, new DateOnly(2026, 8, 31), string.Empty);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Disbursing_creates_the_ledger_movement_that_creates_the_balance()
    {
        using var fixture = await SetUpAsync();
        var loanId = await DisbursedLoanAsync(fixture);

        var loan = await LoadAsync(fixture, loanId);

        Assert.Equal(LoanStatus.Disbursed, loan.Status);
        Assert.Equal(600m, loan.Outstanding.Amount);
        Assert.Single(loan.Transactions.Where(t => t.TransactionType == LoanTransactionType.Disbursement));
    }

    [Fact]
    public async Task An_undisbursed_loan_produces_no_payroll_deduction()
    {
        using var fixture = await SetUpAsync();
        await ApprovedLoanAsync(fixture);

        var due = await fixture.Services.Loans
            .GetDueDeductionsAsync(fixture.Employee.Id, fixture.Period.Id, fixture.Period.EndDate);

        Assert.Empty(due);
    }

    // ---- Balance and recovery -------------------------------------------------------------------

    [Fact]
    public async Task The_outstanding_balance_reconciles_to_the_ledger_after_every_movement()
    {
        using var fixture = await SetUpAsync();
        var loanId = await DisbursedLoanAsync(fixture);

        await fixture.Services.Loans.RecordManualRepaymentAsync(
            loanId, 100m, new DateOnly(2026, 9, 30), "CASH-001", isEarlySettlement: false);
        await fixture.Services.Loans.RecordManualRepaymentAsync(
            loanId, 50m, new DateOnly(2026, 10, 31), "CASH-002", isEarlySettlement: false);
        fixture.Db.Context.ChangeTracker.Clear();

        var loan = await LoadAsync(fixture, loanId);

        Assert.Equal(150m, loan.TotalRepaid.Amount);
        Assert.Equal(450m, loan.Outstanding.Amount);
        Assert.Equal(loan.TotalAdvanced.Amount + loan.InterestAmount - loan.TotalRepaid.Amount,
            loan.Outstanding.Amount);
    }

    [Fact]
    public async Task A_deduction_is_capped_at_the_outstanding_balance()
    {
        using var fixture = await SetUpAsync();

        // One instalment of 600 against a loan already almost repaid.
        var loanId = await DisbursedLoanAsync(fixture, principal: 600m, instalments: 1);
        await fixture.Services.Loans.RecordManualRepaymentAsync(
            loanId, 550m, new DateOnly(2026, 9, 1), "CASH-001", isEarlySettlement: false);
        fixture.Db.Context.ChangeTracker.Clear();

        var due = await fixture.Services.Loans
            .GetDueDeductionsAsync(fixture.Employee.Id, fixture.Period.Id, fixture.Period.EndDate);

        var deduction = Assert.Single(due);
        Assert.Equal(50m, deduction.Amount.Amount);
        Assert.True(deduction.WasCapped);
        Assert.Equal(50m, deduction.OutstandingBefore.Amount);
    }

    [Fact]
    public async Task A_manual_repayment_exceeding_the_balance_is_refused()
    {
        using var fixture = await SetUpAsync();
        var loanId = await DisbursedLoanAsync(fixture);

        var result = await fixture.Services.Loans.RecordManualRepaymentAsync(
            loanId, 700m, new DateOnly(2026, 9, 30), "CASH-001", isEarlySettlement: false);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Message.Contains("exceeds the outstanding balance"));
    }

    /// <summary>
    /// Over-recovery is possible, but only as a deliberate, attributed act with a reason. Taking
    /// more than is owed by default would be taking money that is not the employer's.
    /// </summary>
    [Fact]
    public async Task Over_recovery_is_allowed_only_once_a_named_person_has_approved_it()
    {
        using var fixture = await SetUpAsync();
        var loanId = await DisbursedLoanAsync(fixture);

        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        Assert.False((await approver.Loans.ApproveOverRecoveryAsync(loanId, string.Empty)).IsValid);
        Assert.True((await approver.Loans
            .ApproveOverRecoveryAsync(loanId, "Final settlement agreed on termination.")).IsValid);
        fixture.Db.Context.ChangeTracker.Clear();

        var result = await fixture.Services.Loans.RecordManualRepaymentAsync(
            loanId, 700m, new DateOnly(2026, 9, 30), "CASH-001", isEarlySettlement: false);

        Assert.True(result.Succeeded);

        var loan = await LoadAsync(fixture, loanId);
        Assert.Equal("u-manager", loan.OverRecoveryApprovedBy);
        Assert.Null(loan.MaximumDeduction);
    }

    [Fact]
    public async Task Early_settlement_clears_the_remaining_schedule()
    {
        using var fixture = await SetUpAsync();
        var loanId = await DisbursedLoanAsync(fixture);

        await fixture.Services.Loans.RecordManualRepaymentAsync(
            loanId, 600m, new DateOnly(2026, 9, 30), "CASH-SETTLE", isEarlySettlement: true);
        fixture.Db.Context.ChangeTracker.Clear();

        var loan = await LoadAsync(fixture, loanId);

        Assert.True(loan.IsSettled);
        Assert.Equal(LoanStatus.Settled, loan.Status);
        Assert.All(loan.Instalments,
            i => Assert.NotEqual(LoanInstalmentStatus.Scheduled, i.Status));
    }

    [Fact]
    public async Task A_settled_loan_produces_no_further_deduction()
    {
        using var fixture = await SetUpAsync();
        var loanId = await DisbursedLoanAsync(fixture);
        await fixture.Services.Loans.RecordManualRepaymentAsync(
            loanId, 600m, new DateOnly(2026, 9, 1), "CASH-SETTLE", isEarlySettlement: true);
        fixture.Db.Context.ChangeTracker.Clear();

        var due = await fixture.Services.Loans
            .GetDueDeductionsAsync(fixture.Employee.Id, fixture.Period.Id, fixture.Period.EndDate);

        Assert.Empty(due);
    }

    // ---- Reversal -------------------------------------------------------------------------------

    [Fact]
    public async Task Reversing_a_repayment_restores_the_balance_and_keeps_both_rows()
    {
        using var fixture = await SetUpAsync();
        var loanId = await DisbursedLoanAsync(fixture);
        var repayment = (await fixture.Services.Loans.RecordManualRepaymentAsync(
            loanId, 100m, new DateOnly(2026, 9, 30), "CASH-001", isEarlySettlement: false)).Value!;
        fixture.Db.Context.ChangeTracker.Clear();

        Assert.True((await fixture.Services.Loans
            .ReverseAsync(repayment.Id, "Posted against the wrong employee.")).IsValid);
        fixture.Db.Context.ChangeTracker.Clear();

        var loan = await LoadAsync(fixture, loanId);

        Assert.Equal(600m, loan.Outstanding.Amount);

        // The employee is entitled to see both what was taken and what was given back.
        var original = loan.Transactions.Single(t => t.Id == repayment.Id);
        Assert.True(original.IsReversed);
        Assert.Equal("Posted against the wrong employee.", original.ReversalReason);
        Assert.Contains(loan.Transactions, t => t.TransactionType == LoanTransactionType.Reversal);
    }

    [Fact]
    public async Task A_reversal_needs_a_reason_and_cannot_be_applied_twice()
    {
        using var fixture = await SetUpAsync();
        var loanId = await DisbursedLoanAsync(fixture);
        var repayment = (await fixture.Services.Loans.RecordManualRepaymentAsync(
            loanId, 100m, new DateOnly(2026, 9, 30), "CASH-001", isEarlySettlement: false)).Value!;
        fixture.Db.Context.ChangeTracker.Clear();

        Assert.False((await fixture.Services.Loans.ReverseAsync(repayment.Id, string.Empty)).IsValid);
        Assert.True((await fixture.Services.Loans.ReverseAsync(repayment.Id, "Wrong employee")).IsValid);
        fixture.Db.Context.ChangeTracker.Clear();

        var second = await fixture.Services.Loans.ReverseAsync(repayment.Id, "Again");
        Assert.False(second.IsValid);
    }

    // ---- Payroll integration ----------------------------------------------------------------------

    [Fact]
    public async Task An_approved_loan_reaches_payroll_as_a_deduction_naming_its_balance()
    {
        using var fixture = await SetUpAsync();
        var loanId = await DisbursedLoanAsync(fixture);

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        var loanInput = Assert.Single(snapshot!.LoanDeductions);
        Assert.Equal(loanId, loanInput.LoanId);
        Assert.Equal(100m, loanInput.Amount.Amount);
        Assert.Equal(600m, loanInput.OutstandingBefore.Amount);

        var result = new Tawaka.Payroll.Engine.PayrollCalculator().Calculate(snapshot);
        var deduction = result.Deductions.Single(d => d.Code == "LOAN");
        Assert.Equal(100m, deduction.Amount.Amount);

        // A loan repayment is not a tax-deductible expense; it comes out after tax.
        Assert.False(deduction.ReducesTaxableIncome);
    }

    /// <summary>
    /// A USD loan is repaid in USD. Recovering it from a ZiG payroll would need a converted amount,
    /// and the conversion methodology is compliance question Q1, which is still open.
    /// </summary>
    [Fact]
    public async Task A_loan_in_another_currency_is_not_deducted_from_this_payroll()
    {
        using var fixture = await SetUpAsync(currency: "ZWG");
        await DisbursedLoanAsync(fixture, currency: "USD");

        var snapshot = await fixture.Services.Snapshots
            .BuildAsync(fixture.Employee.Id, fixture.Period, PayrollMode.Development);

        Assert.Empty(snapshot!.LoanDeductions);
    }

    [Fact]
    public async Task Calculating_a_run_does_not_move_the_loan_balance()
    {
        using var fixture = await SetUpAsync();
        var loanId = await DisbursedLoanAsync(fixture);

        var run = (await fixture.Services.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Services.Runs.CalculateAsync(run.Id);
        fixture.Db.Context.ChangeTracker.Clear();

        // A calculated run can still be recalculated or abandoned. Moving the balance now would
        // leave the employee's loan wrong with nothing to point at.
        var loan = await LoadAsync(fixture, loanId);
        Assert.Equal(600m, loan.Outstanding.Amount);
        Assert.DoesNotContain(loan.Transactions,
            t => t.TransactionType == LoanTransactionType.ScheduledRepayment);
    }

    private static async Task<Guid> ApprovedLoanAsync(Fixture fixture, LoanCommand? command = null)
    {
        var loan = (await fixture.Services.Loans.CreateAsync(command ?? Command(fixture))).Value!;
        await fixture.Services.Loans.SubmitAsync(loan.Id);

        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Manager"));
        var approval = await approver.Loans.ApproveAsync(loan.Id);
        Assert.True(approval.IsValid, approval.ToString());

        fixture.Db.Context.ChangeTracker.Clear();
        return loan.Id;
    }

    private static async Task<Guid> DisbursedLoanAsync(
        Fixture fixture, decimal principal = 600m, int instalments = 6, string currency = "USD")
    {
        var loanId = await ApprovedLoanAsync(fixture,
            Command(fixture, principal, instalments, currency: currency));

        var result = await fixture.Services.Loans
            .DisburseAsync(loanId, new DateOnly(2026, 8, 31), "FBC-RTGS-77012");
        Assert.True(result.IsValid, result.ToString());

        fixture.Db.Context.ChangeTracker.Clear();
        return loanId;
    }

    private static Task<EmployeeLoan> LoadAsync(Fixture fixture, Guid loanId) =>
        fixture.Db.Context.EmployeeLoans.AsNoTracking()
            .Include(l => l.Instalments)
            .Include(l => l.Transactions)
            .SingleAsync(l => l.Id == loanId);
}
