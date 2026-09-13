using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Inputs;
using Tawaka.Payroll.Engine.Results;
using Tawaka.Payroll.Engine.Rounding;
using Tawaka.Payroll.Engine.Tracing;

namespace Tawaka.Payroll.Engine.Stages;

public sealed record NssaComputation(
    Money? InsurableEarnings,
    Money? EmployeeContribution,
    Money? EmployerContribution,
    UnresolvedItem? Unresolved,
    IReadOnlyList<TraceEntry> Trace,
    IReadOnlyList<CalculationWarning> Warnings);

/// <summary>
/// NSSA Pension and Other Benefits Scheme.
/// <para>
/// Deliberately a separate stage that does <b>not</b> operate on gross earnings. Insurable
/// earnings are the earnings flagged NSSA-applicable — basic salary under the seeded rule, with
/// overtime and bonuses excluded. An engine that applied 4.5% to gross would over-deduct from
/// exactly the site staff who work the most overtime.
/// </para>
/// </summary>
public sealed class NssaCalculator
{
    private readonly RoundingPolicy _rounding;

    public NssaCalculator(RoundingPolicy rounding) => _rounding = rounding;

    public NssaComputation Compute(PayrollInputSnapshot snapshot)
    {
        var traces = new List<TraceEntry>();
        var warnings = new List<CalculationWarning>();
        var currency = snapshot.PayrollCurrency;

        var rule = snapshot.Rules.Nssa;
        if (rule is null)
        {
            return Blocked(snapshot, traces, warnings, UnresolvedCodes.NssaRuleUnresolved,
                "No NSSA rule is available for this currency and pay date.",
                StatutoryRuleType.NssaPobs,
                "Configure the NSSA contribution rule in Settings > NSSA Configuration.");
        }

        // ---- Eligibility -------------------------------------------------------------------
        var eligibility = DetermineEligibility(snapshot, rule, traces, warnings);
        if (eligibility.Unresolved is not null)
        {
            return new NssaComputation(null, null, null, eligibility.Unresolved, traces, warnings);
        }

        if (!eligibility.IsEligible)
        {
            // A legitimate zero, not an unresolved figure: the rule exists and says nil.
            var zero = Money.Zero(currency);
            traces.Add(new TraceEntryBuilder("ComputeNssa", "NSSA employee")
                .FromRule(rule)
                .Explain(eligibility.Reason!)
                .Result(zero)
                .Build());
            return new NssaComputation(zero, zero, zero, null, traces, warnings);
        }

        // ---- Insurable earnings ------------------------------------------------------------
        var applicable = snapshot.Earnings
            .Where(e => e.IsNssaApplicable && e.Amount.Currency == currency)
            .ToList();

        var insurableBeforeCeiling = Money.Sum(currency, applicable.Select(e => e.Amount));

        var insurableTrace = new TraceEntryBuilder("ComputeNssa", "NSSA insurable earnings")
            .FromRule(rule)
            .Input("Earnings basis", rule.EarningsBasis.ToString());
        foreach (var earning in applicable)
        {
            insurableTrace.Step($"{earning.Name}", earning.Amount.Amount);
        }

        var excluded = snapshot.Earnings.Where(e => !e.IsNssaApplicable && e.IsIncludedInGross).ToList();
        if (excluded.Count > 0)
        {
            insurableTrace.Input("Excluded from insurable earnings",
                string.Join(", ", excluded.Select(e => e.Name)));
        }

        // ---- Ceiling -----------------------------------------------------------------------
        var ceiling = ResolveCeiling(snapshot, rule, currency);
        if (ceiling.Unresolved is not null)
        {
            insurableTrace.Explain(ceiling.Unresolved.Message);
            traces.Add(insurableTrace.Build());
            return new NssaComputation(null, null, null, ceiling.Unresolved, traces, warnings);
        }

        var insurable = ceiling.Amount is { } cap && insurableBeforeCeiling > cap
            ? cap
            : insurableBeforeCeiling;

        if (ceiling.Amount is { } capped)
        {
            insurableTrace.Input("Ceiling", capped);
            if (insurableBeforeCeiling > capped)
            {
                insurableTrace.Step(
                    $"Earnings {insurableBeforeCeiling.Amount:N2} exceed the ceiling; capped",
                    capped.Amount);
            }

            if (ceiling.Description is not null)
            {
                insurableTrace.Input("Ceiling basis", ceiling.Description);
            }
        }

        insurableTrace.Result(insurable)
            .Explain("Insurable earnings are the NSSA-applicable earnings, capped at the ceiling.");
        traces.Add(insurableTrace.Build());

        // ---- Contributions -----------------------------------------------------------------
        var employee = Apply(rule.EmployeeRate, insurable, "NSSA employee", rule, traces);
        var employer = Apply(rule.EmployerRate, insurable, "NSSA employer", rule, traces);

        return new NssaComputation(insurable, employee, employer, null, traces, warnings);
    }

