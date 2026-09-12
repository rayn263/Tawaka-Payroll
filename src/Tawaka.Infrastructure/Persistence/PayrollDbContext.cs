using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Audit;
using Tawaka.Domain.Currencies;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;

namespace Tawaka.Infrastructure.Persistence;

/// <summary>
/// The payroll database. Statutory rules use table-per-type mapping: shared identity and
/// verification metadata live in <c>StatutoryRules</c>, with each rule kind in its own table, so
/// the documented table names hold while common behaviour stays in one place.
/// </summary>
public class PayrollDbContext : DbContext
{
    public PayrollDbContext(DbContextOptions<PayrollDbContext> options) : base(options)
    {
    }

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    public DbSet<StatutoryRule> StatutoryRules => Set<StatutoryRule>();

    public DbSet<TaxRule> TaxRules => Set<TaxRule>();

    public DbSet<TaxBracket> TaxBrackets => Set<TaxBracket>();

    public DbSet<AidsLevyRule> AidsLevyRules => Set<AidsLevyRule>();

    public DbSet<NssaRule> NssaRules => Set<NssaRule>();

    public DbSet<NssaEligibilityRule> NssaEligibilityRules => Set<NssaEligibilityRule>();

    public DbSet<ApwcsRule> ApwcsRules => Set<ApwcsRule>();

    public DbSet<EmployerLevyRule> EmployerLevyRules => Set<EmployerLevyRule>();

    public DbSet<TaxCreditRule> TaxCreditRules => Set<TaxCreditRule>();

    public DbSet<TaxExemptionRule> TaxExemptionRules => Set<TaxExemptionRule>();

    public DbSet<CurrencyTaxStrategyRule> CurrencyTaxStrategyRules => Set<CurrencyTaxStrategyRule>();

    public DbSet<PayrollPeriod> PayrollPeriods => Set<PayrollPeriod>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PayrollDbContext).Assembly);
    }
}
