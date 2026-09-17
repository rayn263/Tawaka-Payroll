using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Security;
using Tawaka.Domain.Security;
using Tawaka.Infrastructure.Persistence;

namespace Tawaka.Infrastructure.Seeding;

/// <summary>
/// The outcome of seeding security. Where an administrator account was created, the generated
/// password is returned <b>once</b> so the first-run screen can display it. It is never written to
/// the database in recoverable form and never logged.
/// </summary>
public sealed record SecuritySeedResult(bool AdministratorCreated, string? GeneratedPassword);

/// <summary>
/// Seeds permissions, the four default roles and a first administrator.
/// <para>
/// The administrator password is randomly generated rather than set to a well-known default,
/// because a shipped default password is a shipped vulnerability. The account is created with
/// <see cref="User.MustChangePassword"/> set, so it must be changed at first sign-in.
/// </para>
/// </summary>
public sealed class SecuritySeeder
{
    private const string AdministratorUsername = "admin";

    private readonly PayrollDbContext _context;
    private readonly IPasswordHasher _hasher;

    public SecuritySeeder(PayrollDbContext context, IPasswordHasher hasher)
    {
        _context = context;
        _hasher = hasher;
    }

    public async Task<SecuritySeedResult> SeedAsync(
        Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        await SeedPermissionsAsync(cancellationToken).ConfigureAwait(false);
        await SeedRolesAsync(cancellationToken).ConfigureAwait(false);
        return await SeedAdministratorAsync(companyId, cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var existing = await _context.Permissions
            .Select(p => p.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var known = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, category, description, conflictsWith) in Permissions.All)
        {
            if (known.Contains(code))
            {
                continue;
            }

            _context.Permissions.Add(new Permission
            {
                Code = code,
                Category = category,
                Description = description,
                ConflictsWith = conflictsWith
            });
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        if (await _context.Roles.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var permissions = await _context.Permissions
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.OrdinalIgnoreCase,
                cancellationToken)
            .ConfigureAwait(false);

        // Administrator configures the system and verifies statutory rules, but does not prepare
        // payroll; Payroll Officer prepares it; Manager approves it. Calculate and Approve are
        // declared as conflicting, so no single role can hold both.
        var administrator = new[]
        {
            Permissions.EmployeesView, Permissions.EmployeesEdit, Permissions.EmployeesViewSalary,
            Permissions.EmployeesEditSalary, Permissions.EmployeesDeactivate,
            Permissions.CompanyView, Permissions.CompanyEdit,
            Permissions.ProjectsView, Permissions.ProjectsEdit,
            Permissions.PayrollView, Permissions.PayrollLock, Permissions.PayrollReopen,
            Permissions.StatutoryView, Permissions.StatutoryEditRules,
            Permissions.StatutoryVerifyRules, Permissions.StatutoryRecordPayment,
            Permissions.TimeView, Permissions.LeaveView,
            Permissions.CalendarView, Permissions.CalendarEdit,
            Permissions.LoansView, Permissions.LoansDisburse,
            Permissions.ReportsRun, Permissions.ReportsExport,
            Permissions.UsersManage, Permissions.AuditView, Permissions.SettingsEdit
        };

        var payrollOfficer = new[]
        {
            Permissions.EmployeesView, Permissions.EmployeesEdit, Permissions.EmployeesViewSalary,
            Permissions.EmployeesEditSalary,
            Permissions.ProjectsView,
            Permissions.PayrollView, Permissions.PayrollCreate, Permissions.PayrollCalculate,
            Permissions.PayrollFinalise, Permissions.PayrollRecordPayment,
            Permissions.StatutoryView, Permissions.StatutoryRecordPayment,
            Permissions.CompanyView,

            // The officer captures inputs. Approving them is the manager's, because a person who
            // can both enter and approve their own time, leave or loan is a control failure.
            Permissions.TimeView, Permissions.TimeEdit,
            Permissions.LeaveView, Permissions.LeaveEdit,
            Permissions.CalendarView,
            Permissions.LoansView, Permissions.LoansEdit,
            Permissions.ReportsRun, Permissions.ReportsExport
        };

        var manager = new[]
        {
            Permissions.EmployeesView, Permissions.EmployeesViewSalary,
            Permissions.ProjectsView,
            Permissions.PayrollView, Permissions.PayrollApprove,
            Permissions.StatutoryView,
            Permissions.CompanyView,
            Permissions.TimeView, Permissions.TimeApprove,
            Permissions.LeaveView, Permissions.LeaveApprove,
            Permissions.CalendarView,
            Permissions.LoansView, Permissions.LoansApprove,
            Permissions.ReportsRun, Permissions.ReportsExport
        };

        var viewer = new[]
        {
            Permissions.EmployeesView, Permissions.ProjectsView, Permissions.PayrollView,
            Permissions.StatutoryView, Permissions.CompanyView, Permissions.ReportsRun,
            Permissions.TimeView, Permissions.LeaveView, Permissions.CalendarView,
            Permissions.LoansView
        };

        AddRole(RoleNames.Administrator,
            "Configures the system, manages users and verifies statutory rules.",
            administrator, permissions);
        AddRole(RoleNames.PayrollOfficer,
            "Maintains employees and prepares payroll. Cannot approve payroll.",
            payrollOfficer, permissions);
        AddRole(RoleNames.Manager,
            "Reviews and approves payroll. Cannot prepare it.",
            manager, permissions);
        AddRole(RoleNames.Viewer, "Read-only access.", viewer, permissions);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void AddRole(
        string name, string description, IEnumerable<string> codes,
        IReadOnlyDictionary<string, Guid> permissions)
    {
        var role = new Role { Name = name, Description = description, IsSystemRole = true };
        _context.Roles.Add(role);

        foreach (var code in codes)
        {
            if (permissions.TryGetValue(code, out var permissionId))
            {
                _context.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permissionId
                });
            }
        }
    }

    private async Task<SecuritySeedResult> SeedAdministratorAsync(
        Guid? companyId, CancellationToken cancellationToken)
    {
        if (await _context.Users.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return new SecuritySeedResult(false, null);
        }

        var password = GeneratedPassword.Create();
        var user = new User
        {
            CompanyId = companyId,
            Username = AdministratorUsername,
            FullName = "System Administrator",
            PasswordHash = _hasher.Hash(password),
            MustChangePassword = true,
            IsActive = true
        };
        _context.Users.Add(user);

        var administratorRole = await _context.Roles
            .FirstAsync(r => r.Name == RoleNames.Administrator, cancellationToken)
            .ConfigureAwait(false);

        _context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = administratorRole.Id });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new SecuritySeedResult(true, password);
    }
}
