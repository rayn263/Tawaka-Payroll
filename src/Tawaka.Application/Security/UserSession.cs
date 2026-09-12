using Tawaka.Application.Abstractions;

namespace Tawaka.Application.Security;

/// <summary>
/// The signed-in user for this desktop session. Implements <see cref="ICurrentUser"/>, so from
/// Milestone 2 the audit trail is attributed to a real person rather than to SYSTEM.
/// </summary>
public sealed class UserSession : ICurrentUser
{
    private AuthenticatedUser? _user;

    public bool IsAuthenticated => _user is not null;

    public AuthenticatedUser? User => _user;

    public Guid? CompanyId => _user?.CompanyId;

    public string UserId => _user?.UserId.ToString() ?? "anonymous";

    public string UserName => _user?.FullName ?? "(not signed in)";

    public string? Machine => Environment.MachineName;

    public event Action? Changed;

    public void SignIn(AuthenticatedUser user)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        Changed?.Invoke();
    }

    public void SignOut()
    {
        _user = null;
        Changed?.Invoke();
    }

    public bool HasPermission(string permissionCode) =>
        _user is not null && _user.Permissions.Contains(permissionCode);

    public bool IsInRole(string roleName) =>
        _user is not null &&
        _user.Roles.Any(r => string.Equals(r, roleName, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Thrown when an action is attempted without the permission it requires.</summary>
public sealed class PermissionDeniedException : UnauthorizedAccessException
{
    public PermissionDeniedException(string permissionCode, string userName)
        : base($"{userName} does not have the '{permissionCode}' permission.")
    {
        PermissionCode = permissionCode;
    }

    public string PermissionCode { get; }
}

public static class CurrentUserExtensions
{
    /// <summary>Throws unless the current user holds the permission. Call before the work, not after.</summary>
    public static void Require(this ICurrentUser user, string permissionCode)
    {
        if (!user.HasPermission(permissionCode))
        {
            throw new PermissionDeniedException(permissionCode, user.UserName);
        }
    }
}
