using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Audit;
using Tawaka.Domain.Currencies;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

public class AuditTests
{
    private static ExchangeRate NewRate(decimal rate = 26.50m) => new()
    {
        FromCurrency = "USD",
        ToCurrency = "ZWG",
        Rate = rate,
        Source = "RBZ interbank",
        RateDate = new DateOnly(2026, 9, 30),
        EffectiveFrom = new DateOnly(2026, 9, 30)
    };

    [Fact]
    public void Creating_an_entity_writes_a_create_entry_attributed_to_the_user()
    {
        using var db = new TestDatabase(new TestUser("u-77", "T. Ncube"));

        db.Context.ExchangeRates.Add(NewRate());
        db.Context.SaveChanges();

        var log = db.Context.AuditLogs.AsNoTracking()
            .Single(a => a.EntityName == nameof(ExchangeRate));

        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Equal("u-77", log.UserId);
        Assert.Equal("T. Ncube", log.UserName);
        Assert.Equal(db.Clock.Now, log.OccurredAt);
    }

    [Fact]
    public void Updating_records_the_field_with_its_old_and_new_value()
    {
        using var db = new TestDatabase();
        var rate = NewRate();
        db.Context.ExchangeRates.Add(rate);
        db.Context.SaveChanges();

        rate.Rate = 28.00m;
        db.Context.SaveChanges();

        var update = db.Context.AuditLogs.AsNoTracking()
            .Single(a => a.Action == AuditAction.Update && a.FieldName == nameof(ExchangeRate.Rate));

        Assert.Equal("26.50", update.OldValue);
        Assert.Equal("28.00", update.NewValue);
    }

    /// <summary>A salary or rate change must be answerable as "who, what, from, to, when".</summary>
    [Fact]
    public void Statutory_rule_changes_are_audited_field_by_field()
    {
        using var db = new TestDatabase();
        var rule = new AidsLevyRule
        {
            RuleId = "AIDS-LEVY-2026",
            Name = "AIDS Levy",
            Rate = 0.03m,
            Base = AidsLevyBase.TaxAfterCredits,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            VerificationStatus = VerificationStatus.Supported,
            Source = new RuleSource { Source = "test" }
        };
        db.Context.AidsLevyRules.Add(rule);
        db.Context.SaveChanges();

        rule.VerificationStatus = VerificationStatus.Verified;
        rule.Rate = 0.035m;
        db.Context.SaveChanges();

        var updates = db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.Update)
            .ToList();

        Assert.Contains(updates, u => u.FieldName == nameof(AidsLevyRule.Rate)
                                      && u.OldValue == "0.03" && u.NewValue == "0.035");
        Assert.Contains(updates, u => u.FieldName == nameof(AidsLevyRule.VerificationStatus)
                                      && u.OldValue == "Supported" && u.NewValue == "Verified");
    }

    [Fact]
    public void Unchanged_fields_are_not_logged()
    {
        using var db = new TestDatabase();
        var rate = NewRate();
        db.Context.ExchangeRates.Add(rate);
        db.Context.SaveChanges();

        rate.Rate = 26.50m;
        rate.Source = "RBZ interbank";
        db.Context.SaveChanges();

        Assert.Empty(db.Context.AuditLogs.AsNoTracking().Where(a => a.Action == AuditAction.Update));
    }

    [Fact]
    public void The_audit_trail_does_not_audit_itself()
    {
        using var db = new TestDatabase();
        db.Context.ExchangeRates.Add(NewRate());
        db.Context.SaveChanges();

        Assert.Empty(db.Context.AuditLogs.AsNoTracking().Where(a => a.EntityName == nameof(AuditLog)));
    }

    [Fact]
    public void Auditable_fields_are_stamped_on_create_and_update()
    {
        using var db = new TestDatabase(new TestUser("u-77"));
        var rate = NewRate();
        db.Context.ExchangeRates.Add(rate);
        db.Context.SaveChanges();

        Assert.Equal(db.Clock.Now, rate.CreatedAt);
        Assert.Equal("u-77", rate.CreatedBy);
        Assert.Null(rate.ModifiedAt);

        rate.Rate = 27m;
        db.Context.SaveChanges();

        Assert.Equal(db.Clock.Now, rate.ModifiedAt);
        Assert.Equal("u-77", rate.ModifiedBy);
    }

    [Fact]
    public void Deleting_is_recorded()
    {
        using var db = new TestDatabase();
        var rate = NewRate();
        db.Context.ExchangeRates.Add(rate);
        db.Context.SaveChanges();

        db.Context.ExchangeRates.Remove(rate);
        db.Context.SaveChanges();

        Assert.Single(db.Context.AuditLogs.AsNoTracking().Where(a => a.Action == AuditAction.Delete));
    }

    [Fact]
    public void Changes_saved_together_share_a_correlation_id()
    {
        using var db = new TestDatabase();
        var second = NewRate(27m);
        second.RateDate = new DateOnly(2026, 10, 1);
        db.Context.ExchangeRates.AddRange(NewRate(), second);
        db.Context.SaveChanges();

        var correlations = db.Context.AuditLogs.AsNoTracking()
            .Select(a => a.CorrelationId).Distinct().ToList();

        Assert.Single(correlations);
    }
}
