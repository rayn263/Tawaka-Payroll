using System.Text;
using Tawaka.Domain.Payroll;

namespace Tawaka.Application.Statutory;

/// <summary>One rule standing between the payroll and live status.</summary>
public sealed record BlockingRule(
    string Description,
    string? RuleId,
    string? RuleName,
    string Reason,
    string? Remedy);

/// <summary>
/// The result of checking a payroll against the verification gate. Either clear, or a report that
/// names every offending rule — the user is never told merely that "something" is unverified.
/// </summary>
public sealed class LivePayrollBlockReport
{
    private readonly List<BlockingRule> _blockingRules = new();

    public IReadOnlyList<BlockingRule> BlockingRules => _blockingRules;

    public bool IsBlocked => _blockingRules.Count > 0;

    public void Add(RuleResolutionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _blockingRules.Add(new BlockingRule(
            failure.QueryDescription,
            failure.RuleId,
            failure.RuleName,
            failure.Message,
            failure.Remedy));
    }

    public const string Headline = "LIVE PAYROLL BLOCKED";

    public const string Summary =
        "One or more statutory rules required for this payroll have not been verified.";

    /// <summary>Renders the full block message, listing the exact rules causing the block.</summary>
    public string Render()
    {
        if (!IsBlocked)
        {
            return "All statutory rules required for this payroll are verified.";
        }

        var builder = new StringBuilder();
        builder.AppendLine(Headline);
        builder.AppendLine(Summary);
        builder.AppendLine();

        var index = 1;
        foreach (var rule in _blockingRules)
        {
            builder.AppendLine($"{index}. {rule.Description}");
            if (!string.IsNullOrWhiteSpace(rule.RuleId))
            {
                builder.AppendLine($"   Rule:   {rule.RuleId} — {rule.RuleName}");
            }

            builder.AppendLine($"   Reason: {rule.Reason}");
            if (!string.IsNullOrWhiteSpace(rule.Remedy))
            {
                builder.AppendLine($"   To clear: {rule.Remedy}");
            }

            builder.AppendLine();
            index++;
        }

        return builder.ToString().TrimEnd();
    }
}

/// <summary>
/// Checks every statutory rule a payroll needs before it may run in live mode. Resolution
/// failures are collected rather than thrown one at a time, so the user sees the whole list and
/// can clear it in one pass instead of discovering blockers one run at a time.
/// </summary>
public sealed class LivePayrollGate
{
    private readonly IStatutoryRuleResolver _resolver;

    public LivePayrollGate(IStatutoryRuleResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public LivePayrollBlockReport Check(IEnumerable<StatutoryRuleQuery> requiredRules)
    {
        ArgumentNullException.ThrowIfNull(requiredRules);

        var report = new LivePayrollBlockReport();
        foreach (var query in requiredRules)
        {
            var liveQuery = query with { Mode = PayrollMode.Live };
            var resolution = _resolver.Resolve<Domain.Statutory.StatutoryRule>(liveQuery);
            if (!resolution.Succeeded)
            {
                report.Add(resolution.Failure!);
            }
        }

        return report;
    }
}
