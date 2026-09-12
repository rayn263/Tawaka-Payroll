using Tawaka.Domain.Common;

namespace Tawaka.Domain.Audit;

public enum AuditAction
{
    Create = 0,
    Update = 1,
    Delete = 2,
    Approve = 3,
    Reject = 4,
    Finalise = 5,
    Lock = 6,
    Reopen = 7,
    Login = 8,
    Export = 9,
    RecordPayment = 10,
    VerifyRule = 11,
    Blocked = 12
}

/// <summary>
/// An append-only audit entry. One row per changed field, so "who changed this salary, from what,
/// to what, when" is answerable directly. There is no update or delete path in the application.
/// </summary>
public class AuditLog : Entity
{
    public DateTimeOffset OccurredAt { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>Denormalised so the entry stays readable if the user record is later removed.</summary>
    public string UserName { get; set; } = string.Empty;

    public AuditAction Action { get; set; }

    public string EntityName { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    public string? FieldName { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string? Reason { get; set; }

    public string? Machine { get; set; }

    public Guid CorrelationId { get; set; }
}
