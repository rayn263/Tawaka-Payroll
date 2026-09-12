using Tawaka.Domain.Common;

namespace Tawaka.Domain.Statutory;

/// <summary>
/// Base class for every statutory rule. There are no statutory constants in this codebase: rates,
/// thresholds and ceilings are rows here, dated and graded (ADR-003).
/// </summary>
public abstract class StatutoryRule : AuditableEntity
{
    /// <summary>Stable business identifier, e.g. "PAYE-USD-2026" or "NSSA-POBS-2026Q3".</summary>
    public string RuleId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public abstract StatutoryRuleType RuleType { get; }

    public string Jurisdiction { get; set; } = "ZW";

    /// <summary>ISO currency code, or null where the rule is currency-neutral.</summary>
    public string? Currency { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public CalculationMethod CalculationMethod { get; set; }

    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Unverified;

    public RuleSource Source { get; set; } = new();

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateRange EffectivePeriod => new(EffectiveFrom, EffectiveTo);

    public CurrencyCode? CurrencyCode =>
        string.IsNullOrWhiteSpace(Currency) ? null : new CurrencyCode(Currency);

    public bool AppliesOn(DateOnly date) => IsActive && EffectivePeriod.Contains(date);

    public bool AppliesTo(CurrencyCode? currency)
    {
        if (Currency is null)
        {
            return true;
        }

        return currency.HasValue && currency.Value == new CurrencyCode(Currency);
    }

    public override string ToString() =>
        $"{RuleId} [{VerificationStatus}] {EffectivePeriod}";
}
