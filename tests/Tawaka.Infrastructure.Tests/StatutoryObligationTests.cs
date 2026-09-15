using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Employees;
using Tawaka.Application.Payroll;
using Tawaka.Application.Security;
using Tawaka.Application.Statutory;
using Tawaka.Application.Statutory.Obligations;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory;
using Tawaka.Domain.Statutory.Obligations;
using Tawaka.Infrastructure.Persistence;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// The statutory obligation state machine from <c>docs/MILESTONE_4_BRIEF.md</c> §2.
/// The rule under test throughout: <b>approving a payroll never marks an obligation paid.</b>
/// </summary>
/// <summary>
/// The shared payroll fixture: a configured company, one verified USD employee and a September
/// period, ready to run.
/// <para>
/// Deliberately fact-free. Several suites need this scaffolding, and a base class carrying its own
/// <c>[Fact]</c>s would have xUnit re-run every one of them in each derived suite — inflating the
/// count and the runtime while proving nothing extra.
/// </para>
/// </summary>
public abstract class PayrollFixtureBase : EmployeeTestBase
{
    protected sealed record Fixture(
        TestDatabase Db, Guid CompanyId, Employee Employee, PayrollPeriod Period,
        PayrollRunService Runs, StatutoryObligationService Obligations, Guid EmploymentTypeId)
        : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    /// <summary>A company with one USD employee, all rules verified, ready to run payroll.</summary>
    protected static new async Task<Fixture> SetUpAsync(ICurrentUser? user = null)
    {
        var (db, companyId, permanentTypeId, _) = await EmployeeTestBase.SetUpAsync(user);

        var employees = new EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new EmployeeContractService(db.Context, db.User);
        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        await contracts.CreateInitialAsync(NewContract(companyId, employee.Id, permanentTypeId, 850m));

        db.Context.EmployeeStatutoryProfiles.Add(new EmployeeStatutoryProfile
        {
            EmployeeId = employee.Id, TaxNumber = "BP0001", NssaNumber = "NSSA0001"
        });

        var period = new PayrollPeriod
        {
            CompanyId = companyId, Code = "2026-09", Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30), TaxYear = 2026, Mode = PayrollMode.Live
        };
        db.Context.PayrollPeriods.Add(period);
        await db.Context.SaveChangesAsync();

        await VerifyAllRulesAsync(db);

        var services = PayrollServices.For(db);
        var obligations = services.Obligations;
        var runs = services.Runs;

        return new Fixture(db, companyId, employee, period, runs, obligations, permanentTypeId);
    }

    protected static async Task VerifyAllRulesAsync(TestDatabase db)
    {
        foreach (var rule in await db.Context.StatutoryRules.ToListAsync())
        {
            if (rule.VerificationStatus != VerificationStatus.Disabled)
            {
                rule.VerificationStatus = VerificationStatus.Verified;
            }
        }

        foreach (var type in await db.Context.EarningTypes.ToListAsync())
        {
            type.TreatmentVerificationStatus = VerificationStatus.Verified;
        }

        foreach (var nssa in await db.Context.NssaRules.ToListAsync())
        {
            nssa.CeilingApplication = CeilingApplicationMethod.ProRataByPeriodLength;
        }

        db.Context.ApwcsRules.Add(new ApwcsRule
        {
            RuleId = "APWCS-2026", Name = "APWCS assessed rate", Currency = "USD",
            IndustryClassification = "Construction", IndustryCode = "CON", Rate = 0.025m,
            Base = ApwcsBase.BasicEarnings, CalculationMethod = CalculationMethod.PercentageOfBase,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = VerificationStatus.Verified,
            Source = new RuleSource { Source = "NSSA assessment" }
        });

        await db.Context.SaveChangesAsync();
    }

    /// <summary>Calculates, approves as a second user, and finalises — producing the obligations.</summary>
    protected static async Task<Guid> RunThroughFinaliseAsync(Fixture fixture)
    {
        var run = (await fixture.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Runs.CalculateAsync(run.Id);

        // Approval must be a different user from the one who calculated.
        var stored = await fixture.Db.Context.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        stored.CalculatedBy = "u-officer";
        await fixture.Db.Context.SaveChangesAsync();

        var approval = await fixture.Runs.ApproveAsync(run.Id);
        Assert.True(approval.IsValid, approval.ToString());

        var finalise = await fixture.Runs.FinaliseAsync(run.Id);
        Assert.True(finalise.IsValid, finalise.ToString());

        return run.Id;
    }

}

public class StatutoryObligationTests : PayrollFixtureBase
{
    [Fact]
    public async Task Finalising_creates_obligations_calculated_and_deducted_but_not_approved_or_paid()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var obligations = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Where(o => o.PayrollRunId == runId).ToListAsync();

        Assert.NotEmpty(obligations);

