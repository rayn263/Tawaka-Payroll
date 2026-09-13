using Tawaka.Application.Abstractions;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;

namespace Tawaka.Application.Statutory;

public interface IStatutoryRuleResolver
{
    RuleResolution<TRule> Resolve<TRule>(StatutoryRuleQuery query) where TRule : StatutoryRule;
}

/// <summary>
/// Resolves statutory rules by type, currency, period basis, effective date and employee
/// circumstances.
/// <para>
/// The central guarantee (ADR-012): this resolver never substitutes. If the rule that matches the
/// query is not usable in the requested mode, it fails and names that rule — it does not fall back
/// to an older verified version, a different currency, or a lower-graded rule. A silently
/// substituted rule produces a plausible wrong number, which is the worst possible outcome for a
/// payroll system.
/// </para>
/// </summary>
public sealed class StatutoryRuleResolver : IStatutoryRuleResolver
{
    private readonly IStatutoryRuleSource _source;

    public StatutoryRuleResolver(IStatutoryRuleSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public RuleResolution<TRule> Resolve<TRule>(StatutoryRuleQuery query) where TRule : StatutoryRule
    {
        ArgumentNullException.ThrowIfNull(query);

        var candidates = _source.GetRules(query.RuleType)
            .OfType<TRule>()
            .Where(rule => rule.AppliesOn(query.EffectiveDate))
            .Where(rule => rule.AppliesTo(query.Currency))
            .Where(rule => MatchesPeriodBasis(rule, query))
            .Where(rule => MatchesDiscriminator(rule, query))
            .Where(rule => MatchesEmployee(rule, query))
            .ToList();

        if (candidates.Count == 0)
        {
            return RuleResolution<TRule>.Failed(NotFound(query));
        }

        if (candidates.Count > 1)
        {
            return RuleResolution<TRule>.Failed(Ambiguous(query, candidates));
        }

        var match = candidates[0];

        if (match.VerificationStatus == VerificationStatus.Disabled)
        {
            return RuleResolution<TRule>.Failed(new RuleResolutionFailure(
                RuleResolutionFailureReason.Disabled,
                query.RuleType,
                query.Describe(),
                $"Statutory rule '{match.RuleId}' is disabled and cannot be used.",
                match.RuleId,
                match.Name,
                match.VerificationStatus,
                "Enable the rule in Settings, or supersede it with an active version."));
        }

        var usable = query.Mode == PayrollMode.Live
            ? match.VerificationStatus.IsUsableInLivePayroll()
            : match.VerificationStatus.IsUsableInDevelopment();

        if (!usable)
        {
            return RuleResolution<TRule>.Failed(new RuleResolutionFailure(
                RuleResolutionFailureReason.NotVerifiedForLiveMode,
                query.RuleType,
                query.Describe(),
                "This statutory rule has not been verified and cannot be used for live payroll.",
                match.RuleId,
                match.Name,
                match.VerificationStatus,
                BuildRemedy(match)));
        }

        var incomplete = DescribeIncompleteConfiguration(match);
        if (incomplete is not null)
        {
            return RuleResolution<TRule>.Failed(new RuleResolutionFailure(
                RuleResolutionFailureReason.IncompleteConfiguration,
                query.RuleType,
                query.Describe(),
                incomplete.Value.Message,
                match.RuleId,
                match.Name,
                match.VerificationStatus,
                incomplete.Value.Remedy));
        }

        return RuleResolution<TRule>.Success(match);
    }

    private static bool MatchesPeriodBasis<TRule>(TRule rule, StatutoryRuleQuery query)
        where TRule : StatutoryRule
    {
        // A PAYE table is only valid for the pay frequency it was published for. Weekly payroll
        // never falls back to the monthly table (ADR-013).
        if (rule is TaxRule taxRule && query.PeriodBasis.HasValue)
        {
            return taxRule.PeriodBasis == query.PeriodBasis.Value;
        }

        return true;
    }

    private static bool MatchesDiscriminator<TRule>(TRule rule, StatutoryRuleQuery query)
        where TRule : StatutoryRule
    {
        if (string.IsNullOrWhiteSpace(query.Discriminator))
        {
            return true;
        }

        return rule switch
        {
            EmployerLevyRule levy =>
                string.Equals(levy.LevyType.ToString(), query.Discriminator, StringComparison.OrdinalIgnoreCase),
            TaxCreditRule credit =>
                string.Equals(credit.CreditType.ToString(), query.Discriminator, StringComparison.OrdinalIgnoreCase),
            TaxExemptionRule exemption =>
                string.Equals(exemption.ExemptionType.ToString(), query.Discriminator, StringComparison.OrdinalIgnoreCase),
            NssaEligibilityRule eligibility =>
                string.Equals(eligibility.EmploymentTypeCode, query.Discriminator, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static bool MatchesEmployee<TRule>(TRule rule, StatutoryRuleQuery query)
        where TRule : StatutoryRule
    {
        if (query.Employee is null)
        {
            return true;
        }

        return rule switch
        {
            NssaEligibilityRule eligibility when query.Employee.EmploymentTypeCode is { } code =>
                string.Equals(eligibility.EmploymentTypeCode, code, StringComparison.OrdinalIgnoreCase),
            ApwcsRule apwcs when query.Employee.IndustryCode is { } industry =>
                string.Equals(apwcs.IndustryCode, industry, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    /// <summary>
    /// Some rules can be Verified in their values yet still carry an undetermined application
    /// method. Those block too — a verified ceiling with no rule for applying it to weekly payroll
    /// is not a usable rule.
    /// </summary>
    private static (string Message, string Remedy)? DescribeIncompleteConfiguration(StatutoryRule rule) =>
        rule switch
        {
            // The NSSA ceiling application method is deliberately NOT checked here. Whether it
            // matters depends on the pay frequency, which this resolver does not know: a monthly
            // ceiling applied to a monthly payroll needs no conversion rule at all. The engine
            // makes that judgement, because it knows the period basis (compliance spec Q22).
            CurrencyTaxStrategyRule strategy when
                strategy.Strategy != CurrencyTaxStrategy.SingleCurrency && !strategy.ApprovedByAdvisor =>
                ($"Multi-currency tax strategy '{strategy.RuleId}' has not been approved by a tax advisor.",
                 "Resolve compliance question Q1 and record the advisor approval in " +
                 "Settings > Tax Configuration."),
            CurrencyTaxStrategyRule strategy when
                strategy.Strategy != CurrencyTaxStrategy.SingleCurrency &&
                strategy.RateDetermination == Domain.Currencies.RateDeterminationRule.NotDetermined =>
                ($"Multi-currency tax strategy '{strategy.RuleId}' does not specify which exchange " +
                 "rate date applies.",
                 "Set the rate determination rule in Settings > Currencies & Exchange Rates."),
            _ => NoIssue
        };

    private static readonly (string Message, string Remedy)? NoIssue = null;

    private static RuleResolutionFailure NotFound(StatutoryRuleQuery query) => new(
        RuleResolutionFailureReason.NotFound,
        query.RuleType,
        query.Describe(),
        $"No statutory rule is configured for {query.Describe()}.",
        Remedy: query.PeriodBasis.HasValue
            ? $"Load the official {query.PeriodBasis} {query.RuleType} table for " +
              $"{query.Currency?.Value ?? "this currency"}. Tables are never derived from another " +
              "period basis."
            : "Configure the rule, with its source and effective dates, in Settings.");

    private static RuleResolutionFailure Ambiguous<TRule>(
        StatutoryRuleQuery query, IReadOnlyCollection<TRule> candidates) where TRule : StatutoryRule
        => new(
            RuleResolutionFailureReason.Ambiguous,
            query.RuleType,
            query.Describe(),
            $"{candidates.Count} active statutory rules match {query.Describe()}: " +
            string.Join(", ", candidates.Select(c => c.RuleId)) +
            ". Overlapping effective periods are a configuration error.",
            Remedy: "Close off the superseded rule by setting its effective-to date.");

    private static string BuildRemedy(StatutoryRule rule) =>
        $"Confirm '{rule.Name}' against the official source" +
        (string.IsNullOrWhiteSpace(rule.Source?.Source) ? string.Empty : $" ({rule.Source!.Source})") +
        ", then record the source and date and set the rule to Verified in Settings.";
}
