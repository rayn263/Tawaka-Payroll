using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory;

namespace Tawaka.Domain.Payroll;

public enum PayrollPeriodStatus
{
    Open = 0,
    Closed = 1,
    Locked = 2
}

/// <summary>
/// Mode a payroll run executes under. Live payroll resolves only Verified statutory rules;
/// Development permits lower grades but watermarks output and creates no obligations (ADR-012).
/// </summary>
public enum PayrollMode
{
    Development = 0,
    Live = 1
}

/// <summary>
/// A payroll period. Introduced at Milestone 1 only as the lockable scope the lock interceptor
/// enforces against; the full period/calendar model arrives with the payroll workflow.
/// </summary>
public class PayrollPeriod : AuditableEntity, ILockable
{
    public Guid CompanyId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public PeriodBasis Frequency { get; set; } = PeriodBasis.Monthly;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public DateOnly PayDate { get; set; }

    public int TaxYear { get; set; }

    public PayrollPeriodStatus Status { get; set; } = PayrollPeriodStatus.Open;

    public PayrollMode Mode { get; set; } = PayrollMode.Development;

    public DateTimeOffset? LockedAt { get; set; }

    public string? LockedBy { get; set; }

    public bool IsLocked => Status == PayrollPeriodStatus.Locked;
}
