using Tawaka.Domain.Common;

namespace Tawaka.Domain.Earnings;

/// <summary>
/// A standing earning for an employee, such as a housing allowance. Effective-dated, so a change
/// in allowance does not rewrite past payroll.
/// </summary>
public class EmployeeRecurringEarning : AuditableEntity
{
    public Guid EmployeeId { get; set; }

    public Guid EarningTypeId { get; set; }
    public EarningType? EarningType { get; set; }

    public decimal Amount { get; set; }

    /// <summary>
    /// ISO code. May differ from the employee's payroll currency where the company genuinely pays
    /// a component in another currency; the original is preserved and converted only under an
    /// approved multi-currency strategy.
    /// </summary>
    public string CurrencyCode { get; set; } = "USD";

    public EarningCalculationBasis CalculationBasis { get; set; } = EarningCalculationBasis.FixedAmount;

    /// <summary>Percentage or per-unit rate, where the basis is not a fixed amount.</summary>
    public decimal? Value { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    public DateRange EffectivePeriod => new(EffectiveFrom, EffectiveTo);

    public bool AppliesOn(DateOnly date) => IsActive && EffectivePeriod.Contains(date);

    public Money AsMoney() => new(Amount, new CurrencyCode(CurrencyCode));
}

/// <summary>A standing deduction for an employee, such as medical aid or union dues.</summary>
public class EmployeeRecurringDeduction : AuditableEntity
{
    public Guid EmployeeId { get; set; }

    public Guid DeductionTypeId { get; set; }
    public DeductionType? DeductionType { get; set; }

    public decimal Amount { get; set; }

    public string CurrencyCode { get; set; } = "USD";

    public EarningCalculationBasis CalculationBasis { get; set; } = EarningCalculationBasis.FixedAmount;

    public decimal? Value { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public int Priority { get; set; } = 100;

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    public DateRange EffectivePeriod => new(EffectiveFrom, EffectiveTo);

    public bool AppliesOn(DateOnly date) => IsActive && EffectivePeriod.Contains(date);

    public Money AsMoney() => new(Amount, new CurrencyCode(CurrencyCode));
}
