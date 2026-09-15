using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Employees;
using Tawaka.Application.Payslips;
using Tawaka.Application.Reports;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory.Obligations;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// The payslip renders persisted results. It never recalculates, and its lines must reconcile
/// exactly to the totals the engine stored.
/// </summary>
public class PayslipTests : PayrollFixtureBase
{
    private static PayslipBuilder Builder(Fixture fixture) =>
        new(fixture.Db.Context, fixture.Db.User, fixture.Db.Clock);

    private static Task<Guid> RunEmployeeIdAsync(Fixture fixture, Guid runId) =>
        fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Where(e => e.PayrollRunId == runId).Select(e => e.Id).FirstAsync();

    [Fact]
    public async Task A_payslip_renders_the_stored_figures()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var runEmployeeId = await RunEmployeeIdAsync(fixture, runId);

        var payslip = await Builder(fixture).BuildAsync(runEmployeeId);

        Assert.NotNull(payslip);
        Assert.Equal("John Moyo", payslip!.Employee.Name);
        Assert.Equal("USD", payslip.Currency.Value);
        Assert.Equal(850m, payslip.GrossEarnings.Value!.Value.Amount);

        var stored = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.Id == runEmployeeId);
        Assert.Equal(stored.NetPayAmount, payslip.NetPay.Value!.Value.Amount);
        Assert.Equal(stored.TotalDeductionsAmount, payslip.TotalDeductions.Value!.Value.Amount);
    }

    /// <summary>The guard against the document quietly diverging from the payroll.</summary>
    [Fact]
    public async Task Payslip_lines_reconcile_to_the_stored_totals()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var runEmployeeId = await RunEmployeeIdAsync(fixture, runId);

        var payslip = await Builder(fixture).BuildAsync(runEmployeeId);
        var stored = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.EarningLines)
            .SingleAsync(e => e.Id == runEmployeeId);

        var grossLines = stored.EarningLines.Where(l => l.IsIncludedInGross).Sum(l => l.Amount);
        Assert.Equal(grossLines, payslip!.GrossEarnings.Value!.Value.Amount);
        Assert.Equal(payslip.DeductionLineTotal, payslip.TotalDeductions.Value!.Value.Amount);

        // Gross less deductions equals net, on the rendered document.
        Assert.Equal(payslip.GrossEarnings.Value!.Value.Amount - payslip.TotalDeductions.Value!.Value.Amount,
            payslip.NetPay.Value!.Value.Amount);
    }

    /// <summary>Configured statutory categories stay visible at zero.</summary>
    [Fact]
    public async Task Statutory_categories_appear_even_when_zero()
    {
        using var fixture = await SetUpAsync();

        // A salary below the tax threshold: PAYE and the levy are legitimately zero.
        var contracts = new EmployeeContractService(fixture.Db.Context, fixture.Db.User);
        await contracts.SupersedeAsync(fixture.Employee.Id,
            NewContract(fixture.CompanyId, fixture.Employee.Id, fixture.EmploymentTypeId, 90m),
            new DateOnly(2026, 9, 1), "Reduced hours");

        var runId = await RunThroughFinaliseAsync(fixture);
        var runEmployeeId = await RunEmployeeIdAsync(fixture, runId);
        var payslip = await Builder(fixture).BuildAsync(runEmployeeId);

        var paye = payslip!.Deductions.Single(l => l.Code == "PAYE");
        Assert.Equal(PayslipValueState.Zero, paye.Amount.State);
        Assert.Equal("0.00", paye.Amount.Display());

        Assert.Contains(payslip.Deductions, l => l.Code == "AIDSLEVY");
        Assert.Contains(payslip.Deductions, l => l.Code == "NSSA_EE");
    }

    /// <summary>Zero and unresolved must never be confused.</summary>
    [Fact]
    public void A_zero_and_an_unresolved_figure_render_differently()
    {
        var zero = PayslipAmount.From(0m, Domain.Common.CurrencyCode.Usd);
        var unresolved = PayslipAmount.From(null, Domain.Common.CurrencyCode.Usd,
            "The rule is unverified.");

        Assert.Equal(PayslipValueState.Zero, zero.State);
        Assert.Equal("0.00", zero.Display());

        Assert.Equal(PayslipValueState.Unresolved, unresolved.State);
        Assert.Equal("—", unresolved.Display());
        Assert.Null(unresolved.Value);
    }

    [Fact]
    public async Task A_development_payslip_is_marked_as_such()
    {
        using var fixture = await SetUpAsync();
        var period = await fixture.Db.Context.PayrollPeriods.SingleAsync();
        period.Mode = PayrollMode.Development;
        await fixture.Db.Context.SaveChangesAsync();

        var run = (await fixture.Runs.CreateRunAsync(period.Id)).Value!;
        await fixture.Runs.CalculateAsync(run.Id);
        var runEmployeeId = await RunEmployeeIdAsync(fixture, run.Id);

        var payslip = await Builder(fixture).BuildAsync(runEmployeeId);

        Assert.True(payslip!.IsDevelopmentCopy);
    }

    [Fact]
    public async Task A_payslip_cannot_be_issued_before_the_run_is_finalised()
    {
        using var fixture = await SetUpAsync();
        var run = (await fixture.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Runs.CalculateAsync(run.Id);
        var runEmployeeId = await RunEmployeeIdAsync(fixture, run.Id);

        var result = await Builder(fixture).GenerateAsync(runEmployeeId);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Message.Contains("finalised run"));
    }

    [Fact]
    public async Task A_payslip_is_numbered_and_re_issuing_creates_a_new_revision()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var runEmployeeId = await RunEmployeeIdAsync(fixture, runId);
        var builder = Builder(fixture);

        var first = (await builder.GenerateAsync(runEmployeeId)).Value!;
        Assert.StartsWith("PS-2026-09-", first.PayslipNumber, StringComparison.Ordinal);
        Assert.Equal(1, first.Revision);

        // Re-issuing without a reason is refused.
        Assert.False((await builder.GenerateAsync(runEmployeeId)).Succeeded);

        var second = (await builder.GenerateAsync(runEmployeeId, "Overtime corrected")).Value!;
        Assert.Equal(2, second.Revision);
        Assert.Equal(first.PayslipNumber, second.PayslipNumber);

        // The superseded revision survives.
        var original = await fixture.Db.Context.Payslips.AsNoTracking()
            .SingleAsync(p => p.Id == first.Id);
        Assert.Equal(second.Id, original.SupersededByPayslipId);
        Assert.True(original.IsSuperseded);
        Assert.Equal(2, await fixture.Db.Context.Payslips.CountAsync());
    }

    /// <summary>The payslip reports remittance status without asserting unrecorded payment.</summary>
    [Fact]
    public async Task The_statutory_block_shows_deducted_but_not_remitted()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var runEmployeeId = await RunEmployeeIdAsync(fixture, runId);

        var payslip = await Builder(fixture).BuildAsync(runEmployeeId);
        var paye = payslip!.StatutoryStatus.Single(s => s.Name == "PAYE");

        Assert.True(paye.IsDeducted);
        Assert.False(paye.IsApproved);
        Assert.False(paye.IsRemitted);

        var apwcs = payslip.StatutoryStatus.Single(s => s.Name == "APWCS");
        Assert.False(apwcs.DeductionApplicable);
    }

    [Fact]
    public async Task Employer_contributions_appear_separately_from_deductions()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);
        var runEmployeeId = await RunEmployeeIdAsync(fixture, runId);

        var payslip = await Builder(fixture).BuildAsync(runEmployeeId);

        Assert.Contains(payslip!.EmployerContributions, l => l.Code == "NSSA_POBS_ER");
        Assert.DoesNotContain(payslip.Deductions, l => l.Code == "NSSA_POBS_ER");
        Assert.DoesNotContain(payslip.Deductions, l => l.Code == "APWCS");
    }
}

