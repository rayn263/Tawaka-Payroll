using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Domain.Audit;
using Tawaka.Domain.Security;

namespace Tawaka.Application.Security;

public enum AuthenticationOutcome
{
    Success = 0,
    UnknownUser = 1,
    IncorrectPassword = 2,
    AccountInactive = 3,
    AccountLockedOut = 4,
    PasswordChangeRequired = 5
}

public sealed record AuthenticatedUser(
    Guid UserId,
    string Username,
    string FullName,
    Guid? CompanyId,
    IReadOnlyList<string> Roles,
    IReadOnlySet<string> Permissions,
    bool MustChangePassword);

public sealed record AuthenticationResult(
    AuthenticationOutcome Outcome,
    AuthenticatedUser? User = null,
    string? Message = null)
{
    public bool Succeeded => Outcome is AuthenticationOutcome.Success
        or AuthenticationOutcome.PasswordChangeRequired;
}

/// <summary>
/// Authenticates users and records every attempt. A failed login says only that the credentials
/// were wrong — never whether the username exists — so the login form cannot be used to enumerate
/// accounts.
/// </summary>
public sealed class AuthenticationService
{
    private readonly IPayrollDataContext _context;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly PasswordPolicy _policy;

    public AuthenticationService(
        IPayrollDataContext context,
        IPasswordHasher hasher,
        IClock clock,
        PasswordPolicy? policy = null)
    {
        _context = context;
        _hasher = hasher;
        _clock = clock;
        _policy = policy ?? new PasswordPolicy();
    }

    public async Task<AuthenticationResult> AuthenticateAsync(
        string username, string password, string? machine = null,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.Now;
        var user = await _context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            await RecordAttemptAsync(username, false, "Unknown user", machine, cancellationToken)
                .ConfigureAwait(false);
            return new AuthenticationResult(
                AuthenticationOutcome.UnknownUser, Message: "Incorrect username or password.");
        }

        if (!user.IsActive)
        {
            await RecordAttemptAsync(username, false, "Account inactive", machine, cancellationToken)
                .ConfigureAwait(false);
            return new AuthenticationResult(
                AuthenticationOutcome.AccountInactive, Message: "This account is not active.");
        }

        if (user.IsLockedOut(now))
        {
            await RecordAttemptAsync(username, false, "Locked out", machine, cancellationToken)
                .ConfigureAwait(false);
            return new AuthenticationResult(
                AuthenticationOutcome.AccountLockedOut,
                Message: $"This account is locked until {user.LockedUntil:HH:mm}.");
        }

        if (!_hasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= _policy.MaximumFailedAttempts)
            {
                user.LockedUntil = now.Add(_policy.LockoutDuration);
                user.FailedLoginCount = 0;
            }

            await RecordAttemptAsync(username, false, "Incorrect password", machine, cancellationToken)
                .ConfigureAwait(false);
            return new AuthenticationResult(
                AuthenticationOutcome.IncorrectPassword,
                Message: "Incorrect username or password.");
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;

        // Transparently strengthen a hash created under weaker parameters.
        if (_hasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = _hasher.Hash(password);
            user.PasswordChangedAt = now;
        }

        var permissions = await LoadPermissionsAsync(user, cancellationToken).ConfigureAwait(false);
        var roles = await LoadRoleNamesAsync(user, cancellationToken).ConfigureAwait(false);

        await RecordAttemptAsync(username, true, null, machine, cancellationToken).ConfigureAwait(false);

        var authenticated = new AuthenticatedUser(
            user.Id, user.Username, user.FullName, user.CompanyId,
            roles, permissions, user.MustChangePassword);

        return new AuthenticationResult(
            user.MustChangePassword
                ? AuthenticationOutcome.PasswordChangeRequired
                : AuthenticationOutcome.Success,
            authenticated);
    }

    public async Task<ValidationResult> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        var result = _policy.Validate(newPassword, "NewPassword");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return result.Add("User", "User not found.");
        }

        if (!_hasher.Verify(currentPassword, user.PasswordHash))
        {
            result.Add("CurrentPassword", "Current password is incorrect.");
        }

        if (_hasher.Verify(newPassword, user.PasswordHash))
        {
            result.Add("NewPassword", "The new password must differ from the current one.");
        }

        if (!result.IsValid)
        {
            return result;
        }

        user.PasswordHash = _hasher.Hash(newPassword);
        user.PasswordChangedAt = _clock.Now;
        user.MustChangePassword = false;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>Records a logout in the audit trail. Sessions are in-process, so there is no token to revoke.</summary>
    public async Task RecordLogoutAsync(
        Guid userId, string username, string? machine = null,
        CancellationToken cancellationToken = default)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            OccurredAt = _clock.Now,
            UserId = userId.ToString(),
            UserName = username,
            Action = AuditAction.Login,
            EntityName = nameof(User),
            EntityId = userId.ToString(),
            FieldName = "Session",
            OldValue = "SignedIn",
            NewValue = "SignedOut",
            Machine = machine,
            CorrelationId = Guid.NewGuid()
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlySet<string>> LoadPermissionsAsync(
        User user, CancellationToken cancellationToken)
    {
        var roleIds = user.UserRoles.Select(r => r.RoleId).ToList();

        var codes = await (from rp in _context.RolePermissions
                           join p in _context.Permissions on rp.PermissionId equals p.Id
                           where roleIds.Contains(rp.RoleId)
                           select p.Code)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return codes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<string>> LoadRoleNamesAsync(
        User user, CancellationToken cancellationToken)
    {
        var roleIds = user.UserRoles.Select(r => r.RoleId).ToList();
        return await _context.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RecordAttemptAsync(
        string username, bool succeeded, string? reason, string? machine,
        CancellationToken cancellationToken)
    {
        _context.LoginAttempts.Add(new LoginAttempt
        {
            Username = username,
            AttemptedAt = _clock.Now,
            Succeeded = succeeded,
            FailureReason = reason,
            Machine = machine
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
