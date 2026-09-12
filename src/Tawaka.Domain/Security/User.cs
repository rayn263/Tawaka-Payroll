using Tawaka.Domain.Common;

namespace Tawaka.Domain.Security;

/// <summary>
/// An application user. Passwords are stored only as a salted, iterated hash — never in plain
/// text, and never reversibly encrypted.
/// </summary>
public class User : AuditableEntity
{
    /// <summary>Null for a global administrator who is not tied to one company.</summary>
    public Guid? CompanyId { get; set; }

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Email { get; set; }

    /// <summary>Encoded hash: algorithm, iteration count, salt and subkey. Never the password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public DateTimeOffset? PasswordChangedAt { get; set; }

    public bool MustChangePassword { get; set; }

    public bool IsActive { get; set; } = true;

    public int FailedLoginCount { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil.HasValue && LockedUntil.Value > now;
}

public class Role : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>System roles cannot be deleted, though their permissions can be adjusted.</summary>
    public bool IsSystemRole { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class Permission : Entity
{
    /// <summary>Stable code, e.g. "Payroll.Approve".</summary>
    public string Code { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Marks a permission that must not be held together with its counterpart by the same role,
    /// enforcing segregation of duties.
    /// </summary>
    public string? ConflictsWith { get; set; }
}

public class RolePermission : Entity
{
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }

    public Guid PermissionId { get; set; }
    public Permission? Permission { get; set; }
}

public class UserRole : Entity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
}

/// <summary>Every authentication attempt, successful or not.</summary>
public class LoginAttempt : Entity
{
    public string Username { get; set; } = string.Empty;

    public DateTimeOffset AttemptedAt { get; set; }

    public bool Succeeded { get; set; }

    public string? FailureReason { get; set; }

    public string? Machine { get; set; }
}