    private Money Apply(
        decimal rate, Money insurable, string itemKey, NssaRule rule, List<TraceEntry> traces)
    {
        var raw = insurable.Amount * rate;
        var amount = new Money(
            _rounding.RoundStatutoryContributions ? _rounding.Round(raw) : raw, insurable.Currency);

        traces.Add(new TraceEntryBuilder("ComputeNssa", itemKey)
            .FromRule(rule)
            .Input("Insurable earnings", insurable)
            .Step($"{insurable.Amount:N2} x {rate * 100m:0.##}%", raw)
            .Raw(raw)
            .Rounded(amount, _rounding.Describe())
            .Build());

        return amount;
    }

    private sealed record EligibilityOutcome(bool IsEligible, string? Reason, UnresolvedItem? Unresolved);

    private static EligibilityOutcome DetermineEligibility(
        PayrollInputSnapshot snapshot, NssaRule rule, List<TraceEntry> traces,
        List<CalculationWarning> warnings)
    {
        // An explicit per-employee override wins, and carries its own audit trail.
        if (snapshot.StatutoryProfile.NssaEligibilityOverride is { } over)
        {
            return new EligibilityOutcome(over,
                over ? "Covered by employee-level override." : "Excluded by employee-level override.",
                null);
        }

        var eligibility = snapshot.Rules.NssaEligibility;
        if (eligibility is null)
        {
            return new EligibilityOutcome(false, null, new UnresolvedItem(
                UnresolvedCodes.NssaEligibilityUnresolved, "NSSA employee",
                $"No NSSA eligibility rule is available for employment type " +
                $"'{snapshot.EmploymentTypeCode}'.",
                StatutoryRuleType.NssaEligibility, null, null, "Q5",
                "Configure and verify the eligibility rule for this employment type."));
        }

        if (!eligibility.IsEligible)
        {
            return new EligibilityOutcome(false,
                $"Employment type '{snapshot.EmploymentTypeCode}' is not covered by the scheme.", null);
        }

        var age = snapshot.StatutoryProfile.Age;
        if (age is not null)
        {
            if (age < rule.MinimumAge)
            {
                return new EligibilityOutcome(false,
                    $"Employee is under the minimum contributory age of {rule.MinimumAge}.", null);
            }

            if (rule.MaximumAge is { } maximum && age > maximum)
            {
                return new EligibilityOutcome(false,
                    $"Contributions cease above age {maximum}.", null);
            }
        }
        else
        {
            warnings.Add(new CalculationWarning("NSSA_AGE_UNKNOWN", "NSSA employee",
                "No date of birth is recorded, so the age bounds could not be applied."));
        }

        // The casual test is expressed in days engaged within the month.
        if (rule.MinimumDaysInMonth is { } minimumDays && snapshot.Timesheet is { } timesheet &&
            timesheet.DaysEngagedInMonth > 0m && timesheet.DaysEngagedInMonth < minimumDays)
        {
            return new EligibilityOutcome(false,
                $"Engaged for {timesheet.DaysEngagedInMonth:N0} days in the month, below the " +
                $"{minimumDays}-day threshold for contributions.", null);
        }

        return new EligibilityOutcome(true, null, null);
    }

    private sealed record CeilingOutcome(Money? Amount, string? Description, UnresolvedItem? Unresolved);

