using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Security;
using Tawaka.Domain.Security;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

public class SecuritySeedingTests
{
    [Fact]
    public async Task All_permissions_and_the_four_default_roles_are_seeded()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        Assert.Equal(Permissions.All.Count, await db.Context.Permissions.CountAsync());

        var roles = await db.Context.Roles.AsNoTracking().Select(r => r.Name).ToListAsync();
        Assert.Equal(4, roles.Count);
        Assert.Contains(RoleNames.Administrator, roles);
        Assert.Contains(RoleNames.PayrollOfficer, roles);
        Assert.Contains(RoleNames.Manager, roles);
        Assert.Contains(RoleNames.Viewer, roles);
    }

    /// <summary>
    /// Segregation of duties, as shipped: the officer who prepares payroll cannot approve it, and
    /// the manager who approves cannot prepare it.
    /// </summary>
    [Fact]
    public async Task The_payroll_officer_cannot_approve_and_the_manager_cannot_calculate()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var officer = await PermissionsForAsync(db, RoleNames.PayrollOfficer);
        var manager = await PermissionsForAsync(db, RoleNames.Manager);

        Assert.Contains(Permissions.PayrollCalculate, officer);
        Assert.DoesNotContain(Permissions.PayrollApprove, officer);

        Assert.Contains(Permissions.PayrollApprove, manager);
        Assert.DoesNotContain(Permissions.PayrollCalculate, manager);
    }

    [Fact]
    public async Task Only_the_administrator_can_verify_statutory_rules_or_manage_users()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        foreach (var role in new[] { RoleNames.PayrollOfficer, RoleNames.Manager, RoleNames.Viewer })
        {
            var permissions = await PermissionsForAsync(db, role);
            Assert.DoesNotContain(Permissions.StatutoryVerifyRules, permissions);
            Assert.DoesNotContain(Permissions.UsersManage, permissions);
        }

        var administrator = await PermissionsForAsync(db, RoleNames.Administrator);
        Assert.Contains(Permissions.StatutoryVerifyRules, administrator);
        Assert.Contains(Permissions.UsersManage, administrator);
    }

    [Fact]
    public async Task The_viewer_role_is_read_only()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var viewer = await PermissionsForAsync(db, RoleNames.Viewer);

        Assert.DoesNotContain(Permissions.EmployeesEdit, viewer);
        Assert.DoesNotContain(Permissions.PayrollCalculate, viewer);
        Assert.DoesNotContain(Permissions.PayrollApprove, viewer);
        Assert.DoesNotContain(Permissions.ReportsExport, viewer);
        Assert.Contains(Permissions.EmployeesView, viewer);
    }

    /// <summary>A shipped default password is a shipped vulnerability.</summary>
    [Fact]
    public async Task The_administrator_password_is_generated_not_defaulted()
    {
        using var dbA = new TestDatabase();
        using var dbB = new TestDatabase();

        var first = await dbA.SeedAllAsync();
        var second = await dbB.SeedAllAsync();

        Assert.True(first.Security.AdministratorCreated);
        Assert.NotNull(first.Security.GeneratedPassword);
        Assert.NotEqual(first.Security.GeneratedPassword, second.Security.GeneratedPassword);
        Assert.True(new PasswordPolicy().Validate(first.Security.GeneratedPassword).IsValid);
    }

    [Fact]
    public async Task The_stored_password_is_a_hash_and_the_account_must_change_it()
    {
        using var db = new TestDatabase();
        var outcome = await db.SeedAllAsync();

        var admin = await db.Context.Users.AsNoTracking().SingleAsync(u => u.Username == "admin");

        Assert.DoesNotContain(outcome.Security.GeneratedPassword!, admin.PasswordHash,
            StringComparison.Ordinal);
        Assert.StartsWith("PBKDF2-SHA256$", admin.PasswordHash, StringComparison.Ordinal);
        Assert.True(admin.MustChangePassword);
    }

    private static async Task<List<string>> PermissionsForAsync(TestDatabase db, string roleName)
    {
        var role = await db.Context.Roles.AsNoTracking().SingleAsync(r => r.Name == roleName);
        return await (from rp in db.Context.RolePermissions.AsNoTracking()
                      join p in db.Context.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                      where rp.RoleId == role.Id
                      select p.Code).ToListAsync();
    }
}

public class AuthenticationTests
{
    private const string Password = "Nyanga2026Site";

