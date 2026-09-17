using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Domain.Security;

namespace Tawaka.Application.Security;

public sealed record CreateUserCommand
{
    public required string Username { get; init; }
    public required string FullName { get; init; }
    public string? Email { get; init; }
    public Guid? CompanyId { get; init; }
    public required IReadOnlyList<Guid> RoleIds { get; init; }
}

/// <summary>
/// A newly created or reset account. The password is here once and nowhere else: only its hash is
/// stored, and it is never written to the audit trail or a log.
/// </summary>
public sealed record UserCredential(Guid UserId, string Username, string Password);

/// <summary>One user as the administration screen shows them.</summary>
public sealed record UserSummary(
    Guid Id,
    string Username,
    string FullName,
    string? Email,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? LockedUntil,
    IReadOnlyList<string> Roles);

/// <summary>
/// Creates and maintains the people who use the system.
/// <para>
/// Payroll here is built on two people doing different things: whoever calculates a run cannot
/// approve it, whoever captures a timesheet cannot approve it, whoever raises a loan cannot approve
/// it. That control is worth nothing if an installation can only ever have the one administrator
/// created at first run, so this is the service that lets a business set up its own officers,
/// managers and viewers.
/// </para>
/// <para>
/// Every account is created with a generated password and <see cref="User.MustChangePassword"/>
/// set: the administrator who creates an account never knows the password the user ends up with.
/// </para>
/// </summary>
public sealed class UserAdministrationService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IPasswordHasher _hasher;
    private readonly RoleService _roles;
    private readonly IClock _clock;

    public UserAdministrationService(
        IPayrollDataContext context,
        ICurrentUser currentUser,
        IPasswordHasher hasher,
        RoleService roles,
        IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _hasher = hasher;
        _roles = roles;
        _clock = clock;
    }

    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(
        CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.UsersManage);

        var users = await _context.Users.AsNoTracking()
            .OrderBy(u => u.Username)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var roles = await RoleNamesByUserAsync(cancellationToken).ConfigureAwait(false);

        return users.Select(u => new UserSummary(
            u.Id, u.Username, u.FullName, u.Email, u.IsActive, u.MustChangePassword,
            u.LastLoginAt, u.LockedUntil,
            roles.TryGetValue(u.Id, out var names) ? names : Array.Empty<string>())).ToList();
    }

    public Task<List<Role>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.UsersManage);

        return _context.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
    }

    public async Task<OperationResult<UserCredential>> CreateAsync(
        CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.UsersManage);

        var validation = ValidationResult.Success();
        validation.Require(command.Username, nameof(command.Username), "A username is required.");
        validation.Require(command.FullName, nameof(command.FullName), "A full name is required.");
        validation.AddIf(command.RoleIds.Count == 0, nameof(command.RoleIds),
            "A user needs at least one role, or they can do nothing at all.");

        var username = command.Username?.Trim() ?? string.Empty;

        if (username.Length > 0)
        {
            var taken = await _context.Users
                .AnyAsync(u => u.Username.ToLower() == username.ToLower(), cancellationToken)
                .ConfigureAwait(false);
            validation.AddIf(taken, nameof(command.Username),
                $"The username '{username}' is already in use.");
        }

        await CheckRolesAsync(command.RoleIds, validation, cancellationToken).ConfigureAwait(false);

        if (!validation.IsValid)
        {
            return OperationResult<UserCredential>.Failed(validation);
        }

        var password = GeneratedPassword.Create();
        var user = new User
        {
            CompanyId = command.CompanyId,
            Username = username,
            FullName = command.FullName!.Trim(),
            Email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim(),
            PasswordHash = _hasher.Hash(password),
            MustChangePassword = true,
            IsActive = true
        };

        _context.Users.Add(user);

        foreach (var roleId in command.RoleIds.Distinct())
        {
            _context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return OperationResult<UserCredential>.Success(
            new UserCredential(user.Id, user.Username, password));
    }

    public async Task<ValidationResult> SetRolesAsync(
        Guid userId, IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.UsersManage);

        var validation = ValidationResult.Success();
        validation.AddIf(roleIds.Count == 0, "Roles",
            "A user needs at least one role, or they can do nothing at all.");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return validation.Add("User", "User not found.");
        }

        await CheckRolesAsync(roleIds, validation, cancellationToken).ConfigureAwait(false);

        if (!validation.IsValid)
        {
            return validation;
        }

        var existing = await _context.UserRoles
            .Where(r => r.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Losing the ability to manage users altogether would leave the installation unadministrable.
        var remaining = await OtherAdministratorsAsync(userId, cancellationToken).ConfigureAwait(false);
        var keepsManaging = await GrantsAsync(roleIds, Permissions.UsersManage, cancellationToken)
            .ConfigureAwait(false);

        if (!keepsManaging && remaining == 0)
        {
            return validation.Add("Roles",
                "This is the only active user who can manage users. Give someone else that " +
                "permission before taking it away here.");
        }

        _context.UserRoles.RemoveRange(existing);

        foreach (var roleId in roleIds.Distinct())
        {
            _context.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// Issues a new generated password and requires it to be changed at next sign-in. The
    /// administrator doing this sees the password once; they do not choose it.
    /// </summary>
    public async Task<OperationResult<UserCredential>> ResetPasswordAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.UsersManage);

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return OperationResult<UserCredential>.Failed(
                ValidationResult.Success().Add("User", "User not found."));
        }

        var password = GeneratedPassword.Create();

        user.PasswordHash = _hasher.Hash(password);
        user.PasswordChangedAt = _clock.Now;
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockedUntil = null;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return OperationResult<UserCredential>.Success(
            new UserCredential(user.Id, user.Username, password));
    }

    /// <summary>
    /// Disables or re-enables an account. A user is never deleted: their name is on payroll runs
    /// they calculated and approvals they gave, and an audit trail that forgets who did something
    /// is not an audit trail.
    /// </summary>
    public async Task<ValidationResult> SetActiveAsync(
        Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.UsersManage);

        var validation = ValidationResult.Success();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return validation.Add("User", "User not found.");
        }

        if (!isActive)
        {
            validation.AddIf(user.Id.ToString() == _currentUser.UserId, "User",
                "You cannot disable your own account.");

            var others = await OtherAdministratorsAsync(userId, cancellationToken).ConfigureAwait(false);
            validation.AddIf(others == 0 && await CanManageUsersAsync(userId, cancellationToken)
                    .ConfigureAwait(false), "User",
                "This is the only active user who can manage users. Disabling it would leave the " +
                "installation with nobody who can add another.");
        }

        if (!validation.IsValid)
        {
            return validation;
        }

        user.IsActive = isActive;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>Clears a lockout after repeated failed sign-ins, without changing the password.</summary>
    public async Task<ValidationResult> UnlockAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.UsersManage);

        var validation = ValidationResult.Success();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return validation.Add("User", "User not found.");
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    // ---- Checks -------------------------------------------------------------------------------

    /// <summary>
    /// Roles must exist, and the permissions they grant <b>between them</b> must not conflict.
    /// Segregation of duties is enforced per role when a role is saved; a user holding two roles
    /// would otherwise combine what neither role is allowed to hold on its own.
    /// </summary>
    private async Task CheckRolesAsync(
        IReadOnlyList<Guid> roleIds, ValidationResult validation, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return;
        }

        var found = await _context.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (found.Count != roleIds.Distinct().Count())
        {
            validation.Add("Roles", "One of the roles selected no longer exists.");
            return;
        }

        var codes = await PermissionCodesAsync(roleIds, cancellationToken).ConfigureAwait(false);
        var conflicts = await _roles
            .ValidatePermissionSetAsync(codes, enforceSegregationOfDuties: true, cancellationToken)
            .ConfigureAwait(false);

        foreach (var error in conflicts.Errors)
        {
            validation.Add("Roles",
                error.Message.Replace(
                    "cannot be held by the same role",
                    "cannot be held by the same user, even through two roles"));
        }
    }

    private async Task<List<string>> PermissionCodesAsync(
        IReadOnlyList<Guid> roleIds, CancellationToken cancellationToken) =>
        await _context.RolePermissions.AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Join(_context.Permissions, rp => rp.PermissionId, p => p.Id, (_, p) => p.Code)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<bool> GrantsAsync(
        IReadOnlyList<Guid> roleIds, string permissionCode, CancellationToken cancellationToken) =>
        (await PermissionCodesAsync(roleIds, cancellationToken).ConfigureAwait(false))
        .Contains(permissionCode, StringComparer.OrdinalIgnoreCase);

    private async Task<bool> CanManageUsersAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = await _context.UserRoles.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => r.RoleId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await GrantsAsync(roleIds, Permissions.UsersManage, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>How many other active users could still manage users.</summary>
    private async Task<int> OtherAdministratorsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var candidates = await _context.Users.AsNoTracking()
            .Where(u => u.Id != userId && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var count = 0;
        foreach (var candidate in candidates)
        {
            if (await CanManageUsersAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    private async Task<Dictionary<Guid, IReadOnlyList<string>>> RoleNamesByUserAsync(
        CancellationToken cancellationToken)
    {
        var pairs = await _context.UserRoles.AsNoTracking()
            .Join(_context.Roles, ur => ur.RoleId, r => r.Id,
                (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return pairs
            .GroupBy(p => p.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(p => p.Name).OrderBy(n => n).ToList());
    }
}
