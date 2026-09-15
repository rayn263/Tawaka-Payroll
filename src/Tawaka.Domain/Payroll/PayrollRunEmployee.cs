using Tawaka.Domain.Common;

namespace Tawaka.Domain.Payroll;

/// <summary>
/// One employee's result within a run — the payslip header.
/// <para>
/// The whole payslip is denominated in a single currency, so <see cref="CurrencyCode"/> is stored
/// once here and every amount below is expressed in it. Lines that originated in another currency
/// carry their own original amount and conversion provenance. Money values are exposed through the
/// <see cref="Money"/> value object rather than as naked decimals.
/// </para>
/// <para>
/// A null amount is not zero: it means the figure could not be produced, and the reason is in the
/// run's unresolved items.
/// </para>
/// </summary>
public class PayrollRunEmployee : AuditableEntity
{
    public Guid PayrollRunId { get; set; }
    public PayrollRun? PayrollRun { get; set; }

    public Guid EmployeeId { get; set; }
    public Guid EmployeeContractId { get; set; }
    public int ContractVersion { get; set; }

    /// <summary>Snapshotted so later master-data edits cannot rewrite a processed payslip.</summary>
    public string EmployeeNumber { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string EmploymentTypeCode { get; set; } = string.Empty;
    public Guid? DepartmentId { get; set; }
    public Guid? ProjectId { get; set; }

    public string CurrencyCode { get; set; } = "USD";

    public decimal? GrossEarningsAmount { get; set; }
    public decimal? TaxableIncomeAmount { get; set; }
    public decimal? NssaInsurableEarningsAmount { get; set; }
    public decimal? NssaEmployeeAmount { get; set; }
    public decimal? PayeBeforeCreditsAmount { get; set; }
    public decimal? TaxCreditsAmount { get; set; }
    public decimal? PayeAfterCreditsAmount { get; set; }
    public decimal? AidsLevyAmount { get; set; }
    public decimal? TotalStatutoryDeductionsAmount { get; set; }
    public decimal? TotalOtherDeductionsAmount { get; set; }
    public decimal? TotalDeductionsAmount { get; set; }
    public decimal? NetPayAmount { get; set; }
    public decimal? NssaEmployerAmount { get; set; }
    public decimal? ApwcsAmount { get; set; }
    public decimal? TotalEmployerCostAmount { get; set; }

    /// <summary>True when every required figure was produced.</summary>
    public bool IsCalculated { get; set; }

    public bool IsExcluded { get; set; }
    public string? ExclusionReason { get; set; }

    public ICollection<PayrollEarningLine> EarningLines { get; set; } = new List<PayrollEarningLine>();
    public ICollection<PayrollDeductionLine> DeductionLines { get; set; } = new List<PayrollDeductionLine>();
    public ICollection<PayrollEmployerCostLine> EmployerCostLines { get; set; } = new List<PayrollEmployerCostLine>();
    public ICollection<PayrollCalculationTraceEntry> TraceEntries { get; set; } = new List<PayrollCalculationTraceEntry>();
    public ICollection<PayrollUnresolvedItem> UnresolvedItems { get; set; } = new List<PayrollUnresolvedItem>();
    public ICollection<PayrollCostAllocation> CostAllocations { get; set; } = new List<PayrollCostAllocation>();

    public CurrencyCode Currency => new(CurrencyCode);

    public Money? AsMoney(decimal? amount) => amount is null ? null : new Money(amount.Value, Currency);

    public Money? GrossEarnings => AsMoney(GrossEarningsAmount);
    public Money? TaxableIncome => AsMoney(TaxableIncomeAmount);
    public Money? NssaEmployee => AsMoney(NssaEmployeeAmount);
    public Money? PayeAfterCredits => AsMoney(PayeAfterCreditsAmount);
    public Money? AidsLevy => AsMoney(AidsLevyAmount);
    public Money? NetPay => AsMoney(NetPayAmount);
    public Money? TotalEmployerCost => AsMoney(TotalEmployerCostAmount);
}

public class PayrollEarningLine : Entity, IPayrollResultRow
{
    public Guid PayrollRunEmployeeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public decimal? Quantity { get; set; }
    public decimal? RateAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal ExemptAmount { get; set; }
    public decimal NssaApplicableAmount { get; set; }
    public bool IsIncludedInGross { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Where the line originated in another currency, the original is preserved.</summary>
    public decimal? OriginalAmount { get; set; }
    public string? OriginalCurrencyCode { get; set; }
    public decimal? ExchangeRateUsed { get; set; }
    public DateOnly? ExchangeRateDate { get; set; }
    public string? ExchangeRateSource { get; set; }

    public Money AsMoney() => new(Amount, new CurrencyCode(CurrencyCode));
}

public class PayrollDeductionLine : Entity, IPayrollResultRow
{
    public Guid PayrollRunEmployeeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public bool IsStatutory { get; set; }
    public bool ReducesTaxableIncome { get; set; }
    public int DisplayOrder { get; set; }

    public Money AsMoney() => new(Amount, new CurrencyCode(CurrencyCode));
}

public class PayrollEmployerCostLine : Entity, IPayrollResultRow
{
    public Guid PayrollRunEmployeeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public decimal BaseAmount { get; set; }
    public decimal? RateApplied { get; set; }
    public string? RuleId { get; set; }
    public int DisplayOrder { get; set; }

    public Money AsMoney() => new(Amount, new CurrencyCode(CurrencyCode));
}

/// <summary>
/// One step of the derivation of one figure. Written once when the run is calculated and never
/// updated: the trace of a finalised payroll is immutable.
/// </summary>
public class PayrollCalculationTraceEntry : Entity, IPayrollResultRow
{
    public Guid PayrollRunEmployeeId { get; set; }
    public int Sequence { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string ItemKey { get; set; } = string.Empty;
    public string? RuleId { get; set; }
    public string? RuleType { get; set; }
    public string? VerificationStatus { get; set; }
    public string? RuleSource { get; set; }
    public DateOnly? RuleEffectiveFrom { get; set; }
    public string? Inputs { get; set; }
    public string? Steps { get; set; }
    public decimal? RawValue { get; set; }
    public decimal? OutputAmount { get; set; }
    public string? OutputCurrency { get; set; }
    public string? RoundingApplied { get; set; }
    public string? Explanation { get; set; }
    public string? Conversion { get; set; }
}

/// <summary>A figure that could not be produced, with the rule and question behind it.</summary>
public class PayrollUnresolvedItem : Entity, IPayrollResultRow
{
    public Guid PayrollRunEmployeeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string ItemKey { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? RuleType { get; set; }
    public string? RuleId { get; set; }
    public string? VerificationStatus { get; set; }
    public string? ComplianceQuestion { get; set; }
    public string? Remedy { get; set; }
}

/// <summary>How this employee's cost is attributed to projects and departments.</summary>
public class PayrollCostAllocation : Entity, IPayrollResultRow
{
    public Guid PayrollRunEmployeeId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? ProjectName { get; set; }

    /// <summary>
    /// The site within the project, where the work was booked to one. Persisted alongside the
    /// project because labour cost on a construction contract is asked about by site at least as
    /// often as by project.
    /// </summary>
    public Guid? ProjectSiteId { get; set; }
    public string? ProjectSiteName { get; set; }

    public Guid? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public decimal Percent { get; set; }
    public decimal AllocatedCostAmount { get; set; }
    public string CurrencyCode { get; set; } = "USD";

    public Money AllocatedCost => new(AllocatedCostAmount, new CurrencyCode(CurrencyCode));
}