    private static async Task<(TestDatabase Db, AuthenticationService Auth, Guid UserId)> SetUpAsync()
    {
        var db = new TestDatabase();
        await db.SeedAllAsync();

        var user = new User
        {
            Username = "tncube",
            FullName = "Tapiwa Ncube",
            PasswordHash = db.Hasher.Hash(Password),
            IsActive = true
        };
        var officer = await db.Context.Roles.SingleAsync(r => r.Name == RoleNames.PayrollOfficer);
        db.Context.Users.Add(user);
        db.Context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = officer.Id });
        await db.Context.SaveChangesAsync();

        var auth = new AuthenticationService(db.Context, db.Hasher, db.Clock);
        return (db, auth, user.Id);
    }

    [Fact]
    public async Task Correct_credentials_sign_the_user_in_with_their_permissions()
    {
        var (db, auth, _) = await SetUpAsync();
        using var _db = db;

        var result = await auth.AuthenticateAsync("tncube", Password);

        Assert.True(result.Succeeded);
        Assert.Equal(AuthenticationOutcome.Success, result.Outcome);
        Assert.Equal("Tapiwa Ncube", result.User!.FullName);
        Assert.Contains(RoleNames.PayrollOfficer, result.User.Roles);
        Assert.Contains(Permissions.PayrollCalculate, result.User.Permissions);
        Assert.DoesNotContain(Permissions.PayrollApprove, result.User.Permissions);
    }

    [Fact]
    public async Task An_incorrect_password_is_refused()
    {
        var (db, auth, _) = await SetUpAsync();
        using var _db = db;

        var result = await auth.AuthenticateAsync("tncube", "wrong-password");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthenticationOutcome.IncorrectPassword, result.Outcome);
        Assert.Null(result.User);
    }

    /// <summary>
    /// The message must not reveal whether the username exists, or the login form becomes an
    /// account-enumeration tool.
    /// </summary>
    [Fact]
    public async Task An_unknown_user_and_a_wrong_password_give_the_same_message()
    {
        var (db, auth, _) = await SetUpAsync();
        using var _db = db;

        var unknown = await auth.AuthenticateAsync("nobody", Password);
        var wrong = await auth.AuthenticateAsync("tncube", "wrong-password");

        Assert.Equal(unknown.Message, wrong.Message);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account()
    {
        var (db, auth, _) = await SetUpAsync();
        using var _db = db;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await auth.AuthenticateAsync("tncube", "wrong-password");
        }

        var result = await auth.AuthenticateAsync("tncube", Password);

        Assert.Equal(AuthenticationOutcome.AccountLockedOut, result.Outcome);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_the_failure_count()
    {
        var (db, auth, userId) = await SetUpAsync();
        using var _db = db;

        await auth.AuthenticateAsync("tncube", "wrong-password");
        await auth.AuthenticateAsync("tncube", Password);

        var user = await db.Context.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Equal(db.Clock.Now, user.LastLoginAt);
    }

    [Fact]
    public async Task An_inactive_account_cannot_sign_in()
    {
        var (db, auth, userId) = await SetUpAsync();
        using var _db = db;

        var user = await db.Context.Users.SingleAsync(u => u.Id == userId);
        user.IsActive = false;
        await db.Context.SaveChangesAsync();

        var result = await auth.AuthenticateAsync("tncube", Password);

        Assert.Equal(AuthenticationOutcome.AccountInactive, result.Outcome);
    }

    [Fact]
    public async Task Every_attempt_is_recorded()
    {
        var (db, auth, _) = await SetUpAsync();
        using var _db = db;

        await auth.AuthenticateAsync("tncube", Password);
        await auth.AuthenticateAsync("tncube", "wrong-password");

        var attempts = await db.Context.LoginAttempts.AsNoTracking()
            .Where(a => a.Username == "tncube").ToListAsync();

        Assert.Equal(2, attempts.Count);
        Assert.Contains(attempts, a => a.Succeeded);
        Assert.Contains(attempts, a => !a.Succeeded && a.FailureReason == "Incorrect password");
    }

    [Fact]
    public async Task Changing_a_password_requires_the_current_one_and_a_compliant_new_one()
    {
        var (db, auth, userId) = await SetUpAsync();
        using var _db = db;

        Assert.False((await auth.ChangePasswordAsync(userId, "wrong", "Harare2026Site")).IsValid);
        Assert.False((await auth.ChangePasswordAsync(userId, Password, "weak")).IsValid);
        Assert.False((await auth.ChangePasswordAsync(userId, Password, Password)).IsValid);

        var result = await auth.ChangePasswordAsync(userId, Password, "Harare2026Site");

        Assert.True(result.IsValid);
        Assert.True((await auth.AuthenticateAsync("tncube", "Harare2026Site")).Succeeded);
        Assert.False((await auth.AuthenticateAsync("tncube", Password)).Succeeded);
    }

    [Fact]
    public async Task Signing_out_is_recorded_in_the_audit_trail()
    {
        var (db, auth, userId) = await SetUpAsync();
        using var _db = db;

        await auth.RecordLogoutAsync(userId, "Tapiwa Ncube");

        var entry = await db.Context.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.FieldName == "Session");

        Assert.Equal("SignedIn", entry.OldValue);
        Assert.Equal("SignedOut", entry.NewValue);
    }

    [Fact]
    public async Task The_session_reports_permissions_and_clears_on_sign_out()
    {
        var (db, auth, _) = await SetUpAsync();
        using var _db = db;

        var result = await auth.AuthenticateAsync("tncube", Password);
        var session = new UserSession();
        session.SignIn(result.User!);

        Assert.True(session.IsAuthenticated);
        Assert.True(session.HasPermission(Permissions.PayrollCalculate));
        Assert.False(session.HasPermission(Permissions.PayrollApprove));
        Assert.True(session.IsInRole(RoleNames.PayrollOfficer));
        Assert.Equal("Tapiwa Ncube", session.UserName);

        session.SignOut();

        Assert.False(session.IsAuthenticated);
        Assert.False(session.HasPermission(Permissions.PayrollCalculate));
        Assert.Equal("anonymous", session.UserId);
    }
}

