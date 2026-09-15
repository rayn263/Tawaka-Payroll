using Tawaka.Domain.Common;

namespace Tawaka.Domain.Payroll;

/// <summary>
/// The exact input snapshot a calculation ran on, stored verbatim.
/// <para>
/// Milestone 3 made the snapshot immutable in memory. That is not enough once payroll consumes
/// approved time, leave and loan balances, because those move: recalculating a past period a year
/// later would find a different loan balance and produce a different, equally "correct" answer.
/// Storing the snapshot means a completed run can always be reproduced against the inputs it
/// actually had, and a dispute has something to point at (ADR-035).
/// </para>
/// </summary>
public class PayrollInputSnapshotRecord : AuditableEntity
{
    public Guid PayrollRunId { get; set; }

    public Guid PayrollRunEmployeeId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>The serialised snapshot, exactly as the engine received it.</summary>
    public string SnapshotJson { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the serialised snapshot. Cheap to compare, and it makes tampering visible: a
    /// stored snapshot whose hash no longer matches its content has been altered.
    /// </summary>
    public string SnapshotHash { get; set; } = string.Empty;

    public string EngineVersion { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>
    /// When the snapshot was sealed — the moment calculation began. After this the snapshot is
    /// read-only for every purpose, including recalculation.
    /// </summary>
    public DateTimeOffset? SealedAt { get; set; }

    public bool IsSealed => SealedAt is not null;
}

/// <summary>
/// One approved input a payroll run consumed, recorded relationally.
/// <para>
/// The snapshot JSON already contains this, but a question like "which runs consumed this
/// timesheet?" should not require parsing every snapshot in the database to answer.
/// </para>
/// </summary>
public class PayrollRunInputSource : Entity
{
    public Guid PayrollRunId { get; set; }

    public Guid PayrollRunEmployeeId { get; set; }

    /// <summary>"Timesheet", "LeaveRequest", "Loan", "HolidayCalendar".</summary>
    public string InputType { get; set; } = string.Empty;

    public Guid InputId { get; set; }

    public string Description { get; set; } = string.Empty;

    public string? ApprovedBy { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
}
