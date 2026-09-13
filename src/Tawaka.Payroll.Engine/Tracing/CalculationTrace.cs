using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;

namespace Tawaka.Payroll.Engine.Tracing;

/// <summary>
/// One step in a derivation, e.g. "200.00 x 20% = 40.00". Steps are recorded as they are
/// performed so the explanation is the calculation, not a description written afterwards.
/// </summary>
public sealed record TraceStep(string Description, decimal? Value = null)
{
    public override string ToString() =>
        Value is null ? Description : $"{Description} = {Value.Value:N4}";
}

/// <summary>
/// The full derivation of a single calculated figure: which rule, which bracket, what inputs,
/// what rounding, what came out. This is what the user sees when they click a number.
/// </summary>
public sealed record TraceEntry
{
    public required string Stage { get; init; }

    public int Sequence { get; init; }

    /// <summary>What was calculated, e.g. "PAYE" or "NSSA employee contribution".</summary>
    public required string ItemKey { get; init; }

    public string? RuleId { get; init; }

    public StatutoryRuleType? RuleType { get; init; }

    public VerificationStatus? VerificationStatus { get; init; }

    public string? RuleSource { get; init; }

    public DateOnly? RuleEffectiveFrom { get; init; }

    public IReadOnlyDictionary<string, string> Inputs { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyList<TraceStep> Steps { get; init; } = Array.Empty<TraceStep>();

    /// <summary>Unrounded result, retained so rounding is visible rather than implicit.</summary>
    public decimal? RawValue { get; init; }

    public Money? Output { get; init; }

    public string? RoundingApplied { get; init; }

    public Currencies.ConversionRecord? Conversion { get; init; }

    public string? Explanation { get; init; }
}

/// <summary>An ordered collection of trace entries for one employee's payroll calculation.</summary>
public sealed class CalculationTrace
{
    private readonly List<TraceEntry> _entries = new();

    public IReadOnlyList<TraceEntry> Entries => _entries;

    public void Add(TraceEntry entry) => _entries.Add(entry);

    public TraceEntry? ForItem(string itemKey) =>
        _entries.FirstOrDefault(e => string.Equals(e.ItemKey, itemKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when any figure in this trace came from a rule not usable in live payroll.</summary>
    public bool ContainsUnverifiedRule =>
        _entries.Any(e => e.VerificationStatus.HasValue &&
                          !e.VerificationStatus.Value.IsUsableInLivePayroll());
}
