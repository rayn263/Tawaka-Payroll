using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Statutory.Obligations;
using Tawaka.Domain.Statutory.Obligations;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// The statutory obligation from calculation to settlement, in one continuous sequence, with the
/// outstanding balance checked after every single operation.
/// <para>
/// The individual transitions are tested in <see cref="StatutoryObligationTests"/>. What this adds
/// is the sequence: a part payment, a second part payment, settlement, and then a reversal that
/// has to put the money back. An obligation that forgets it was settled, or that stays settled
/// after its payment is reversed, is the failure that gets an employer penalised.
/// </para>
/// </summary>
public class QaObligationLifecycleTests : PayrollFixtureBase
{
    private static async Task<StatutoryObligation> ReloadAsync(Fixture fixture, Guid id)
    {
        fixture.Db.Context.ChangeTracker.Clear();
        return await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .SingleAsync(o => o.Id == id);
    }

    private static RecordPaymentRequest Payment(
        Guid obligationId, decimal amount, string reference, int day) => new()
        {
            ObligationId = obligationId,
            Amount = amount,
            CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 10, day),
            PaymentReference = reference,
            AuthorityReceiptNumber = $"ZIMRA-{reference}"
        };

    [Fact]
    public async Task The_whole_obligation_lifecycle_keeps_the_balance_right_at_every_step()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var paye = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .SingleAsync(o => o.PayrollRunId == runId &&
                              o.ObligationType == StatutoryObligationType.Paye);

        var total = paye.CalculatedAmount;
        Assert.True(total > 0m);

        // --- Calculated and deducted, but owed in full ------------------------------------------
        Assert.True(paye.IsCalculated);
        Assert.True(paye.IsDeducted);
        Assert.False(paye.IsApproved);
        Assert.False(paye.IsPaid);
        Assert.Equal(0m, paye.PaidAmount.Amount);
        Assert.Equal(total, paye.Outstanding.Amount);

        // --- Approved: still owed in full -------------------------------------------------------
        Assert.True((await fixture.Obligations.ApproveAsync(paye.Id)).IsValid);
        var approved = await ReloadAsync(fixture, paye.Id);
        Assert.True(approved.IsApproved);
        Assert.False(approved.IsPaid);
        Assert.Equal(total, approved.Outstanding.Amount);

        // --- First part payment -----------------------------------------------------------------
        var first = decimal.Round(total / 4m, 2);
        var firstPayment = await fixture.Obligations.RecordPaymentAsync(
            Payment(paye.Id, first, "RTGS-001", 5));
        Assert.True(firstPayment.Succeeded, firstPayment.Validation.ToString());

        var afterFirst = await ReloadAsync(fixture, paye.Id);
        Assert.False(afterFirst.IsPaid);
        Assert.Equal(first, afterFirst.PaidAmount.Amount);
        Assert.Equal(total - first, afterFirst.Outstanding.Amount);

        // --- Second part payment ----------------------------------------------------------------
        var second = decimal.Round(total / 4m, 2);
        Assert.True((await fixture.Obligations.RecordPaymentAsync(
            Payment(paye.Id, second, "RTGS-002", 6))).Succeeded);

        var afterSecond = await ReloadAsync(fixture, paye.Id);
        Assert.False(afterSecond.IsPaid);
        Assert.Equal(first + second, afterSecond.PaidAmount.Amount);
        Assert.Equal(total - first - second, afterSecond.Outstanding.Amount);

        // --- Anything beyond the balance is refused ----------------------------------------------
        var overpayment = await fixture.Obligations.RecordPaymentAsync(
            Payment(paye.Id, afterSecond.Outstanding.Amount + 0.01m, "RTGS-BAD", 7));
        Assert.False(overpayment.Succeeded);

        var unchanged = await ReloadAsync(fixture, paye.Id);
        Assert.Equal(first + second, unchanged.PaidAmount.Amount);

        // --- Settlement ---------------------------------------------------------------------------
        var balance = unchanged.Outstanding.Amount;
        Assert.True((await fixture.Obligations.RecordPaymentAsync(
            Payment(paye.Id, balance, "RTGS-003", 8))).Succeeded);

        var settled = await ReloadAsync(fixture, paye.Id);
        Assert.True(settled.IsPaid);
        Assert.Equal(total, settled.PaidAmount.Amount);
        Assert.Equal(0m, settled.Outstanding.Amount);
        Assert.Equal(3, settled.Payments.Count);

        // --- Reversal: the money comes back and the obligation stops being paid -------------------
        var toReverse = settled.Payments.Single(p => p.PaymentReference == "RTGS-003");
        var reversal = await fixture.Obligations.ReversePaymentAsync(
            toReverse.Id, "Bank returned the transfer unpaid.");
        Assert.True(reversal.IsValid, reversal.ToString());

        var afterReversal = await ReloadAsync(fixture, paye.Id);
        Assert.False(afterReversal.IsPaid);
        Assert.Equal(first + second, afterReversal.PaidAmount.Amount);
        Assert.Equal(balance, afterReversal.Outstanding.Amount);

        // The reversed payment is still on the record — an audit trail does not forget.
        Assert.Equal(3, afterReversal.Payments.Count);
        var reversed = afterReversal.Payments.Single(p => p.PaymentReference == "RTGS-003");
        Assert.True(reversed.IsReversed);
        Assert.Equal("Bank returned the transfer unpaid.", reversed.ReversalReason);

        // --- Settling again after the reversal ------------------------------------------------------
        Assert.True((await fixture.Obligations.RecordPaymentAsync(
            Payment(paye.Id, balance, "RTGS-004", 12))).Succeeded);

        var resettled = await ReloadAsync(fixture, paye.Id);
        Assert.True(resettled.IsPaid);
        Assert.Equal(0m, resettled.Outstanding.Amount);
        Assert.Equal(4, resettled.Payments.Count);
    }

    /// <summary>
    /// An employer-borne obligation is a cost to the employer, never a deduction from the
    /// employee. Turning one into a deduction would take money from someone who does not owe it.
    /// </summary>
    [Fact]
    public async Task An_employer_borne_obligation_never_becomes_an_employee_deduction()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var employerBorne = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.PayrollRunId == runId && !o.IsDeductionApplicable)
            .ToListAsync();

        Assert.NotEmpty(employerBorne);

        var runEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.DeductionLines)
            .SingleAsync(e => e.PayrollRunId == runId);

        foreach (var obligation in employerBorne)
        {
            Assert.Equal(0m, obligation.DeductedAmount);
            Assert.False(obligation.IsDeducted);
            Assert.DoesNotContain(
                runEmployee.DeductionLines,
                line => line.Amount == obligation.CalculatedAmount &&
                        line.Code.Contains("ER", StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Paid means paid: the flag is derived from unreversed payments, so it cannot be set by hand,
    /// by approving the payroll, or by marking the employees' wages paid.
    /// </summary>
    [Fact]
    public async Task Paid_is_derived_only_from_unreversed_payments()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var obligation = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .FirstAsync(o => o.PayrollRunId == runId);

        Assert.False(obligation.IsPaid);

        Assert.True((await fixture.Obligations.ApproveAsync(obligation.Id)).IsValid);
        Assert.True((await fixture.Runs.MarkNetWagesPaidAsync(runId)).IsValid);

        var afterWages = await ReloadAsync(fixture, obligation.Id);
        Assert.False(afterWages.IsPaid);
        Assert.Equal(0m, afterWages.PaidAmount.Amount);

        Assert.True((await fixture.Obligations.RecordPaymentAsync(
            Payment(obligation.Id, obligation.CalculatedAmount, "RTGS-010", 9))).Succeeded);

        var afterPayment = await ReloadAsync(fixture, obligation.Id);
        Assert.True(afterPayment.IsPaid);

        // Reversing the only payment takes it straight back to unpaid.
        Assert.True((await fixture.Obligations.ReversePaymentAsync(
            afterPayment.Payments.Single().Id, "Duplicate capture.")).IsValid);

        var afterReversal = await ReloadAsync(fixture, obligation.Id);
        Assert.False(afterReversal.IsPaid);
        Assert.Equal(0m, afterReversal.PaidAmount.Amount);
        Assert.Equal(obligation.CalculatedAmount, afterReversal.Outstanding.Amount);
    }
}
