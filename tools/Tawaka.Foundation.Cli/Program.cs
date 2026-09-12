using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tawaka.Application.Statutory;
using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;
using Tawaka.Infrastructure;
using Tawaka.Infrastructure.Persistence;
using Tawaka.Infrastructure.Seeding;

// A cross-platform way to exercise the Milestone 1 foundation without the Windows desktop shell:
// it creates the database from the real migrations, seeds the statutory baseline, prints the rule
// register with verification status, and runs the live payroll gate.

var databasePath = args.Length > 0
    ? args[0]
    : Path.Combine(Path.GetTempPath(), "tawaka-foundation.db");

if (File.Exists(databasePath))
{
    File.Delete(databasePath);
}

var services = new ServiceCollection();
services.AddTawakaInfrastructure($"Data Source={databasePath}");
await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();

var context = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
var gate = scope.ServiceProvider.GetRequiredService<LivePayrollGate>();

Console.WriteLine("TAWAKA PAYROLL — FOUNDATION CHECK");
Console.WriteLine($"Database: {databasePath}");
Console.WriteLine();

await context.Database.MigrateAsync();
Console.WriteLine($"Migrations applied: {string.Join(", ", await context.Database.GetAppliedMigrationsAsync())}");

await scope.ServiceProvider.GetRequiredService<StatutoryRuleSeeder>().SeedAsync();

var currencies = await context.Currencies.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync();
Console.WriteLine($"Currencies:          {string.Join(", ", currencies.Select(c => $"{c.Code} ({c.DisplayCode})"))}");
Console.WriteLine($"Settings:            {await context.AppSettings.CountAsync()}");
Console.WriteLine($"Audit entries:       {await context.AuditLogs.CountAsync()}");
Console.WriteLine();

var rules = await context.StatutoryRules.AsNoTracking().OrderBy(r => r.RuleId).ToListAsync();

Console.WriteLine("STATUTORY RULE REGISTER");
Console.WriteLine(new string('-', 112));
Console.WriteLine($"{"RULE ID",-32} {"CUR",-4} {"FROM",-12} {"STATUS",-12} NAME");
Console.WriteLine(new string('-', 112));
foreach (var rule in rules)
{
    var active = rule.IsActive ? string.Empty : "  [inactive]";
    Console.WriteLine(
        $"{rule.RuleId,-32} {rule.Currency ?? "—",-4} {rule.EffectiveFrom:yyyy-MM-dd}   " +
        $"{rule.VerificationStatus,-12} {rule.Name}{active}");
}

Console.WriteLine(new string('-', 112));
Console.WriteLine(
    $"Total {rules.Count} rules — " +
    string.Join(", ", rules.GroupBy(r => r.VerificationStatus)
        .OrderBy(g => g.Key)
        .Select(g => $"{g.Key}: {g.Count()}")));
Console.WriteLine();

var payDate = new DateOnly(2026, 9, 30);
var report = gate.Check(new[]
{
    new StatutoryRuleQuery
    {
        RuleType = StatutoryRuleType.PayeTable,
        EffectiveDate = payDate,
        Currency = CurrencyCode.Usd,
        PeriodBasis = PeriodBasis.Monthly
    },
    new StatutoryRuleQuery
    {
        RuleType = StatutoryRuleType.NssaPobs,
        EffectiveDate = payDate,
        Currency = CurrencyCode.Usd
    },
    new StatutoryRuleQuery
    {
        RuleType = StatutoryRuleType.AidsLevy,
        EffectiveDate = payDate
    },
    new StatutoryRuleQuery
    {
        RuleType = StatutoryRuleType.Apwcs,
        EffectiveDate = payDate,
        Currency = CurrencyCode.Usd
    }
});

Console.WriteLine($"LIVE PAYROLL GATE — September 2026, monthly, USD");
Console.WriteLine(new string('-', 112));
Console.WriteLine(report.Render());
Console.WriteLine();
Console.WriteLine(report.IsBlocked
    ? "Result: payroll may run in DEVELOPMENT mode only."
    : "Result: payroll may run in LIVE mode.");