    /// <summary>
    /// Resolves the ceiling for this pay frequency.
    /// <para>
    /// The published ceiling is monthly. Applying a monthly ceiling to a weekly run without a rule
    /// for how it converts would understate contributions roughly fourfold, so where the periods
    /// differ and the application method is undetermined the engine refuses (compliance spec Q22).
    /// </para>
    /// </summary>
    private static CeilingOutcome ResolveCeiling(
        PayrollInputSnapshot snapshot, NssaRule rule, CurrencyCode currency)
    {
        var ceiling = new Money(rule.CeilingAmount, currency);

        if (rule.CeilingPeriodBasis == snapshot.PeriodBasis)
        {
            return new CeilingOutcome(ceiling,
                $"{rule.CeilingPeriodBasis} ceiling applied directly to a " +
                $"{snapshot.PeriodBasis.ToString().ToLowerInvariant()} period.", null);
        }

        switch (rule.CeilingApplication)
        {
            case CeilingApplicationMethod.ProRataByPeriodLength:
                var factor = ProRataFactor(rule.CeilingPeriodBasis, snapshot.PeriodBasis);
                if (factor is null)
                {
                    break;
                }

                return new CeilingOutcome(
                    new Money(ceiling.Amount * factor.Value, currency),
                    $"{rule.CeilingPeriodBasis} ceiling pro-rated to {snapshot.PeriodBasis} " +
                    $"(factor {factor.Value:0.######}).", null);

            case CeilingApplicationMethod.PerPayRun:
                return new CeilingOutcome(ceiling,
                    "Ceiling applied in full to each pay run.", null);

            case CeilingApplicationMethod.MonthlyAccumulation:
                return new CeilingOutcome(null, null, new UnresolvedItem(
                    UnresolvedCodes.NssaCeilingApplicationUnresolved, "NSSA insurable earnings",
                    "The NSSA ceiling is configured to accumulate across the calendar month, which " +
                    "requires month-to-date contribution history. That is not yet available.",
                    StatutoryRuleType.NssaPobs, rule.RuleId, rule.VerificationStatus, "Q22",
                    "Use pro-rata application, or wait for month-to-date accumulation support."));
        }

        return new CeilingOutcome(null, null, new UnresolvedItem(
            UnresolvedCodes.NssaCeilingApplicationUnresolved, "NSSA insurable earnings",
            $"The NSSA ceiling is expressed per {rule.CeilingPeriodBasis.ToString().ToLowerInvariant()} " +
            $"but this payroll is {snapshot.PeriodBasis.ToString().ToLowerInvariant()}, and no rule " +
            "says how the ceiling converts between them.",
            StatutoryRuleType.NssaPobs, rule.RuleId, rule.VerificationStatus, "Q22",
            "Resolve compliance question Q22 and set the ceiling application method in " +
            "Settings > NSSA Configuration. Guessing here would misstate contributions for every " +
            "weekly-paid employee."));
    }

    private static decimal? ProRataFactor(PeriodBasis from, PeriodBasis to)
    {
        var perYear = PeriodsPerYear(from);
        var toPerYear = PeriodsPerYear(to);
        if (perYear is null || toPerYear is null)
        {
            return null;
        }

        return perYear.Value / toPerYear.Value;
    }

    private static decimal? PeriodsPerYear(PeriodBasis basis) => basis switch
    {
        PeriodBasis.Annual => 1m,
        PeriodBasis.Monthly => 12m,
        PeriodBasis.Fortnightly => 26m,
        PeriodBasis.Weekly => 52m,
        PeriodBasis.Daily => 365m,
        _ => null
    };

    private static NssaComputation Blocked(
        PayrollInputSnapshot snapshot, List<TraceEntry> traces, List<CalculationWarning> warnings,
        string code, string message, StatutoryRuleType ruleType, string remedy)
    {
        var unresolvedFromSnapshot = snapshot.Rules.FindUnresolved(ruleType);
        return new NssaComputation(null, null, null,
            unresolvedFromSnapshot ??
            new UnresolvedItem(code, "NSSA employee", message, ruleType, Remedy: remedy),
            traces, warnings);
    }
}