/// <summary>Reports read persisted results, reconcile, and never sum across currencies.</summary>
public class PayrollReportTests : PayrollFixtureBase
{
    private static PayrollReportService Reports(Fixture fixture) =>
        PayrollServices.For(fixture.Db).Reports;

    [Fact]
    public async Task The_summary_reconciles_to_the_register()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var reports = await Reports(fixture).BuildAsync(runId);

        var summary = reports!.Summary.Single(s => s.CurrencyCode == "USD").Totals;
        var register = reports.Register.Single(s => s.CurrencyCode == "USD").Totals;

        Assert.Equal(summary.Gross, register.Gross);
        Assert.Equal(summary.Paye, register.Paye);
        Assert.Equal(summary.NetPay, register.NetPay);
        Assert.Equal(summary.EmployerCost, register.EmployerCost);
    }

    /// <summary>Gross plus employer contributions must equal total employer cost.</summary>
    [Fact]
    public async Task The_employer_cost_report_reconciles()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var reports = await Reports(fixture).BuildAsync(runId);
        var section = reports!.EmployerCosts.Single(s => s.CurrencyCode == "USD");

        var gross = section.Rows.Single(r => r.Code == "GROSS").Amount;
        var contributions = section.Rows.Where(r => r.Code != "GROSS").Sum(r => r.Amount);
        var summary = reports.Summary.Single(s => s.CurrencyCode == "USD").Totals;

        Assert.Equal(gross + contributions, summary.EmployerCost);
        Assert.Equal(section.Totals.Amount, summary.EmployerCost);
    }

    [Fact]
    public async Task The_statutory_report_reconciles_to_the_obligation_register()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var reports = await Reports(fixture).BuildAsync(runId);
        var section = reports!.Statutory.Single(s => s.CurrencyCode == "USD");

        var obligations = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Where(o => o.PayrollRunId == runId && o.CurrencyCode == "USD").ToListAsync();

        Assert.Equal(obligations.Count, section.Rows.Count);
        Assert.Equal(obligations.Sum(o => o.CalculatedAmount), section.Totals.Calculated);
        Assert.Equal(0m, section.Totals.Paid);
        Assert.Equal(obligations.Sum(o => o.CalculatedAmount), section.Totals.Outstanding);
    }

    [Fact]
    public async Task Project_allocations_sum_to_the_total_employer_cost()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var reports = await Reports(fixture).BuildAsync(runId);
        var allocations = reports!.ProjectLabourCost.SingleOrDefault(s => s.CurrencyCode == "USD");
        var summary = reports.Summary.Single(s => s.CurrencyCode == "USD").Totals;

        if (allocations is not null && allocations.Rows.Count > 0)
        {
            Assert.Equal(summary.EmployerCost, allocations.Totals.AllocatedCost);
        }
    }

    /// <summary>
    /// The central reporting rule: currency sections are separate, and no field anywhere holds a
    /// total that spans them.
    /// </summary>
    [Fact]
    public async Task Reports_never_sum_across_currencies()
    {
        using var fixture = await SetUpAsync();

        // Add a ZiG employee alongside the USD one.
        await AddZigEmployeeAsync(fixture);
        var runId = await RunThroughFinaliseAsync(fixture);

        var reports = await Reports(fixture).BuildAsync(runId);

        Assert.Equal(2, reports!.CurrencySummary.Count);
        Assert.Contains(reports.CurrencySummary, c => c.CurrencyCode == "USD");
        Assert.Contains(reports.CurrencySummary, c => c.CurrencyCode == "ZWG");

        var usd = reports.CurrencySummary.Single(c => c.CurrencyCode == "USD");
        var zig = reports.CurrencySummary.Single(c => c.CurrencyCode == "ZWG");

        // Each section's totals cover only its own currency.
        Assert.Equal(850m, usd.Gross);
        Assert.Equal(15000m, zig.Gross);
        Assert.NotEqual(usd.Gross + zig.Gross, usd.Gross);

        Assert.Equal(2, reports.Summary.Count);
        Assert.Equal(2, reports.Register.Count);
        Assert.Equal(2, reports.Deductions.Count);
        Assert.Equal(2, reports.EmployerCosts.Count);
        Assert.Equal("ZiG", zig.CurrencyLabel);
    }

    [Fact]
    public async Task The_deduction_report_separates_statutory_from_other_deductions()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var reports = await Reports(fixture).BuildAsync(runId);
        var section = reports!.Deductions.Single(s => s.CurrencyCode == "USD");

        Assert.Contains(section.Rows, r => r.Code == "PAYE" && r.IsStatutory);
        Assert.Contains(section.Rows, r => r.Code == "NSSA_EE" && r.IsStatutory);
        Assert.Equal(section.Rows.Sum(r => r.Total), section.Totals.Total);
    }

    private static async Task AddZigEmployeeAsync(Fixture fixture)
    {
        var db = fixture.Db.Context;

        db.TaxRules.Add(ZwgMonthlyTable());
        db.NssaRules.Add(new Domain.Statutory.NssaRule
        {
            RuleId = "NSSA-POBS-2026-ZWG", Name = "NSSA POBS ZiG", Currency = "ZWG",
            EmployeeRate = 0.045m, EmployerRate = 0.045m, CeilingAmount = 18000m,
            CeilingPeriodBasis = Domain.Statutory.PeriodBasis.Monthly,
            CeilingApplication = Domain.Statutory.CeilingApplicationMethod.ProRataByPeriodLength,
            EarningsBasis = Domain.Statutory.NssaEarningsBasis.BasicOnly,
            MinimumAge = 16, MaximumAge = 64,
            CalculationMethod = Domain.Statutory.CalculationMethod.CappedPercentage,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = Domain.Statutory.VerificationStatus.Verified,
            Source = new Domain.Statutory.RuleSource { Source = "test" }
        });
        db.ApwcsRules.Add(new Domain.Statutory.ApwcsRule
        {
            RuleId = "APWCS-2026-ZWG", Name = "APWCS ZiG", Currency = "ZWG",
            IndustryCode = "CON", IndustryClassification = "Construction", Rate = 0.025m,
            Base = Domain.Statutory.ApwcsBase.BasicEarnings,
            CalculationMethod = Domain.Statutory.CalculationMethod.PercentageOfBase,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = Domain.Statutory.VerificationStatus.Verified,
            Source = new Domain.Statutory.RuleSource { Source = "test" }
        });
        await db.SaveChangesAsync();

        var employees = new EmployeeService(db, fixture.Db.User, fixture.Db.Clock);
        var contracts = new EmployeeContractService(db, fixture.Db.User);
        var dube = NewEmployee(fixture.CompanyId, "EMP-0032");
        dube.FirstName = "Peter";
        dube.LastName = "Dube";
        dube.NationalId = "63-7654321 A 11";
        var created = (await employees.CreateAsync(dube)).Value!;
        await contracts.CreateInitialAsync(
            NewContract(fixture.CompanyId, created.Id, fixture.EmploymentTypeId, 15000m, "ZWG"));
    }

    private static Domain.Statutory.TaxRule ZwgMonthlyTable()
    {
        var table = new Domain.Statutory.TaxRule
        {
            RuleId = "PAYE-ZWG-2026-MONTHLY-TEST", Name = "PAYE ZiG monthly (test fixture)",
            Currency = "ZWG", TaxYear = 2026, PeriodBasis = Domain.Statutory.PeriodBasis.Monthly,
            CalculationMethod = Domain.Statutory.CalculationMethod.PeriodTable,
            EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 12, 31),
            VerificationStatus = Domain.Statutory.VerificationStatus.Verified,
            Source = new Domain.Statutory.RuleSource { Source = "test fixture" }
        };
        table.Brackets.Add(new Domain.Statutory.TaxBracket
        {
            Sequence = 1, LowerBound = 0m, UpperBound = 2800m, Rate = 0m
        });
        table.Brackets.Add(new Domain.Statutory.TaxBracket
        {
            Sequence = 2, LowerBound = 2800m, UpperBound = null, Rate = 0.20m
        });
        return table;
    }
}