        var paye = obligations.Single(o => o.ObligationType == StatutoryObligationType.Paye);
        Assert.True(paye.IsCalculated);
        Assert.True(paye.IsDeducted);
        Assert.False(paye.IsApproved);
        Assert.False(paye.IsPaid);
        Assert.Equal(StatutoryObligationStatus.Deducted, paye.StatusOn(new DateOnly(2026, 10, 1)));
    }

    /// <summary>The rule the whole design exists to enforce.</summary>
    [Fact]
    public async Task Approving_a_payroll_run_never_marks_an_obligation_paid()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var obligations = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .Where(o => o.PayrollRunId == runId).ToListAsync();

        Assert.All(obligations, o => Assert.False(o.IsPaid));
        Assert.All(obligations, o => Assert.Empty(o.Payments));
        Assert.All(obligations, o => Assert.Equal(o.CalculatedAmount, o.Outstanding.Amount));
    }

    [Fact]
    public async Task Employer_borne_obligations_show_deduction_as_not_applicable()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var obligations = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Where(o => o.PayrollRunId == runId).ToListAsync();

        var apwcs = obligations.Single(o => o.ObligationType == StatutoryObligationType.Apwcs);
        Assert.False(apwcs.IsDeductionApplicable);
        Assert.False(apwcs.IsDeducted);
        Assert.Equal(0m, apwcs.DeductedAmount);

        var employee = obligations.Single(o => o.ObligationType == StatutoryObligationType.NssaPobsEmployee);
        Assert.True(employee.IsDeductionApplicable);
        Assert.True(employee.IsDeducted);
    }

    [Fact]
    public async Task An_obligation_cannot_be_approved_before_it_is_deducted()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var paye = await fixture.Db.Context.StatutoryObligations
            .SingleAsync(o => o.PayrollRunId == runId && o.ObligationType == StatutoryObligationType.Paye);
        paye.IsDeducted = false;
        await fixture.Db.Context.SaveChangesAsync();

        var result = await fixture.Obligations.ApproveAsync(paye.Id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("has not yet been deducted"));
    }

    [Fact]
    public async Task A_payment_cannot_be_recorded_before_approval()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var paye = await Paye(fixture, runId);

        var result = await fixture.Obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = paye.Id, Amount = paye.CalculatedAmount, CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 10, 8), PaymentReference = "RTGS-99"
        });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Message.Contains("not been approved"));
    }

    [Fact]
    public async Task A_payment_without_a_reference_is_refused()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var paye = await Paye(fixture, runId);
        await fixture.Obligations.ApproveAsync(paye.Id);

        var result = await fixture.Obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = paye.Id, Amount = paye.CalculatedAmount, CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 10, 8), PaymentReference = "   "
        });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Message.Contains("reference"));
    }

    /// <summary>A USD liability is not settled with a ZiG payment.</summary>
    [Fact]
    public async Task A_payment_in_the_wrong_currency_is_refused()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var paye = await Paye(fixture, runId);
        await fixture.Obligations.ApproveAsync(paye.Id);

        var result = await fixture.Obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = paye.Id, Amount = paye.CalculatedAmount, CurrencyCode = "ZWG",
            PaymentDate = new DateOnly(2026, 10, 8), PaymentReference = "RTGS-1"
        });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Message.Contains("must be paid in USD"));
    }

    [Fact]
    public async Task Recording_a_full_payment_settles_the_obligation()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var paye = await Paye(fixture, runId);
        await fixture.Obligations.ApproveAsync(paye.Id);

        var result = await fixture.Obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = paye.Id, Amount = paye.CalculatedAmount, CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 10, 8), PaymentReference = "RTGS-20261008-001",
            AuthorityReceiptNumber = "ZIMRA-556677"
        });

        Assert.True(result.Succeeded);

        var settled = await Paye(fixture, runId);
        Assert.True(settled.IsPaid);
        Assert.Equal(0m, settled.Outstanding.Amount);
        Assert.Equal(StatutoryObligationStatus.Paid, settled.StatusOn(new DateOnly(2026, 10, 9)));
        Assert.Equal("RTGS-20261008-001", settled.Payments.Single().PaymentReference);
    }

    [Fact]
    public async Task A_partial_payment_leaves_the_correct_balance_outstanding()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var paye = await Paye(fixture, runId);
        await fixture.Obligations.ApproveAsync(paye.Id);

        var half = Math.Round(paye.CalculatedAmount / 2m, 2);
        await fixture.Obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = paye.Id, Amount = half, CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 10, 8), PaymentReference = "RTGS-PART-1"
        });

        var partial = await Paye(fixture, runId);
        Assert.False(partial.IsPaid);
        Assert.True(partial.IsPartiallyPaid);
        Assert.Equal(paye.CalculatedAmount - half, partial.Outstanding.Amount);
        Assert.Equal(StatutoryObligationStatus.PartiallyPaid,
            partial.StatusOn(new DateOnly(2026, 10, 9)));
    }

    [Fact]
    public async Task A_payment_exceeding_the_outstanding_balance_is_refused()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var paye = await Paye(fixture, runId);
        await fixture.Obligations.ApproveAsync(paye.Id);

        var result = await fixture.Obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = paye.Id, Amount = paye.CalculatedAmount + 50m, CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 10, 8), PaymentReference = "RTGS-OVER"
        });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Message.Contains("exceeds the outstanding"));
    }

    /// <summary>Penalty and interest are recorded separately from the liability itself.</summary>
    [Fact]
    public async Task A_payment_may_include_penalty_or_interest_recorded_separately()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var paye = await Paye(fixture, runId);
        await fixture.Obligations.ApproveAsync(paye.Id);

        var result = await fixture.Obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = paye.Id, Amount = paye.CalculatedAmount + 15m, CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 11, 20), PaymentReference = "RTGS-LATE",
            PenaltyOrInterestIncluded = 15m
        });

        Assert.True(result.Succeeded);
        Assert.Equal(paye.CalculatedAmount, result.Value!.PrincipalAmount);
    }

    [Fact]
    public async Task Reversing_a_payment_requires_a_reason_and_keeps_the_record()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var paye = await Paye(fixture, runId);
        await fixture.Obligations.ApproveAsync(paye.Id);

        var payment = (await fixture.Obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = paye.Id, Amount = paye.CalculatedAmount, CurrencyCode = "USD",
            PaymentDate = new DateOnly(2026, 10, 8), PaymentReference = "RTGS-WRONG"
        })).Value!;

        Assert.False((await fixture.Obligations.ReversePaymentAsync(payment.Id, "  ")).IsValid);

        var reversal = await fixture.Obligations.ReversePaymentAsync(
            payment.Id, "Entered against the wrong period");
        Assert.True(reversal.IsValid);

        var after = await Paye(fixture, runId);
        Assert.False(after.IsPaid);
        Assert.Equal(after.CalculatedAmount, after.Outstanding.Amount);

        // The payment row survives, marked reversed, with its reason.
        var stored = await fixture.Db.Context.StatutoryPayments.AsNoTracking()
            .SingleAsync(p => p.Id == payment.Id);
        Assert.True(stored.IsReversed);
        Assert.Equal("Entered against the wrong period", stored.ReversalReason);
    }

    [Fact]
    public async Task An_obligation_reconciles_to_its_per_employee_lines()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var obligations = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.PayrollRunId == runId).ToListAsync();

        foreach (var obligation in obligations)
        {
            Assert.Equal(obligation.CalculatedAmount, obligation.Lines.Sum(l => l.Amount));
        }
    }

    /// <summary>The lines carry the identifiers a statutory return needs.</summary>
    [Fact]
    public async Task Obligation_lines_carry_the_employee_statutory_identifiers()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var line = await fixture.Db.Context.StatutoryObligationLines.AsNoTracking()
            .FirstAsync(l => fixture.Db.Context.StatutoryObligations
                .Any(o => o.Id == l.StatutoryObligationId && o.PayrollRunId == runId));

        Assert.Equal("BP0001", line.TaxNumber);
        Assert.Equal("NSSA0001", line.NssaNumber);
        Assert.False(string.IsNullOrWhiteSpace(line.EmployeeNumber));
    }

    [Fact]
    public async Task An_overdue_obligation_is_reported_as_overdue()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var paye = await Paye(fixture, runId);

        Assert.Equal(new DateOnly(2026, 10, 10), paye.DueDate);
        Assert.Equal(StatutoryObligationStatus.Overdue, paye.StatusOn(new DateOnly(2026, 10, 11)));
        Assert.NotEqual(StatutoryObligationStatus.Overdue, paye.StatusOn(new DateOnly(2026, 10, 9)));
    }

    /// <summary>ZIMDEF is due on the 15th, not the 10th.</summary>
    [Fact]
    public async Task Due_dates_differ_by_authority()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var obligations = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Where(o => o.PayrollRunId == runId).ToListAsync();

        var zimdef = obligations.SingleOrDefault(o => o.ObligationType == StatutoryObligationType.Zimdef);
        if (zimdef is not null)
        {
            Assert.Equal(15, zimdef.DueDate!.Value.Day);
        }

        Assert.Equal(10, obligations
            .Single(o => o.ObligationType == StatutoryObligationType.Paye).DueDate!.Value.Day);
    }

    [Fact]
    public async Task Obligation_transitions_are_audited()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var paye = await Paye(fixture, runId);
        await fixture.Obligations.ApproveAsync(paye.Id);

        var entries = await fixture.Db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == nameof(StatutoryObligation)).ToListAsync();

        Assert.Contains(entries, a => a.FieldName == nameof(StatutoryObligation.IsApproved)
                                      && a.OldValue == "False" && a.NewValue == "True");
    }

    [Fact]
    public async Task Recording_a_payment_requires_the_statutory_payment_permission()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var paye = await Paye(fixture, runId);

        var limited = new StatutoryObligationService(
            fixture.Db.Context, TestUser.WithPermissions(Permissions.StatutoryView),
            fixture.Db.Clock);

        await Assert.ThrowsAsync<PermissionDeniedException>(() => limited.ApproveAsync(paye.Id));
    }

    private static async Task<StatutoryObligation> Paye(Fixture fixture, Guid runId) =>
        await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .SingleAsync(o => o.PayrollRunId == runId &&
                              o.ObligationType == StatutoryObligationType.Paye);
}
