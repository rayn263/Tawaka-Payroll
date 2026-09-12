namespace Tawaka.Infrastructure.Interceptors;

/// <summary>
/// Thrown when a write is attempted against data belonging to a locked payroll period. Raised by
/// the interceptor at the data layer, so no screen, import or future feature can bypass it by
/// forgetting a check (ADR-006).
/// </summary>
public sealed class PeriodLockedException : InvalidOperationException
{
    public PeriodLockedException(string entityName, string entityId)
        : base($"{entityName} ({entityId}) belongs to a locked payroll period and cannot be " +
               "modified. Reopening a locked payroll requires the Payroll.Reopen permission and " +
               "a recorded reason.")
    {
        EntityName = entityName;
        EntityId = entityId;
    }

    public string EntityName { get; }

    public string EntityId { get; }
}
