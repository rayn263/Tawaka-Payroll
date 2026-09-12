using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Domain.Statutory;

namespace Tawaka.Infrastructure.Persistence;

/// <summary>
/// Supplies candidate rules from the database. Tax brackets are eagerly loaded because a table
/// without its bands is not a usable rule.
/// </summary>
public sealed class EfStatutoryRuleSource : IStatutoryRuleSource
{
    private readonly PayrollDbContext _context;

    public EfStatutoryRuleSource(PayrollDbContext context)
    {
        _context = context;
    }

    public IReadOnlyList<StatutoryRule> GetRules(StatutoryRuleType ruleType) => ruleType switch
    {
        StatutoryRuleType.PayeTable =>
            _context.TaxRules.Include(r => r.Brackets).AsNoTracking().ToList(),
        StatutoryRuleType.AidsLevy => _context.AidsLevyRules.AsNoTracking().ToList(),
        StatutoryRuleType.NssaPobs => _context.NssaRules.AsNoTracking().ToList(),
        StatutoryRuleType.NssaEligibility => _context.NssaEligibilityRules.AsNoTracking().ToList(),
        StatutoryRuleType.Apwcs => _context.ApwcsRules.AsNoTracking().ToList(),
        StatutoryRuleType.EmployerLevy => _context.EmployerLevyRules.AsNoTracking().ToList(),
        StatutoryRuleType.TaxCredit => _context.TaxCreditRules.AsNoTracking().ToList(),
        StatutoryRuleType.TaxExemption => _context.TaxExemptionRules.AsNoTracking().ToList(),
        StatutoryRuleType.CurrencyTaxStrategy =>
            _context.CurrencyTaxStrategyRules.AsNoTracking().ToList(),
        _ => Array.Empty<StatutoryRule>()
    };
}
