using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Currencies;
using Tawaka.Domain.Statutory;
using Tawaka.Infrastructure.Seeding;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

public class DatabaseFoundationTests
{
    [Fact]
    public void Migration_creates_the_documented_tables()
    {
        using var db = new TestDatabase();

        var tables = db.TableNames();

        foreach (var expected in new[]
                 {
                     "AidsLevyRules", "AppSettings", "ApwcsRules", "AuditLogs", "Currencies",
                     "CurrencyTaxStrategyRules", "EmployerLevyRules", "ExchangeRates",
                     "NssaEligibilityRules", "NssaRules", "PayrollPeriods", "StatutoryRules",
                     "TaxBrackets", "TaxCreditRules", "TaxExemptionRules", "TaxRules"
                 })
        {
            Assert.Contains(expected, tables);
        }
    }

    [Fact]
    public async Task Seeder_creates_both_payroll_currencies_with_local_labels()
    {
        using var db = new TestDatabase();
        await new StatutoryRuleSeeder(db.Context).SeedAsync();

        var currencies = db.Context.Currencies.AsNoTracking().OrderBy(c => c.SortOrder).ToList();

        Assert.Equal(2, currencies.Count);
        Assert.Equal("USD", currencies[0].Code);
        Assert.Equal("ZWG", currencies[1].Code);
        Assert.Equal("ZiG", currencies[1].DisplayCode);
        Assert.Equal("ZiG 18,500.00", currencies[1].Format(18500m));
    }

    /// <summary>
    /// The honesty check. No rule in the compliance specification has been confirmed against a
    /// primary source, so nothing may ship as Verified. If this test ever fails, either a rule was
    /// genuinely verified (and the specification should say so) or the gate has been undermined.
    /// </summary>
    [Fact]
    public async Task No_seeded_rule_is_marked_verified()
    {
        using var db = new TestDatabase();
        await new StatutoryRuleSeeder(db.Context).SeedAsync();

        var verified = db.AllRules()
            .Where(r => r.VerificationStatus == VerificationStatus.Verified)
            .ToList();

        Assert.Empty(verified);
    }

    [Fact]
    public async Task Every_seeded_rule_records_where_its_value_came_from()
    {
        using var db = new TestDatabase();
        await new StatutoryRuleSeeder(db.Context).SeedAsync();

        foreach (var rule in db.AllRules())
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Source?.Source),
                $"Rule {rule.RuleId} has no source.");
            Assert.False(string.IsNullOrWhiteSpace(rule.Notes),
                $"Rule {rule.RuleId} has no notes explaining its status.");
        }
    }

    /// <summary>
    /// The seeded PAYE tables carry no fixed-deduction column, because it was never obtained
    /// (spec Q26). Recording that absence explicitly is what stops it being quietly assumed zero.
    /// </summary>
    [Fact]
    public async Task Seeded_paye_tables_have_no_fixed_deduction_column()
    {
        using var db = new TestDatabase();
        await new StatutoryRuleSeeder(db.Context).SeedAsync();

        var brackets = db.Context.TaxBrackets.AsNoTracking().ToList();

        Assert.NotEmpty(brackets);
        Assert.All(brackets, b => Assert.Null(b.FixedDeduction));
    }

    [Fact]
    public async Task Nssa_seed_leaves_the_ceiling_application_undetermined()
    {
        using var db = new TestDatabase();
        await new StatutoryRuleSeeder(db.Context).SeedAsync();

        var nssa = db.Context.NssaRules.AsNoTracking().Single();

        Assert.Equal(CeilingApplicationMethod.NotDetermined, nssa.CeilingApplication);
        Assert.Equal(700m, nssa.CeilingAmount);
        Assert.Equal(0.045m, nssa.EmployeeRate);
        Assert.Equal(0.045m, nssa.EmployerRate);
        Assert.False(nssa.GrossUpEnabled);
        Assert.Equal(18, nssa.MinimumDaysInMonth);
    }

    [Fact]
    public async Task Conflicting_medical_credit_is_seeded_inactive_with_no_percentage()
    {
        using var db = new TestDatabase();
        await new StatutoryRuleSeeder(db.Context).SeedAsync();

        var medical = db.Context.TaxCreditRules.AsNoTracking()
            .Single(c => c.CreditType == TaxCreditType.MedicalAidContribution);

        Assert.False(medical.IsActive);
        Assert.Null(medical.PercentageOfQualifyingAmount);
    }

    [Fact]
    public async Task Sdf_levy_is_seeded_inactive_because_liability_is_unestablished()
    {
        using var db = new TestDatabase();
        await new StatutoryRuleSeeder(db.Context).SeedAsync();

        var sdf = db.Context.EmployerLevyRules.AsNoTracking()
            .Single(l => l.LevyType == EmployerLevyType.StandardsDevelopmentFund);
        var zimdef = db.Context.EmployerLevyRules.AsNoTracking()
            .Single(l => l.LevyType == EmployerLevyType.Zimdef);

        Assert.False(sdf.IsActive);
        Assert.True(zimdef.IsActive);
        Assert.Equal(15, zimdef.DueDayOfFollowingMonth);
        Assert.Equal(10, sdf.DueDayOfFollowingMonth);
    }

    [Fact]
    public async Task Seeding_is_idempotent()
    {
        using var db = new TestDatabase();
        var seeder = new StatutoryRuleSeeder(db.Context);

        await seeder.SeedAsync();
        var first = db.AllRules().Count;
        await seeder.SeedAsync();

        Assert.Equal(first, db.AllRules().Count);
    }

    /// <summary>
    /// Decimals persist as scaled integers, so they survive the round trip exactly. Stored as
    /// SQLite TEXT they would not compare or sum correctly.
    /// </summary>
    [Theory]
    [InlineData(0.045)]
    [InlineData(700.00)]
    [InlineData(26.50)]
    [InlineData(0.0377)]
    [InlineData(1008000.00)]
    public void Decimal_values_round_trip_exactly(decimal value)
    {
        using var db = new TestDatabase();

        db.Context.ExchangeRates.Add(new ExchangeRate
        {
            FromCurrency = "USD",
            ToCurrency = "ZWG",
            Rate = value,
            Source = "test",
            RateDate = new DateOnly(2026, 9, 30),
            EffectiveFrom = new DateOnly(2026, 9, 30)
        });
        db.Context.SaveChanges();
        db.Context.ChangeTracker.Clear();

        Assert.Equal(value, db.Context.ExchangeRates.AsNoTracking().Single().Rate);
    }
}
