using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;

namespace Tawaka.Payroll.Engine.Tracing;

/// <summary>Fluent builder so calculation code records its working as it goes.</summary>
public sealed class TraceEntryBuilder
{
    private readonly Dictionary<string, string> _inputs = new();
    private readonly List<TraceStep> _steps = new();
    private readonly string _stage;
    private readonly string _itemKey;

    private int _sequence;
    private string? _ruleId;
    private StatutoryRuleType? _ruleType;
    private VerificationStatus? _verificationStatus;
    private string? _ruleSource;
    private DateOnly? _ruleEffectiveFrom;
    private decimal? _rawValue;
    private Money? _output;
    private string? _rounding;
    private string? _explanation;
    private Currencies.ConversionRecord? _conversion;

    public TraceEntryBuilder(string stage, string itemKey)
    {
        _stage = stage;
        _itemKey = itemKey;
    }

    public TraceEntryBuilder Sequence(int sequence)
    {
        _sequence = sequence;
        return this;
    }

    /// <summary>Records the rule this figure came from, including its verification grade.</summary>
    public TraceEntryBuilder FromRule(StatutoryRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _ruleId = rule.RuleId;
        _ruleType = rule.RuleType;
        _verificationStatus = rule.VerificationStatus;
        _ruleSource = rule.Source?.ToString();
        _ruleEffectiveFrom = rule.EffectiveFrom;
        return this;
    }

    public TraceEntryBuilder Input(string name, string value)
    {
        _inputs[name] = value;
        return this;
    }

    public TraceEntryBuilder Input(string name, Money value) => Input(name, value.ToString());

    public TraceEntryBuilder Step(string description, decimal? value = null)
    {
        _steps.Add(new TraceStep(description, value));
        return this;
    }

    public TraceEntryBuilder Raw(decimal value)
    {
        _rawValue = value;
        return this;
    }

    public TraceEntryBuilder Rounded(Money output, string roundingApplied)
    {
        _output = output;
        _rounding = roundingApplied;
        return this;
    }

    public TraceEntryBuilder Result(Money output)
    {
        _output = output;
        return this;
    }

    public TraceEntryBuilder Converted(Currencies.ConversionRecord conversion)
    {
        _conversion = conversion;
        return this;
    }

    public TraceEntryBuilder Explain(string explanation)
    {
        _explanation = explanation;
        return this;
    }

    public TraceEntry Build() => new()
    {
        Stage = _stage,
        Sequence = _sequence,
        ItemKey = _itemKey,
        RuleId = _ruleId,
        RuleType = _ruleType,
        VerificationStatus = _verificationStatus,
        RuleSource = _ruleSource,
        RuleEffectiveFrom = _ruleEffectiveFrom,
        Inputs = _inputs,
        Steps = _steps,
        RawValue = _rawValue,
        Output = _output,
        RoundingApplied = _rounding,
        Conversion = _conversion,
        Explanation = _explanation
    };
}
