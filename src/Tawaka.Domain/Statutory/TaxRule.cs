namespace Tawaka.Domain.Statutory;

/// <summary>
/// A PAYE table for one currency, one period basis and one effective period. ZIMRA publishes
/// daily, weekly, fortnightly, monthly and annual tables; each is stored as its own rule.
/// </summary>
public class TaxRule : StatutoryRule
{
    public override StatutoryRuleType RuleType => StatutoryRuleType.PayeTable;

    public int TaxYear { get; set; }

    public PeriodBasis PeriodBasis { get; set; }

    public ICollection<TaxBracket> Brackets { get; set; } = new List<TaxBracket>();

    /// <summary>Brackets in ascending order of lower bound.</summary>
    public IReadOnlyList<TaxBracket> OrderedBrackets() =>
        Brackets.OrderBy(b => b.Sequence).ThenBy(b => b.LowerBound).ToList();
}

/// <summary>
/// One band of a PAYE table. ZIMRA publishes tables in "gross x rate - fixed deduction" form; the
/// official deduction column is stored rather than reconstructed, so results reconcile
/// line-for-line with the published table.
/// </summary>
public class TaxBracket : Common.Entity
{
    public Guid TaxRuleId { get; set; }

    public TaxRule? TaxRule { get; set; }

    public int Sequence { get; set; }

    public decimal LowerBound { get; set; }

    /// <summary>Null means the top, open-ended band.</summary>
    public decimal? UpperBound { get; set; }

    /// <summary>Marginal rate as a fraction, e.g. 0.20 for 20%.</summary>
    public decimal Rate { get; set; }

    /// <summary>
    /// The published "less" column. Null means it has not been obtained from the official table
    /// yet — which is why the seeded 2026 tables cannot be marked Verified (spec Q26).
    /// </summary>
    public decimal? FixedDeduction { get; set; }

    public bool Contains(decimal amount) =>
        amount > LowerBound && (UpperBound is null || amount <= UpperBound.Value);
}
