using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Employees;
using Tawaka.Application.Payroll;
using Tawaka.Application.Security;
using Tawaka.Application.Statutory;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory;
using Tawaka.Infrastructure.Persistence;
using Tawaka.Payroll.Engine;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// End-to-end payroll: real database, real rule resolution, real engine. These cover the parts
/// the pure engine tests cannot — persistence, historical reproducibility and the workflow gates.
/// </summary>
public class PayrollRunTests : EmployeeTestBase
{
    private static async Task<PayrollFixture> SetUpPayrollAsync(ICurrentUser? user = null)
    {
        var (db, companyId, permanentTypeId, _) = await SetUpAsync(user);

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
            CompanyId = companyId,
            Code = "2026-09",
            Name = "September 2026",
            Frequency = PeriodBasis.Monthly,
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 30),
            PayDate = new DateOnly(2026, 9, 30),
            TaxYear = 2026,
            Mode = PayrollMode.Development
        };
        db.Context.PayrollPeriods.Add(period);
        await db.Context.SaveChangesAsync();

        var source = new EfStatutoryRuleSource(db.Context);
        var resolver = new StatutoryRuleResolver(source);
        var builder = new PayrollSnapshotBuilder(db.Context, resolver);
        var obligations = new Tawaka.Application.Statutory.Obligations.StatutoryObligationService(
            db.Context, db.User, db.Clock);
        var service = new PayrollRunService(db.Context, builder, obligations, db.User, db.Clock);

        return new PayrollFixture(db, companyId, employee, period, service, builder, permanentTypeId);
    }

    private sealed record PayrollFixture(
        TestDatabase Db, Guid CompanyId, Employee Employee, PayrollPeriod Period,
        PayrollRunService Service, PayrollSnapshotBuilder Snapshots, Guid EmploymentTypeId)
        : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    /// <summary>Marks the seeded rules verified, as a verification exercise would.</summary>
    private static async Task VerifyRulesAsync(TestDatabase db)
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

        var nssa = await db.Context.NssaRules.FirstAsync();
        nssa.CeilingApplication = CeilingApplicationMethod.ProRataByPeriodLength;

        // APWCS is employer-specific and is not seeded; a real company enters its assessed rate.
        db.Context.ApwcsRules.Add(new ApwcsRule
        {
            RuleId = "APWCS-2026",
            Name = "APWCS assessed rate",
            Currency = "USD",
            IndustryClassification = "Construction",
            IndustryCode = "CON",
            Rate = 0.025m,
            Base = ApwcsBase.BasicEarnings,
            CalculationMethod = CalculationMethod.PercentageOfBase,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = VerificationStatus.Verified,
            Source = new RuleSource { Source = "NSSA assessment" }
        });

        await db.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task A_run_can_be_created_for_a_period()
    {
        using var fixture = await SetUpPayrollAsync();

        var result = await fixture.Service.CreateRunAsync(fixture.Period.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(PayrollRunStatus.Draft, result.Value!.Status);
        Assert.Equal(1, result.Value.RunNumber);
    }

    /// <summary>
    /// The seeded rules are unverified, so a development calculation still produces figures but
    /// every one that needs an unavailable rule is recorded as unresolved rather than zero.
    /// </summary>
    [Fact]
    public async Task A_development_run_calculates_and_records_what_it_could_not_resolve()
    {
        using var fixture = await SetUpPayrollAsync();
        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;

        var calculated = await fixture.Service.CalculateAsync(run.Id);

        Assert.True(calculated.Succeeded);
        Assert.Equal(PayrollRunStatus.Review, calculated.Value!.Status);
        Assert.Equal(PayrollCalculator.Version, calculated.Value.EngineVersion);

        var runEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.UnresolvedItems)
            .SingleAsync(e => e.PayrollRunId == run.Id);

        // APWCS is never seeded, so it must be unresolved — not silently zero.
        Assert.Contains(runEmployee.UnresolvedItems, u => u.ComplianceQuestion == "Q6");
        Assert.Null(runEmployee.ApwcsAmount);
    }

    /// <summary>With every rule verified, the run calculates cleanly and matches the engine.</summary>
    [Fact]
    public async Task A_verified_run_produces_the_expected_figures()
    {
        using var fixture = await SetUpPayrollAsync();
        await VerifyRulesAsync(fixture.Db);

        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Service.CalculateAsync(run.Id);

        var runEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.UnresolvedItems)
            .Include(e => e.EarningLines)
            .Include(e => e.DeductionLines)
            .SingleAsync(e => e.PayrollRunId == run.Id);

        Assert.Empty(runEmployee.UnresolvedItems);
        Assert.True(runEmployee.IsCalculated);

        // Basic 850 exceeds the USD 700 ceiling, so NSSA is 700 x 4.5% = 31.50 and taxable
        // income is 850 - 31.50 = 818.50.
        Assert.Equal(850m, runEmployee.GrossEarningsAmount);
        Assert.Equal(700m, runEmployee.NssaInsurableEarningsAmount);
        Assert.Equal(31.50m, runEmployee.NssaEmployeeAmount);
        Assert.Equal(818.50m, runEmployee.TaxableIncomeAmount);
        Assert.Equal("USD", runEmployee.CurrencyCode);

        // Money survives the round trip through scaled-integer storage.
        Assert.Equal(Domain.Common.Money.Usd(850m), runEmployee.GrossEarnings);
        Assert.Equal(runEmployee.GrossEarningsAmount - runEmployee.TotalDeductionsAmount,
            runEmployee.NetPayAmount);
    }

    [Fact]
    public async Task The_calculation_trace_is_persisted_for_every_figure()
    {
        using var fixture = await SetUpPayrollAsync();
        await VerifyRulesAsync(fixture.Db);
        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Service.CalculateAsync(run.Id);

        var runEmployee = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.TraceEntries)
            .SingleAsync(e => e.PayrollRunId == run.Id);

        foreach (var item in new[] { "Gross earnings", "NSSA employee", "Taxable income", "PAYE", "AIDS Levy", "Net pay" })
        {
            Assert.Contains(runEmployee.TraceEntries, t => t.ItemKey == item);
        }

        var paye = runEmployee.TraceEntries.Single(t => t.ItemKey == "PAYE");
        Assert.Equal("PAYE-USD-2026-MONTHLY", paye.RuleId);
        Assert.NotNull(paye.Steps);
        Assert.NotNull(paye.RoundingApplied);
        Assert.NotNull(paye.Explanation);
    }

    /// <summary>
    /// Historical reproducibility: after a salary increase, recalculating September still uses the
    /// contract version that applied in September.
    /// </summary>
    [Fact]
    public async Task A_later_salary_increase_does_not_change_a_past_period()
    {
        using var fixture = await SetUpPayrollAsync();
        await VerifyRulesAsync(fixture.Db);

        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Service.CalculateAsync(run.Id);
        var before = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == run.Id);

        // Increase the salary with effect from October.
        var contracts = new EmployeeContractService(fixture.Db.Context, fixture.Db.User);
        await contracts.SupersedeAsync(fixture.Employee.Id,
            NewContract(fixture.CompanyId, fixture.Employee.Id, fixture.EmploymentTypeId, 1200m),
            new DateOnly(2026, 10, 1), "Annual increase");

        // Recalculating September must still use the September contract.
        await fixture.Service.CalculateAsync(run.Id);
        var after = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == run.Id);

        Assert.Equal(850m, after.GrossEarningsAmount);
        Assert.Equal(before.GrossEarningsAmount, after.GrossEarningsAmount);
        Assert.Equal(before.NetPayAmount, after.NetPayAmount);
        Assert.Equal(1, after.ContractVersion);
    }

    /// <summary>Changing today's tax table cannot alter a payroll already calculated under the old one.</summary>
    [Fact]
    public async Task A_future_tax_table_does_not_affect_the_current_period()
    {
        using var fixture = await SetUpPayrollAsync();
        await VerifyRulesAsync(fixture.Db);

        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Service.CalculateAsync(run.Id);
        var before = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == run.Id);

        // A 2027 table is loaded; September 2026 must be unaffected.
        var table2027 = new TaxRule
        {
            RuleId = "PAYE-USD-2027-MONTHLY",
            Name = "PAYE USD monthly 2027",
            Currency = "USD",
            TaxYear = 2027,
            PeriodBasis = PeriodBasis.Monthly,
            CalculationMethod = CalculationMethod.PeriodTable,
            EffectiveFrom = new DateOnly(2027, 1, 1),
            EffectiveTo = new DateOnly(2027, 12, 31),
            VerificationStatus = VerificationStatus.Verified,
            Source = new RuleSource { Source = "test" }
        };
        table2027.Brackets.Add(new TaxBracket
        {
            Sequence = 1, LowerBound = 0m, UpperBound = null, Rate = 0.45m
        });
        fixture.Db.Context.TaxRules.Add(table2027);
        await fixture.Db.Context.SaveChangesAsync();

        await fixture.Service.CalculateAsync(run.Id);
        var after = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == run.Id);

        Assert.Equal(before.PayeAfterCreditsAmount, after.PayeAfterCreditsAmount);
    }

    [Fact]
    public async Task An_approved_run_cannot_be_silently_recalculated()
    {
        using var fixture = await SetUpPayrollAsync();
        await VerifyRulesAsync(fixture.Db);
        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Service.CalculateAsync(run.Id);

        var stored = await fixture.Db.Context.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        stored.Status = PayrollRunStatus.Approved;
        await fixture.Db.Context.SaveChangesAsync();

        var result = await fixture.Service.CalculateAsync(run.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e => e.Message.Contains("frozen"));
    }

    /// <summary>A locked run is immutable at the data layer, as a locked period is.</summary>
    [Fact]
    public async Task A_locked_run_cannot_be_modified()
    {
        using var fixture = await SetUpPayrollAsync();
        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;

        var stored = await fixture.Db.Context.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        stored.Status = PayrollRunStatus.Locked;
        await fixture.Db.Context.SaveChangesAsync();
        fixture.Db.Context.ChangeTracker.Clear();

        var locked = await fixture.Db.Context.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        locked.Notes = "Tampered";

        Assert.Throws<Interceptors.PeriodLockedException>(() => fixture.Db.Context.SaveChanges());
    }

    /// <summary>A development run cannot be approved, however tidy its figures look.</summary>
    [Fact]
    public async Task A_development_run_cannot_be_approved()
    {
        using var fixture = await SetUpPayrollAsync();
        await VerifyRulesAsync(fixture.Db);
        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Service.CalculateAsync(run.Id);

        var approval = await fixture.Service.ApproveAsync(run.Id);

        Assert.False(approval.IsValid);
        Assert.Contains(approval.Errors, e => e.Message.Contains("development calculation"));
    }

    /// <summary>A run with unresolved figures cannot be approved.</summary>
    [Fact]
    public async Task A_run_with_unresolved_figures_cannot_be_approved()
    {
        using var fixture = await SetUpPayrollAsync();
        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Service.CalculateAsync(run.Id);

        var stored = await fixture.Db.Context.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        stored.Mode = PayrollMode.Live;
        await fixture.Db.Context.SaveChangesAsync();

        var approval = await fixture.Service.ApproveAsync(run.Id);

        Assert.False(approval.IsValid);
        Assert.Contains(approval.Errors, e => e.Message.Contains("could not be calculated"));
    }

    /// <summary>Segregation of duties survives into the run workflow, not just the role definitions.</summary>
    [Fact]
    public async Task The_user_who_calculated_a_run_cannot_approve_it()
    {
        using var fixture = await SetUpPayrollAsync();
        await VerifyRulesAsync(fixture.Db);
        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Service.CalculateAsync(run.Id);

        var stored = await fixture.Db.Context.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        stored.Mode = PayrollMode.Live;
        await fixture.Db.Context.SaveChangesAsync();

        var approval = await fixture.Service.ApproveAsync(run.Id);

        Assert.Contains(approval.Errors, e => e.Message.Contains("Segregation of duties"));
    }

    [Fact]
    public async Task Calculating_requires_the_calculate_permission()
    {
        using var fixture = await SetUpPayrollAsync(
            TestUser.WithPermissions(Permissions.PayrollView, Permissions.PayrollCreate,
                Permissions.EmployeesView, Permissions.EmployeesEdit, Permissions.EmployeesEditSalary));
        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            fixture.Service.CalculateAsync(run.Id));
    }

    /// <summary>An employee with no contract in force is recorded as excluded, never skipped quietly.</summary>
    [Fact]
    public async Task An_employee_without_a_contract_is_recorded_as_excluded()
    {
        using var fixture = await SetUpPayrollAsync();
        var employees = new EmployeeService(fixture.Db.Context, fixture.Db.User, fixture.Db.Clock);
        var extra = NewEmployee(fixture.CompanyId, "EMP-0099");
        extra.NationalId = "63-9999999 Z 09";
        await employees.CreateAsync(extra);

        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Service.CalculateAsync(run.Id);

        var excluded = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .SingleAsync(e => e.PayrollRunId == run.Id && e.EmployeeNumber == "EMP-0099");

        Assert.True(excluded.IsExcluded);
        Assert.Contains("No contract", excluded.ExclusionReason!);
    }

    /// <summary>TC-24/TC-25: USD and ZiG employees coexist, each retaining their own currency.</summary>
    [Fact]
    public async Task A_single_run_holds_usd_and_zig_employees_without_merging_them()
    {
        using var fixture = await SetUpPayrollAsync();
        await VerifyRulesAsync(fixture.Db);

        // A ZiG table and NSSA rule for the second employee.
        var zwgTable = new TaxRule
        {
            RuleId = "PAYE-ZWG-2026-MONTHLY",
            Name = "PAYE ZiG monthly 2026",
            Currency = "ZWG",
            TaxYear = 2026,
            PeriodBasis = PeriodBasis.Monthly,
            CalculationMethod = CalculationMethod.PeriodTable,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveTo = new DateOnly(2026, 12, 31),
            VerificationStatus = VerificationStatus.Verified,
            Source = new RuleSource { Source = "test" }
        };
        zwgTable.Brackets.Add(new TaxBracket { Sequence = 1, LowerBound = 0m, UpperBound = 2800m, Rate = 0m });
        zwgTable.Brackets.Add(new TaxBracket { Sequence = 2, LowerBound = 2800m, UpperBound = null, Rate = 0.20m });
        fixture.Db.Context.TaxRules.Add(zwgTable);

        fixture.Db.Context.NssaRules.Add(new NssaRule
        {
            RuleId = "NSSA-POBS-2026-ZWG", Name = "NSSA POBS ZiG", Currency = "ZWG",
            EmployeeRate = 0.045m, EmployerRate = 0.045m, CeilingAmount = 18000m,
            CeilingPeriodBasis = PeriodBasis.Monthly,
            CeilingApplication = CeilingApplicationMethod.ProRataByPeriodLength,
            EarningsBasis = NssaEarningsBasis.BasicOnly, MinimumAge = 16, MaximumAge = 64,
            CalculationMethod = CalculationMethod.CappedPercentage,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = VerificationStatus.Verified,
            Source = new RuleSource { Source = "test" }
        });
        fixture.Db.Context.ApwcsRules.Add(new ApwcsRule
        {
            RuleId = "APWCS-2026-ZWG", Name = "APWCS ZiG", Currency = "ZWG",
            IndustryCode = "CON", IndustryClassification = "Construction", Rate = 0.025m,
            Base = ApwcsBase.BasicEarnings, CalculationMethod = CalculationMethod.PercentageOfBase,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = VerificationStatus.Verified,
            Source = new RuleSource { Source = "test" }
        });
        await fixture.Db.Context.SaveChangesAsync();

        var employees = new EmployeeService(fixture.Db.Context, fixture.Db.User, fixture.Db.Clock);
        var contracts = new EmployeeContractService(fixture.Db.Context, fixture.Db.User);
        var dube = NewEmployee(fixture.CompanyId, "EMP-0032");
        dube.FirstName = "Peter";
        dube.LastName = "Dube";
        dube.NationalId = "63-7654321 A 11";
        var created = (await employees.CreateAsync(dube)).Value!;
        await contracts.CreateInitialAsync(
            NewContract(fixture.CompanyId, created.Id, fixture.EmploymentTypeId, 15000m, "ZWG"));

        var run = (await fixture.Service.CreateRunAsync(fixture.Period.Id)).Value!;
        await fixture.Service.CalculateAsync(run.Id);

        var rows = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Where(e => e.PayrollRunId == run.Id).ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.CurrencyCode == "USD" && r.GrossEarningsAmount == 850m);
        Assert.Contains(rows, r => r.CurrencyCode == "ZWG" && r.GrossEarningsAmount == 15000m);

        // Totals are per currency, and the two are never added.
        var byCurrency = rows.GroupBy(r => r.CurrencyCode)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.NetPayAmount ?? 0m));
        Assert.Equal(2, byCurrency.Count);
    }
}
