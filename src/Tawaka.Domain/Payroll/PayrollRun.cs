using Tawaka.Domain.Common;

namespace Tawaka.Domain.Payroll;

/// <summary>
/// The processing lifecycle of a payroll run. A period may need more than one run — a normal run,
/// then a supplementary or a correction — so the workflow lives on the run rather than the
/// calendar period, while the period-level lock still gates everything within it.
/// </summary>
public enum PayrollRunStatus
{
    Draft = 0,
    Calculating = 1,
    Review = 2,
    Approved = 3,
    Finalised = 4,
    Paid = 5,
    Locked = 6
}

public enum PayrollRunType
{
    Normal = 0,
    Supplementary = 1,
    Bonus = 2,
    Correction = 3
}

/// <summary>
/// One processing of a payroll period.
/// <para>
/// A run holds the results and, critically, the rule and exchange-rate snapshot used to produce
/// them. Re-opening a September run in 2028 reproduces September's figures because the run carries
/// the rule versions it was calculated under, not whatever the rules say today (ADR-004).
/// </para>
/// </summary>
public class PayrollRun : AuditableEntity, ILockable
{
    public Guid CompanyId { get; set; }

    public Guid PayrollPeriodId { get; set; }

    public PayrollPeriod? PayrollPeriod { get; set; }

    public int RunNumber { get; set; } = 1;

    public PayrollRunType RunType { get; set; } = PayrollRunType.Normal;

    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Draft;

    /// <summary>
    /// Development runs may use unverified rules but are watermarked and create no statutory
    /// obligations. Live runs resolve only verified rules.
    /// </summary>
    public PayrollMode Mode { get; set; } = PayrollMode.Development;

    public string? CalculatedBy { get; set; }
    public DateTimeOffset? CalculatedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? FinalisedBy { get; set; }
    public DateTimeOffset? FinalisedAt { get; set; }
    public string? PaidBy { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public string? ReopenedBy { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
    public string? ReopenReason { get; set; }

    /// <summary>The engine version that produced the results, so behaviour changes are traceable.</summary>
    public string? EngineVersion { get; set; }

    /// <summary>Immutable copy of the rule versions used, in addition to the per-result records.</summary>
    public string? RuleSnapshot { get; set; }

    /// <summary>Immutable copy of the exchange rates frozen into this run.</summary>
    public string? ExchangeRateSnapshot { get; set; }

    public string? Notes { get; set; }

    public ICollection<PayrollRunEmployee> Employees { get; set; } = new List<PayrollRunEmployee>();

    public bool IsLocked => Status == PayrollRunStatus.Locked;

    /// <summary>Once approved, figures are frozen; changing them requires an audited reopen.</summary>
    public bool AreFiguresFrozen => Status >= PayrollRunStatus.Approved;

    public bool CanCalculate => Status is PayrollRunStatus.Draft or PayrollRunStatus.Calculating
        or PayrollRunStatus.Review;
}
