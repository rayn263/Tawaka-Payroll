using Tawaka.Domain.Common;
using Tawaka.Domain.Payroll;
using Tawaka.Payroll.Engine.Tracing;

namespace Tawaka.Payroll.Engine.Results;

public enum PayrollResultStatus
{
    /// <summary>Every required figure was produced.</summary>
    Calculated = 0,

    /// <summary>One or more figures could not be produced; see <see cref="PayrollResult.Unresolved"/>.</summary>
    Blocked = 1
}

public sealed record EarningLineResult(
    string Code, string Name, Money Amount, decimal? Quantity, Money? Rate,
    Money TaxableAmount, Money ExemptAmount, Money NssaApplicableAmount,
    bool IsIncludedInGross, Currencies.ConversionRecord? Conversion = null);

public sealed record DeductionLineResult(
    string Code, string Name, Money Amount, bool IsStatutory, bool ReducesTaxableIncome);

public sealed record EmployerCostResult(
    string Code, string Name, Money Amount, Money BaseAmount, decimal? RateApplied, string? RuleId);

public sealed record CostAllocationResult(
    Guid? ProjectId, string? ProjectName, Guid? DepartmentId, string? DepartmentName,
    decimal Percent, Money AllocatedCost);

/// <summary>
/// The outcome of calculating one employee's pay. Immutable: once produced it is written to the
/// payroll run and never edited — a correction is a new calculation, not a mutation.
/// <para>
/// Money fields are nullable on purpose. A null is not zero: it means the figure could not be
/// produced, and the reason is in <see cref="Unresolved"/>. A legitimate zero is
/// <c>Money.Zero</c>, and appears on the payslip as 0.00.
/// </para>
/// </summary>
public sealed record PayrollResult
{
    public required Guid EmployeeId { get; init; }
    public required string EmployeeNumber { get; init; }
    public required string EmployeeName { get; init; }
    public required Guid PayrollPeriodId { get; init; }
    public required Guid ContractId { get; init; }
    public required int ContractVersion { get; init; }
    public required CurrencyCode Currency { get; init; }
    public required PayrollMode Mode { get; init; }

    public PayrollResultStatus Status =>
        Unresolved.Count > 0 ? PayrollResultStatus.Blocked : PayrollResultStatus.Calculated;

    /// <summary>True when this result may be used for a live payroll.</summary>
    public bool IsUsableForLivePayroll => Status == PayrollResultStatus.Calculated &&
                                          Mode == PayrollMode.Live;

    public IReadOnlyList<EarningLineResult> Earnings { get; init; } = Array.Empty<EarningLineResult>();
    public IReadOnlyList<DeductionLineResult> Deductions { get; init; } = Array.Empty<DeductionLineResult>();
    public IReadOnlyList<EmployerCostResult> EmployerCosts { get; init; } = Array.Empty<EmployerCostResult>();
    public IReadOnlyList<CostAllocationResult> CostAllocations { get; init; } = Array.Empty<CostAllocationResult>();

    public Money? GrossEarnings { get; init; }
    public Money? TaxableIncome { get; init; }
    public Money? NssaInsurableEarnings { get; init; }
    public Money? NssaEmployee { get; init; }
    public Money? PayeBeforeCredits { get; init; }
    public Money? TaxCredits { get; init; }
    public Money? PayeAfterCredits { get; init; }
    public Money? AidsLevy { get; init; }
    public Money? TotalStatutoryDeductions { get; init; }
    public Money? TotalOtherDeductions { get; init; }
    public Money? TotalDeductions { get; init; }
    public Money? NetPay { get; init; }

    public Money? NssaEmployer { get; init; }
    public Money? Apwcs { get; init; }
    public Money? TotalEmployerCost { get; init; }

    public IReadOnlyList<UnresolvedItem> Unresolved { get; init; } = Array.Empty<UnresolvedItem>();
    public IReadOnlyList<CalculationWarning> Warnings { get; init; } = Array.Empty<CalculationWarning>();

    public required CalculationTrace Trace { get; init; }

    /// <summary>Rule versions used, for the run's immutable snapshot.</summary>
    public IReadOnlyDictionary<string, string> RuleSnapshot { get; init; } =
        new Dictionary<string, string>();

    public required string EngineVersion { get; init; }
}
