using Tawaka.Domain.Statutory;

namespace Tawaka.Application.Statutory;

public enum RuleResolutionFailureReason
{
    /// <summary>No rule of this type exists for the currency, period basis and date.</summary>
    NotFound = 0,

    /// <summary>A rule exists but its verification grade is not usable in live payroll.</summary>
    NotVerifiedForLiveMode = 1,

    /// <summary>More than one active rule matches — a configuration error, never resolved silently.</summary>
    Ambiguous = 2,

    /// <summary>The matching rule has been deliberately disabled.</summary>
    Disabled = 3,

    /// <summary>Rule found, but a required precondition on it is unset (e.g. ceiling application method).</summary>
    IncompleteConfiguration = 4
}

/// <summary>
/// Why a rule could not be used, in terms the user can act on. Carries the offending rule where
/// one was found, so the block message can name it.
/// </summary>
public sealed record RuleResolutionFailure(
    RuleResolutionFailureReason Reason,
    StatutoryRuleType RuleType,
    string QueryDescription,
    string Message,
    string? RuleId = null,
    string? RuleName = null,
    VerificationStatus? VerificationStatus = null,
    string? Remedy = null);

/// <summary>
/// The outcome of resolving a rule. Either a rule, or a failure — never a fallback. The engine
/// must not substitute an unverified rule for a missing verified one, so there is no third case.
/// </summary>
public sealed class RuleResolution<TRule> where TRule : StatutoryRule
{
    private RuleResolution(TRule? rule, RuleResolutionFailure? failure)
    {
        Rule = rule;
        Failure = failure;
    }

    public TRule? Rule { get; }

    public RuleResolutionFailure? Failure { get; }

    public bool Succeeded => Rule is not null;

    public static RuleResolution<TRule> Success(TRule rule) => new(rule, null);

    public static RuleResolution<TRule> Failed(RuleResolutionFailure failure) => new(null, failure);

    /// <summary>Returns the rule or throws. Used where the caller has already checked the gate.</summary>
    public TRule Require() => Rule ?? throw new StatutoryRuleUnavailableException(Failure!);
}

public sealed class StatutoryRuleUnavailableException : InvalidOperationException
{
    public StatutoryRuleUnavailableException(RuleResolutionFailure failure)
        : base(failure.Message)
    {
        Failure = failure;
    }

    public RuleResolutionFailure Failure { get; }
}
