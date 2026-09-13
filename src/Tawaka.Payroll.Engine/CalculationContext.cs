using Tawaka.Domain.Common;
using Tawaka.Payroll.Engine.Inputs;
using Tawaka.Payroll.Engine.Results;
using Tawaka.Payroll.Engine.Rounding;
using Tawaka.Payroll.Engine.Tracing;

namespace Tawaka.Payroll.Engine;

/// <summary>An earning as it accumulates through the pipeline.</summary>
internal sealed class EarningAccumulator
{
    public required EarningInput Input { get; init; }
    public Money? TaxableAmount { get; set; }
    public Money? ExemptAmount { get; set; }
}

/// <summary>
/// Carries state between pipeline stages and assembles the final result.
/// <para>
/// Internal by design: it is mutable while the pipeline runs, and the only thing that escapes the
/// engine is the immutable <see cref="PayrollResult"/> it builds.
/// </para>
/// </summary>
internal sealed class CalculationContext
{
    private readonly List<EarningAccumulator> _earnings = new();
    private readonly List<DeductionLineResult> _deductions = new();
    private readonly List<EmployerCostResult> _employerCosts = new();
    private readonly List<CostAllocationResult> _allocations = new();
    private readonly List<UnresolvedItem> _unresolved = new();
    private readonly List<CalculationWarning> _warnings = new();
    private readonly CalculationTrace _trace = new();
    private readonly Dictionary<string, string> _ruleSnapshot = new();
    private int _sequence;

    public CalculationContext(PayrollInputSnapshot snapshot)
    {
        Snapshot = snapshot;
        RecordRuleSnapshot();
    }

    public PayrollInputSnapshot Snapshot { get; }

    public CurrencyCode Currency => Snapshot.PayrollCurrency;

    public RoundingPolicy Rounding => Snapshot.RoundingPolicy;

    public IReadOnlyList<EarningAccumulator> Earnings => _earnings;

    public IReadOnlyList<DeductionLineResult> Deductions => _deductions;

    public IReadOnlyList<EmployerCostResult> EmployerCosts => _employerCosts;

    public bool HasUnresolved => _unresolved.Count > 0;

    public Money? GrossEarnings { get; set; }
    public Money? TaxableIncome { get; set; }
    public Money? NssaInsurableEarnings { get; set; }
    public Money? NssaEmployee { get; set; }
    public Money? NssaEmployer { get; set; }
    public Money? PayeBeforeCredits { get; set; }
    public Money? TaxCredits { get; set; }
    public Money? PayeAfterCredits { get; set; }
    public Money? AidsLevy { get; set; }
    public Money? Apwcs { get; set; }
    public Money? TotalStatutoryDeductions { get; set; }
    public Money? TotalOtherDeductions { get; set; }
    public Money? TotalDeductions { get; set; }
    public Money? NetPay { get; set; }
    public Money? TotalEmployerCost { get; set; }

    public Money Round(Money value) => Rounding.Round(value);

    public void AddEarning(EarningInput earning) =>
        _earnings.Add(new EarningAccumulator { Input = earning });

    public void AddDeduction(DeductionInput deduction) =>
        _deductions.Add(new DeductionLineResult(
            deduction.Code, deduction.Name, deduction.Amount, false, deduction.ReducesTaxableIncome));

    public void AddStatutoryDeduction(
        string code, string name, Money amount, bool reducesTaxableIncome) =>
        _deductions.Add(new DeductionLineResult(code, name, amount, true, reducesTaxableIncome));

    public void AddEmployerCost(
        string code, string name, Money amount, Money baseAmount, decimal? rate, string? ruleId) =>
        _employerCosts.Add(new EmployerCostResult(code, name, amount, baseAmount, rate, ruleId));

    public void AddAllocation(CostAllocationResult allocation) => _allocations.Add(allocation);

    public void Unresolve(UnresolvedItem item) => _unresolved.Add(item);

    public void Warn(CalculationWarning warning) => _warnings.Add(warning);

    public void Trace(TraceEntry entry)
    {
        _trace.Add(entry with { Sequence = _sequence++ });
    }

    public PayrollResult Build() => new()
    {
        EmployeeId = Snapshot.EmployeeId,
        EmployeeNumber = Snapshot.EmployeeNumber,
        EmployeeName = Snapshot.EmployeeName,
        PayrollPeriodId = Snapshot.PayrollPeriodId,
        ContractId = Snapshot.ContractId,
        ContractVersion = Snapshot.ContractVersion,
        Currency = Currency,
        Mode = Snapshot.Mode,
        Earnings = _earnings.Select(e => new EarningLineResult(
            e.Input.Code, e.Input.Name, e.Input.Amount, e.Input.Quantity, e.Input.Rate,
            e.TaxableAmount ?? (e.Input.IsTaxable ? e.Input.Amount : Money.Zero(Currency)),
            e.ExemptAmount ?? Money.Zero(Currency),
            e.Input.IsNssaApplicable ? e.Input.Amount : Money.Zero(Currency),
            e.Input.IsIncludedInGross)).ToList(),
        Deductions = _deductions,
        EmployerCosts = _employerCosts,
        CostAllocations = _allocations,
        GrossEarnings = GrossEarnings,
        TaxableIncome = TaxableIncome,
        NssaInsurableEarnings = NssaInsurableEarnings,
        NssaEmployee = NssaEmployee,
        PayeBeforeCredits = PayeBeforeCredits,
        TaxCredits = TaxCredits,
        PayeAfterCredits = PayeAfterCredits,
        AidsLevy = AidsLevy,
        TotalStatutoryDeductions = TotalStatutoryDeductions,
        TotalOtherDeductions = TotalOtherDeductions,
        TotalDeductions = TotalDeductions,
        NetPay = NetPay,
        NssaEmployer = NssaEmployer,
        Apwcs = Apwcs,
        TotalEmployerCost = TotalEmployerCost,
        Unresolved = _unresolved,
        Warnings = _warnings,
        Trace = _trace,
        RuleSnapshot = _ruleSnapshot,
        EngineVersion = PayrollCalculator.Version
    };

    /// <summary>
    /// Records which rule version produced this result, so the run can be reproduced even after
    /// the rules are later corrected.
    /// </summary>
    private void RecordRuleSnapshot()
    {
        void Record(string key, Domain.Statutory.StatutoryRule? rule)
        {
            if (rule is not null)
            {
                _ruleSnapshot[key] = $"{rule.RuleId} ({rule.VerificationStatus}, from {rule.EffectiveFrom:yyyy-MM-dd})";
            }
        }

        Record("PayeTable", Snapshot.Rules.PayeTable);
        Record("AidsLevy", Snapshot.Rules.AidsLevy);
        Record("Nssa", Snapshot.Rules.Nssa);
        Record("NssaEligibility", Snapshot.Rules.NssaEligibility);
        Record("Apwcs", Snapshot.Rules.Apwcs);
        Record("BonusExemption", Snapshot.Rules.BonusExemption);
        Record("CurrencyStrategy", Snapshot.Rules.CurrencyStrategy);

        foreach (var credit in Snapshot.Rules.TaxCredits)
        {
            Record($"TaxCredit:{credit.CreditType}", credit);
        }

        foreach (var levy in Snapshot.Rules.EmployerLevies)
        {
            Record($"Levy:{levy.LevyType}", levy);
        }

        foreach (var (key, conversion) in Snapshot.ExchangeRates)
        {
            _ruleSnapshot[$"Rate:{key}"] =
                $"{conversion.Rate} on {conversion.RateDate:yyyy-MM-dd} ({conversion.RateSource})";
        }
    }
}