public class SegregationOfDutiesTests
{
    [Fact]
    public async Task A_role_may_not_hold_both_calculate_and_approve()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();
        var service = new RoleService(db.Context, db.User);

        var result = await service.ValidatePermissionSetAsync(
            new[] { Permissions.PayrollCalculate, Permissions.PayrollApprove },
            enforceSegregationOfDuties: true);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains("Segregation of duties", result.Errors[0].Message);
    }

    [Fact]
    public async Task Either_permission_alone_is_permitted()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();
        var service = new RoleService(db.Context, db.User);

        Assert.True((await service.ValidatePermissionSetAsync(
            new[] { Permissions.PayrollCalculate }, true)).IsValid);
        Assert.True((await service.ValidatePermissionSetAsync(
            new[] { Permissions.PayrollApprove }, true)).IsValid);
    }

    /// <summary>
    /// Disabling segregation of duties is a deliberate company decision, and the change itself is
    /// audited like any other.
    /// </summary>
    [Fact]
    public async Task The_check_can_be_switched_off_deliberately()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();
        var service = new RoleService(db.Context, db.User);

        var result = await service.ValidatePermissionSetAsync(
            new[] { Permissions.PayrollCalculate, Permissions.PayrollApprove },
            enforceSegregationOfDuties: false);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Setting_a_conflicting_permission_set_on_a_role_is_refused()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();
        var service = new RoleService(db.Context, db.User);
        var officer = await db.Context.Roles.SingleAsync(r => r.Name == RoleNames.PayrollOfficer);

        var result = await service.SetRolePermissionsAsync(officer.Id,
            new[] { Permissions.PayrollCalculate, Permissions.PayrollApprove });

        Assert.False(result.IsValid);

        var permissions = await service.GetRolePermissionsAsync(officer.Id);
        Assert.DoesNotContain(Permissions.PayrollApprove, permissions);
    }

    [Fact]
    public async Task Permission_changes_are_audited()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();
        var service = new RoleService(db.Context, db.User);
        var viewer = await db.Context.Roles.SingleAsync(r => r.Name == RoleNames.Viewer);

        var before = await db.Context.AuditLogs.CountAsync(a => a.EntityName == nameof(RolePermission));

        await service.SetRolePermissionsAsync(viewer.Id,
            new[] { Permissions.EmployeesView, Permissions.ReportsRun });

        var after = await db.Context.AuditLogs.CountAsync(a => a.EntityName == nameof(RolePermission));
        Assert.True(after > before);
    }

    [Fact]
    public async Task Managing_roles_requires_the_users_manage_permission()
    {
        using var db = new TestDatabase(TestUser.WithPermissions(Permissions.EmployeesView));
        await db.SeedAllAsync();
        var service = new RoleService(db.Context, db.User);
        var viewer = await db.Context.Roles.SingleAsync(r => r.Name == RoleNames.Viewer);

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            service.SetRolePermissionsAsync(viewer.Id, new[] { Permissions.EmployeesView }));
    }
}
