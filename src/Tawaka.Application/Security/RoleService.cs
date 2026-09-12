using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Domain.Security;

namespace Tawaka.Application.Security;

/// <summary>
/// Manages roles and their permissions, and enforces segregation of duties: a role may not hold
/// two permissions declared as conflicting, so the same person cannot both calculate and approve
/// a payroll run.
/// </summary>
public sealed class RoleService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;

    public RoleService(IPayrollDataContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Checks a proposed permission set against the conflict declarations. Returns the offending
    /// pairs rather than silently dropping one of them.
    /// </summary>
    public async Task<ValidationResult> ValidatePermissionSetAsync(
        IReadOnlyCollection<string> permissionCodes,
        bool enforceSegregationOfDuties,
        CancellationToken cancellationToken = default)
    {
        var result = ValidationResult.Success();
        if (!enforceSegregationOfDuties)
        {
            return result;
        }

        var permissions = await _context.Permissions
            .Where(p => p.ConflictsWith != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var requested = permissionCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Each conflicting pair is declared from both sides, so report each pair once.
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var permission in permissions)
        {
            if (!requested.Contains(permission.Code) || !requested.Contains(permission.ConflictsWith!))
            {
                continue;
            }

            var pair = string.Join('|', new[] { permission.Code, permission.ConflictsWith! }
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase));

            if (!reported.Add(pair))
            {
                continue;
            }

            result.Add("Permissions",
                $"Segregation of duties: '{permission.Code}' and '{permission.ConflictsWith}' " +
                "cannot be held by the same role.");
        }

        return result;
    }

    public async Task<ValidationResult> SetRolePermissionsAsync(
        Guid roleId,
        IReadOnlyCollection<string> permissionCodes,
        bool enforceSegregationOfDuties = true,
        CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.UsersManage);

        var validation = await ValidatePermissionSetAsync(
            permissionCodes, enforceSegregationOfDuties, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return validation;
        }

        var role = await _context.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return validation.Add("Role", "Role not found.");
        }

        var permissions = await _context.Permissions
            .Where(p => permissionCodes.Contains(p.Code))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var existing in role.RolePermissions.ToList())
        {
            _context.RolePermissions.Remove(existing);
        }

        foreach (var permission in permissions)
        {
            _context.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id
            });
        }

        // Permission changes are significant: the audit interceptor records each row added and
        // removed, attributed to the administrator making the change.
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public async Task<IReadOnlyList<string>> GetRolePermissionsAsync(
        Guid roleId, CancellationToken cancellationToken = default) =>
        await (from rp in _context.RolePermissions
               join p in _context.Permissions on rp.PermissionId equals p.Id
               where rp.RoleId == roleId
               orderby p.Code
               select p.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
