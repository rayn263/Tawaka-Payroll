using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Employees;
using Tawaka.Application.Loans;
using Tawaka.Application.Reports;
using Tawaka.Application.Statutory.Obligations;
using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// USD and ZiG are two different sums of money and are never added together. This tries to make
/// the application do it anyway.
/// </summary>
public class QaCurrencySeparationTests : PayrollFixtureBase
{
    /// <summary>
    /// Test scaffolding so a ZiG payroll can be run at all. Whether ZIMRA publishes a monthly ZiG
    /// table is compliance question Q29 and is still open; none of these figures is a claim about
    /// what is published, and none of them is used outside this test.
    /// </summary>
    private static async Task AddZwgScaffoldingAsync(Fixture fixture)
    {
        var db = fixture.Db.Context;

        var paye = new TaxRule
        {
            RuleId = "TEST-PAYE-ZWG-2026-MONTHLY",
            Name = "Test scaffolding: ZiG monthly table",
            Currency = "ZWG",
            TaxYear = 2026,
            PeriodBasis = PeriodBasis.Monthly,
            CalculationMethod = CalculationMethod.PeriodTable,
            BracketApplication = TaxBracketApplication.ProgressiveLadder,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveTo = new DateOnly(2026, 12, 31),
            VerificationStatus = VerificationStatus.Verified,
            Source = new RuleSource { Source = "Test scaffolding" }
        };
        paye.Brackets.Add(new TaxBracket { Sequence = 1, LowerBound = 0m, UpperBound = 2800m, Rate = 0m });
        paye.Brackets.Add(new TaxBracket { Sequence = 2, LowerBound = 2800m, UpperBound = 8400m, Rate = 0.20m });
        paye.Brackets.Add(new TaxBracket { Sequence = 3, LowerBound = 8400m, UpperBound = null, Rate = 0.30m });
        db.StatutoryRules.Add(paye);

        db.StatutoryRules.Add(new NssaRule
        {
            RuleId = "TEST-NSSA-POBS-2026-ZWG",
            Name = "Test scaffolding: NSSA POBS (ZiG)",
            Currency = "ZWG",
            EmployeeRate = 0.045m,
            EmployerRate = 0.045m,
            CeilingAmount = 18000m,
            CeilingPeriodBasis = PeriodBasis.Monthly,
            CeilingApplication = CeilingApplicationMethod.ProRataByPeriodLength,
            EarningsBasis = NssaEarningsBasis.BasicOnly,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveTo = new DateOnly(2026, 12, 31),
            VerificationStatus = VerificationStatus.Verified,
            Source = new RuleSource { Source = "Test scaffolding" }
        });

        db.StatutoryRules.Add(new ApwcsRule
        {
            RuleId = "TEST-APWCS-2026-ZWG",
            Name = "Test scaffolding: APWCS (ZiG)",
            Currency = "ZWG",
            IndustryClassification = "Construction",
            IndustryCode = "CON",
            Rate = 0.025m,
            Base = ApwcsBase.BasicEarnings,
            CalculationMethod = CalculationMethod.PercentageOfBase,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = VerificationStatus.Verified,
            Source = new RuleSource { Source = "Test scaffolding" }
        });

        var usdAidsLevy = await db.AidsLevyRules.AsNoTracking().FirstOrDefaultAsync(r => r.Currency == "USD");
        if (usdAidsLevy is not null && !await db.AidsLevyRules.AnyAsync(r => r.Currency == "ZWG"))
        {
            db.StatutoryRules.Add(new AidsLevyRule
            {
                RuleId = "TEST-AIDS-2026-ZWG",
                Name = "Test scaffolding: AIDS Levy (ZiG)",
                Currency = "ZWG",
                Rate = usdAidsLevy.Rate,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                VerificationStatus = VerificationStatus.Verified,
                Source = new RuleSource { Source = "Test scaffolding" }
            });
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<Employee> AddZwgEmployeeAsync(Fixture fixture)
    {
        var employees = new EmployeeService(fixture.Db.Context, fixture.Db.User, fixture.Db.Clock);
        var contracts = new EmployeeContractService(fixture.Db.Context, fixture.Db.User);

        var employee = (await employees.CreateAsync(new Employee
        {
            CompanyId = fixture.CompanyId,
            EmployeeNumber = "EMP-ZWG-1",
            FirstName = "Rudo",
            LastName = "Chikore",
            NationalId = "63-2223334 C 11",
            HireDate = new DateOnly(2024, 2, 1),
            Status = EmployeeStatus.Active
        })).Value!;

        await contracts.CreateInitialAsync(NewContract(
            fixture.CompanyId, employee.Id, fixture.EmploymentTypeId, 15000m, "ZWG"));

        fixture.Db.Context.EmployeeStatutoryProfiles.Add(new EmployeeStatutoryProfile
        {
            EmployeeId = employee.Id, TaxNumber = "BP0002", NssaNumber = "NSSA0002"
        });

        await fixture.Db.Context.SaveChangesAsync();
        fixture.Db.Context.ChangeTracker.Clear();
        return employee;
    }

    /// <summary>
    /// Structural, not behavioural: there is no field on the report model that *could* hold a
    /// total spanning currencies, because every monetary list is grouped by currency first.
    /// </summary>
    [Fact]
    public void No_report_collection_can_hold_a_figure_that_spans_currencies()
    {
        var monetaryLists = typeof(PayrollRunReports).GetProperties(
                BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType &&
                        p.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            .ToList();

        Assert.NotEmpty(monetaryLists);

        // The lists that carry no money: what was fed in, what was left out, and who did what.
        var withoutMoney = new[] { nameof(PayrollRunReports.Inputs),
            nameof(PayrollRunReports.SkippedInputs), nameof(PayrollRunReports.Audit),
            nameof(PayrollRunReports.Currencies), nameof(PayrollRunReports.CurrencySummary) };

        foreach (var property in monetaryLists.Where(p => !withoutMoney.Contains(p.Name)))
        {
            var element = property.PropertyType.GetGenericArguments()[0];

            Assert.True(
                element.IsGenericType &&
                element.GetGenericTypeDefinition() == typeof(CurrencySection<>),
                $"{property.Name} is a list of {element.Name}, which is not grouped by currency. " +
                "Every monetary report section must be a CurrencySection so that no total can " +
                "span currencies.");
        }

        // The currency summary is one row per currency, and each row names its own.
        Assert.Contains(
            typeof(CurrencySummaryRow).GetProperties(),
            p => p.Name == nameof(CurrencySummaryRow.CurrencyCode));
    }

    [Fact]
    public async Task A_mixed_currency_payroll_is_reported_as_two_payrolls_and_never_as_one()
    {
        using var fixture = await SetUpAsync();
        await AddZwgScaffoldingAsync(fixture);
        await AddZwgEmployeeAsync(fixture);

        var runId = await RunThroughFinaliseAsync(fixture);
        var services = PayrollServices.For(fixture.Db);

        var results = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Where(e => e.PayrollRunId == runId).ToListAsync();
        Assert.Equal(2, results.Count);

        var reports = await services.Reports.BuildAsync(runId);
        Assert.NotNull(reports);

        Assert.Equal(2, reports!.CurrencySummary.Count);
        Assert.Equal(new[] { "USD", "ZWG" }, reports.Currencies.OrderBy(c => c).ToArray());

        foreach (var section in new[] { "Summary", "Register", "Deductions", "EmployerCosts",
                     "Statutory", "EmployeeEarnings", "Tax", "Nssa" })
        {
            var value = typeof(PayrollRunReports).GetProperty(section)!.GetValue(reports)!;
            var rows = ((System.Collections.IEnumerable)value).Cast<object>().ToList();
            Assert.Equal(2, rows.Count);
        }

        // Each currency's gross is exactly its own employees' gross, never the other's.
        foreach (var summary in reports.CurrencySummary)
        {
            var expected = results
                .Where(r => r.CurrencyCode == summary.CurrencyCode)
                .Sum(r => r.GrossEarningsAmount ?? 0m);

            Assert.Equal(expected, summary.Gross);
        }

        var usd = reports.CurrencySummary.Single(c => c.CurrencyCode == "USD");
        var zwg = reports.CurrencySummary.Single(c => c.CurrencyCode == "ZWG");
        Assert.NotEqual(usd.Gross, zwg.Gross);

        // And the deliberate attempt: adding the two is refused, not silently totalled.
        Assert.Throws<CurrencyMismatchException>(() =>
            _ = new Money(usd.Gross, CurrencyCode.Usd) +
                new Money(zwg.Gross, CurrencyCode.Zwg));
    }

    [Fact]
    public async Task A_csv_export_keeps_each_currency_in_its_own_table()
    {
        using var fixture = await SetUpAsync();
        await AddZwgScaffoldingAsync(fixture);
        await AddZwgEmployeeAsync(fixture);

        var runId = await RunThroughFinaliseAsync(fixture);
        var reports = await PayrollServices.For(fixture.Db).Reports.BuildAsync(runId);

        var csv = PayrollReportCsv.Export(reports!, "register");

        Assert.Contains("USD", csv);
        Assert.Contains("ZWG", csv);
        Assert.Contains("Employee number", csv);

        // Two tables, each headed with its currency, and no line that mixes them.
        var usdIndex = csv.IndexOf("USD", StringComparison.Ordinal);
        var zwgIndex = csv.IndexOf("ZWG", StringComparison.Ordinal);
        Assert.True(usdIndex >= 0 && zwgIndex >= 0 && usdIndex != zwgIndex);

        foreach (var line in csv.Split('\n'))
        {
            Assert.False(
                line.Contains("USD", StringComparison.Ordinal) &&
                line.Contains("ZWG", StringComparison.Ordinal),
                $"A single exported line names both currencies: {line}");
        }
    }

    [Fact]
    public async Task A_statutory_obligation_cannot_be_paid_in_the_wrong_currency()
    {
        using var fixture = await SetUpAsync();
        var runId = await RunThroughFinaliseAsync(fixture);

        var obligation = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .FirstAsync(o => o.PayrollRunId == runId && o.CurrencyCode == "USD");

        Assert.True((await fixture.Obligations.ApproveAsync(obligation.Id)).IsValid);

        var wrongCurrency = await fixture.Obligations.RecordPaymentAsync(new RecordPaymentRequest
        {
            ObligationId = obligation.Id,
            Amount = obligation.CalculatedAmount,
            CurrencyCode = "ZWG",
            PaymentDate = new DateOnly(2026, 10, 8),
            PaymentReference = "RTGS-XCUR"
        });

        Assert.False(wrongCurrency.Succeeded);

        fixture.Db.Context.ChangeTracker.Clear();
        var unchanged = await fixture.Db.Context.StatutoryObligations.AsNoTracking()
            .Include(o => o.Payments)
            .SingleAsync(o => o.Id == obligation.Id);

        Assert.Empty(unchanged.Payments);
        Assert.Equal(obligation.CalculatedAmount, unchanged.Outstanding.Amount);
    }

    /// <summary>
    /// A loan is repaid out of pay, and pay is in one currency, so a loan in another currency is
    /// not recovered from this payroll — deliberately, because recovering it would need a
    /// conversion nobody has authorised (compliance question Q1).
    /// <para>
    /// What must not happen is that it disappears quietly: the employee still owes the money. The
    /// snapshot records why it was left out and the report says so.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_loan_in_another_currency_is_left_out_visibly_and_never_deducted_as_pay_currency()
    {
        using var fixture = await SetUpAsync();
        var services = PayrollServices.For(fixture.Db);

        var loan = (await services.Loans.CreateAsync(new LoanCommand
        {
            EmployeeId = fixture.Employee.Id,
            CurrencyCode = "ZWG",
            PrincipalAmount = 5000m,
            InstalmentCount = 5,
            FirstInstalmentDate = new DateOnly(2026, 9, 30)
        })).Value!;

        Assert.True((await services.Loans.SubmitAsync(loan.Id)).IsValid);

        fixture.Db.Context.ChangeTracker.Clear();
        var approver = PayrollServices.For(fixture.Db, new TestUser("u-manager", "Rudo Moyo"));
        Assert.True((await approver.Loans.ApproveAsync(loan.Id)).IsValid);

        fixture.Db.Context.ChangeTracker.Clear();
        Assert.True((await approver.Loans
            .DisburseAsync(loan.Id, new DateOnly(2026, 9, 1), "FBC-RTGS-1")).IsValid);

        fixture.Db.Context.ChangeTracker.Clear();
        var runId = await RunThroughFinaliseAsync(fixture);

        var result = await fixture.Db.Context.PayrollRunEmployees.AsNoTracking()
            .Include(e => e.DeductionLines)
            .SingleAsync(e => e.PayrollRunId == runId);

        // Nothing in ZiG reached a USD payslip.
        Assert.Equal("USD", result.CurrencyCode);
        Assert.All(result.DeductionLines, line => Assert.Equal("USD", line.CurrencyCode));
        Assert.DoesNotContain(result.DeductionLines, line => line.Code == "LOAN");

        // And the run says so, by name and with a reason.
        var reports = await PayrollServices.For(fixture.Db).Reports.BuildAsync(runId);
        var skipped = Assert.Single(reports!.SkippedInputs);

        Assert.Equal("Loan", skipped.InputType);
        Assert.Contains(loan.LoanNumber, skipped.Reason);
        Assert.Contains("ZWG", skipped.Reason);
        Assert.Contains("not recovered", skipped.Reason, StringComparison.OrdinalIgnoreCase);

        // It is on the exported file too, not only on the screen.
        var csv = PayrollReportCsv.Export(reports, "inputs");
        Assert.Contains("Inputs this run did not use", csv);
        Assert.Contains(loan.LoanNumber, csv);
    }
}
