using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Security;
using Tawaka.Domain.Security;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// Creating the people who use the system.
/// <para>
/// This exists because without it a business could not run a payroll at all. Approval refuses
/// whoever calculated the run, so an installation holding only the administrator created at first
/// run can calculate and nothing else: no approval, no finalisation, no obligations, no payment.
/// </para>
/// </summary>
public class UserAdministrationTests : EmployeeTestBase
{
    private static UserAdministrationService ServiceFor(
        TestDatabase db, Tawaka.Application.Abstractions.ICurrentUser? user = null)
    {
        var actor = user ?? db.User;
        return new UserAdministrationService(
            db.Context, actor, db.Hasher, new RoleService(db.Context, actor), db.Clock);
    }

    private static async Task<Guid> RoleIdAsync(TestDatabase db, string name) =>
        (await db.Context.Roles.AsNoTracking().SingleAsync(r => r.Name == name)).Id;

    [Fact]
    public async Task A_user_is_created_with_a_generated_password_they_must_change()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;

        var service = ServiceFor(db);

        var created = await service.CreateAsync(new CreateUserCommand
        {
            Username = "t.ncube",
            FullName = "Tapiwa Ncube",
            Email = "tapiwa@example.co.zw",
            CompanyId = companyId,
            RoleIds = new[] { await RoleIdAsync(db, RoleNames.PayrollOfficer) }
        });

        Assert.True(created.Succeeded, created.Validation.ToString());
        Assert.Equal(16, created.Value!.Password.Length);

        db.Context.ChangeTracker.Clear();
        var stored = await db.Context.Users.AsNoTracking()
            .SingleAsync(u => u.Username == "t.ncube");

        Assert.True(stored.MustChangePassword);
        Assert.True(stored.IsActive);

        // The password is not in the database in any recoverable form.
        Assert.DoesNotContain(created.Value.Password, stored.PasswordHash);
        Assert.True(db.Hasher.Verify(created.Value.Password, stored.PasswordHash));

        var roles = await db.Context.UserRoles.AsNoTracking()
            .Where(r => r.UserId == stored.Id).ToListAsync();
        Assert.Single(roles);
    }

    [Fact]
    public async Task A_duplicate_username_is_refused_whatever_its_case()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var service = ServiceFor(db);
        var officer = await RoleIdAsync(db, RoleNames.PayrollOfficer);

        Assert.True((await service.CreateAsync(new CreateUserCommand
        {
            Username = "r.moyo", FullName = "Rudo Moyo", RoleIds = new[] { officer }
        })).Succeeded);

        db.Context.ChangeTracker.Clear();
        var again = await service.CreateAsync(new CreateUserCommand
        {
            Username = "R.Moyo", FullName = "Rudo Moyo", RoleIds = new[] { officer }
        });

        Assert.False(again.Succeeded);
        Assert.Contains(again.Validation.Errors, e =>
            e.Message.Contains("already in use", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_user_with_no_role_is_refused()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var result = await ServiceFor(db).CreateAsync(new CreateUserCommand
        {
            Username = "nobody", FullName = "No Body", RoleIds = Array.Empty<Guid>()
        });

        Assert.False(result.Succeeded);
        Assert.Equal(0, await db.Context.Users.CountAsync(u => u.Username == "nobody"));
    }

    /// <summary>
    /// Segregation of duties is enforced per role. Two roles handed to one person would otherwise
    /// combine into exactly the set neither role is allowed to hold.
    /// </summary>
    [Fact]
    public async Task Two_roles_that_conflict_between_them_cannot_be_given_to_one_person()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var service = ServiceFor(db);

        var result = await service.CreateAsync(new CreateUserCommand
        {
            Username = "both",
            FullName = "Both Hats",
            RoleIds = new[]
            {
                await RoleIdAsync(db, RoleNames.PayrollOfficer),
                await RoleIdAsync(db, RoleNames.Manager)
            }
        });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Validation.Errors, e =>
            e.Message.Contains("Segregation of duties", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Validation.Errors, e =>
            e.Message.Contains("same user", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Resetting_a_password_issues_a_new_one_and_clears_a_lockout()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var service = ServiceFor(db);
        var created = (await service.CreateAsync(new CreateUserCommand
        {
            Username = "locked.out",
            FullName = "Locked Out",
            RoleIds = new[] { await RoleIdAsync(db, RoleNames.Viewer) }
        })).Value!;

        db.Context.ChangeTracker.Clear();
        var user = await db.Context.Users.SingleAsync(u => u.Id == created.UserId);
        user.FailedLoginCount = 5;
        user.LockedUntil = db.Clock.Now.AddMinutes(15);
        user.MustChangePassword = false;
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var reset = await service.ResetPasswordAsync(created.UserId);
        Assert.True(reset.Succeeded);
        Assert.NotEqual(created.Password, reset.Value!.Password);

        db.Context.ChangeTracker.Clear();
        var after = await db.Context.Users.AsNoTracking().SingleAsync(u => u.Id == created.UserId);

        Assert.True(after.MustChangePassword);
        Assert.Null(after.LockedUntil);
        Assert.Equal(0, after.FailedLoginCount);
        Assert.True(db.Hasher.Verify(reset.Value.Password, after.PasswordHash));
        Assert.False(db.Hasher.Verify(created.Password, after.PasswordHash));
    }

    [Fact]
    public async Task The_last_user_who_can_manage_users_cannot_be_disabled()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        db.Context.ChangeTracker.Clear();
        var administrator = await db.Context.Users.AsNoTracking().SingleAsync();

        // Someone else entirely is doing the disabling, so this is not the "not yourself" rule.
        var service = ServiceFor(db, new TestUser("u-other", "Someone Else"));
        var result = await service.SetActiveAsync(administrator.Id, isActive: false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.Message.Contains("only active user", StringComparison.OrdinalIgnoreCase));

        db.Context.ChangeTracker.Clear();
        Assert.True((await db.Context.Users.AsNoTracking()
            .SingleAsync(u => u.Id == administrator.Id)).IsActive);
    }

    [Fact]
    public async Task Nobody_can_disable_their_own_account()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var service = ServiceFor(db);
        var second = (await service.CreateAsync(new CreateUserCommand
        {
            Username = "second.admin",
            FullName = "Second Administrator",
            RoleIds = new[] { await RoleIdAsync(db, RoleNames.Administrator) }
        })).Value!;

        db.Context.ChangeTracker.Clear();

        var asThemselves = ServiceFor(
            db, new TestUser(second.UserId.ToString(), "Second Administrator"));
        var result = await asThemselves.SetActiveAsync(second.UserId, isActive: false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.Message.Contains("your own account", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Managing_users_requires_the_permission()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var withoutPermission = ServiceFor(db, TestUser.WithPermissions(Permissions.AuditView));

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            withoutPermission.CreateAsync(new CreateUserCommand
            {
                Username = "sneaky", FullName = "Sneaky", RoleIds = new[] { Guid.NewGuid() }
            }));

        await Assert.ThrowsAsync<PermissionDeniedException>(() =>
            withoutPermission.GetUsersAsync());
    }
}

