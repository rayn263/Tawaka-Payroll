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

    /// <summary>
    /// The snapshot, with anything the engine derived from approved time, leave and loans folded
    /// in. Stages after the first read this rather than the raw snapshot, so a derived overtime
    /// line and a captured allowance are treated identically from that point on.
    /// </summary>
    public PayrollInputSnapshot Snapshot { get; private set; }

    /// <summary>
    /// Folds engine-derived earnings and deductions into the snapshot. This is the only mutation
    /// of the snapshot anywhere, it happens in the first stage before any figure is computed, and
    /// it produces a new record rather than editing the caller's — the snapshot the caller holds
    /// is unchanged, which is what keeps it reproducible.
    /// </summary>
    public void AddDerivedInputs(
        IReadOnlyList<EarningInput> earnings, IReadOnlyList<DeductionInput> deductions)
    {
        if (earnings.Count == 0 && deductions.Count == 0)
        {
            return;
        }

        Snapshot = Snapshot with
        {
            Earnings = Snapshot.Earnings.Concat(earnings).ToList(),
            Deductions = Snapshot.Deductions.Concat(deductions).ToList()
        };
    }

    public CurrencyCode Currency => Snapshot.PayrollCurrency;

    public RoundingPolicy Rounding => Snapshot.RoundingPolicy;

    public IReadOnlyList<EarningAccumulator> Earnings => _earnings;

    public IReadOnlyList<DeductionLineResult> Deductions => _deductions;

    public IReadOnlyList<EmployerCostResult> EmployerCosts => _employerCosts;

    public bool HasUnresolved => _unresolved.Count > 0;

    /// <summary>
    /// True when a component of pay itself could not be produced — missing approved time, an
    /// overtime rate that has not been established, a contract with no rate.
    /// <para>
    /// Gross earnings must then be absent rather than a total of the parts that happened to work.
    /// A gross that silently excludes unpriced overtime is a wrong number presented as a right one,
    /// which is precisely what this system exists not to do.
    /// </para>
    /// </summary>
    public bool HasUnresolvedEarnings => _unresolved.Any(u =>
        u.Code is UnresolvedCodes.TimesheetMissing or UnresolvedCodes.TimesheetNotApproved
            or UnresolvedCodes.OvertimeMultiplierUnresolved or UnresolvedCodes.OvertimeRuleUnresolved
            or UnresolvedCodes.RateMissing or UnresolvedCodes.CurrencyStrategyUnresolved);

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
        Record("PayDivisor", Snapshot.Rules.PayDivisor);

        foreach (var overtime in Snapshot.Rules.Overtime)
        {
            Record($"Overtime:{overtime.CategoryCode}", overtime);
        }

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
