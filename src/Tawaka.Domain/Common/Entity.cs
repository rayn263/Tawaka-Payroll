namespace Tawaka.Domain.Common;

/// <summary>Base class for all persisted entities.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

/// <summary>
/// An entity whose changes are recorded by the audit interceptor. The interceptor populates these
/// fields; application code does not set them by hand.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}

/// <summary>
/// Implemented by entities that become immutable once their payroll period is locked. The
/// lock interceptor rejects writes at the data layer, so no screen or import can bypass it.
/// </summary>
public interface ILockable
{
    bool IsLocked { get; }
}
