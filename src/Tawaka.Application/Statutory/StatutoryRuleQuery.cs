using Tawaka.Domain.Common;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;

namespace Tawaka.Application.Statutory;

/// <summary>
/// The employee circumstances that can affect which statutory rule applies. Supplied by the
/// caller; the resolver never infers them.
/// </summary>
public sealed record EmployeeRuleContext
{
    public string? EmploymentTypeCode { get; init; }

    public int? Age { get; init; }

    public int? DaysEngagedInMonth { get; init; }

    public string? IndustryCode { get; init; }
}

/// <summary>A request to resolve one statutory rule.</summary>
public sealed record StatutoryRuleQuery
{
    public required StatutoryRuleType RuleType { get; init; }

    /// <summary>The date the rule must be effective on — normally the payroll period's pay date.</summary>
    public required DateOnly EffectiveDate { get; init; }

    public CurrencyCode? Currency { get; init; }

    /// <summary>Required for PAYE tables: the employee's payment frequency.</summary>
    public PeriodBasis? PeriodBasis { get; init; }

    public PayrollMode Mode { get; init; } = PayrollMode.Live;

    public EmployeeRuleContext? Employee { get; init; }

    /// <summary>Narrows within a rule type, e.g. a levy type or credit type.</summary>
    public string? Discriminator { get; init; }

    public string Describe()
    {
        var parts = new List<string> { RuleType.ToString() };
        if (Currency.HasValue)
        {
            parts.Add(Currency.Value.Value);
        }

        if (PeriodBasis.HasValue)
        {
            parts.Add(PeriodBasis.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(Discriminator))
        {
            parts.Add(Discriminator!);
        }

        parts.Add($"on {EffectiveDate:yyyy-MM-dd}");
        return string.Join(" / ", parts);
    }
}