/// <summary>
/// The payroll that user administration exists to make possible.
/// </summary>
public class UserAdministrationPayrollTests : PayrollFixtureBase
{
    private static async Task<Guid> RoleIdAsync(TestDatabase db, string name) =>
        (await db.Context.Roles.AsNoTracking().SingleAsync(r => r.Name == name)).Id;

    /// <summary>
    /// The whole point: two real accounts, created through the application, one of whom can approve
    /// what the other calculated.
    /// </summary>
    [Fact]
    public async Task Two_created_users_can_run_a_payroll_between_them()
    {
        using var fixture = await SetUpAsync();
        var db = fixture.Db;

        var admin = new UserAdministrationService(
            db.Context, db.User, db.Hasher, new RoleService(db.Context, db.User), db.Clock);

        var officer = (await admin.CreateAsync(new CreateUserCommand
        {
            Username = "officer",
            FullName = "Tapiwa Ncube",
            CompanyId = fixture.CompanyId,
            RoleIds = new[] { await RoleIdAsync(db, RoleNames.PayrollOfficer) }
        })).Value!;

        db.Context.ChangeTracker.Clear();

        var manager = (await admin.CreateAsync(new CreateUserCommand
        {
            Username = "manager",
            FullName = "Rudo Moyo",
            CompanyId = fixture.CompanyId,
            RoleIds = new[] { await RoleIdAsync(db, RoleNames.Manager) }
        })).Value!;

        db.Context.ChangeTracker.Clear();

        var asOfficer = PayrollServices.For(
            db, new TestUser(officer.UserId.ToString(), "Tapiwa Ncube"));
        var asManager = PayrollServices.For(
            db, new TestUser(manager.UserId.ToString(), "Rudo Moyo"));

        var run = (await asOfficer.Runs.CreateRunAsync(fixture.Period.Id)).Value!;
        Assert.True((await asOfficer.Runs.CalculateAsync(run.Id)).Succeeded);

        db.Context.ChangeTracker.Clear();

        // The officer cannot approve what they calculated…
        var byOfficer = await asOfficer.Runs.ApproveAsync(run.Id);
        Assert.False(byOfficer.IsValid);

        db.Context.ChangeTracker.Clear();

        // …and the manager can.
        var byManager = await asManager.Runs.ApproveAsync(run.Id);
        Assert.True(byManager.IsValid, byManager.ToString());

        db.Context.ChangeTracker.Clear();
        Assert.True((await asOfficer.Runs.FinaliseAsync(run.Id)).IsValid);

        db.Context.ChangeTracker.Clear();
        Assert.NotEmpty(await db.Context.StatutoryObligations.AsNoTracking()
            .Where(o => o.PayrollRunId == run.Id).ToListAsync());
    }
}
