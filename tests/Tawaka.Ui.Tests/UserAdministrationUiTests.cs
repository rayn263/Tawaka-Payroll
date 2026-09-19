using Bunit;
using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Security;
using Tawaka.Ui.Shared.Components.Pages;

namespace Tawaka.Ui.Tests;

/// <summary>
/// The screen a business uses to set itself up: adding the officer and the manager whose separate
/// accounts make an approved payroll possible in the first place.
/// </summary>
public sealed class UserAdministrationUiTests : UiTestHost
{
    private IRenderedComponent<Administration> UsersTab()
    {
        SignInWithEverything();
        return RenderComponent<Administration>(p => p.Add(x => x.Tab, "users"));
    }

    [Fact]
    public void The_users_tab_lists_the_seeded_administrator_and_offers_to_add_another()
    {
        var page = UsersTab();

        Assert.Contains("Users", page.Markup);
        Assert.Contains("admin", page.Markup);
        Assert.Contains("Add a user", page.Markup);
        Assert.Contains("Create user", page.Markup);
    }

    [Fact]
    public void A_user_created_from_the_screen_is_stored_and_their_password_is_shown_once()
    {
        var page = UsersTab();
        var officerRole = Db.Roles.AsNoTracking().Single(r => r.Name == RoleNames.PayrollOfficer);

        Field(page, "Username").Change("t.ncube");
        Field(page, "Full name").Change("Tapiwa Ncube");

        page.FindAll("input[type=checkbox]")[RoleIndex(page, officerRole.Name)].Change(true);

        Button(page, "Create user").Click();

        var stored = Db.Users.AsNoTracking().Single(u => u.Username == "t.ncube");
        Assert.Equal("Tapiwa Ncube", stored.FullName);
        Assert.True(stored.MustChangePassword);

        // The password is on the screen exactly once, and it is not what is in the database.
        Assert.Contains("Password for t.ncube", page.Markup);
        Assert.Contains("cannot be shown again", page.Markup);
        Assert.Contains("Tapiwa Ncube", page.Markup);

        Button(page, "Done").Click();
        Assert.DoesNotContain("Password for t.ncube", page.Markup);
    }

    [Fact]
    public void A_user_with_no_role_is_refused_on_the_screen()
    {
        var page = UsersTab();

        Field(page, "Username").Change("nobody");
        Field(page, "Full name").Change("No Body");

        Button(page, "Create user").Click();

        Assert.Contains("at least one role", page.Markup);
        Assert.Empty(Db.Users.AsNoTracking().Where(u => u.Username == "nobody").ToList());
    }

    /// <summary>
    /// A person's role changes — an officer is promoted to manager. Without this an account is
    /// stuck with whatever it was created as.
    /// </summary>
    [Fact]
    public void A_users_roles_can_be_changed_after_they_are_created()
    {
        var page = UsersTab();
        var officer = Db.Roles.AsNoTracking().Single(r => r.Name == RoleNames.PayrollOfficer);
        var manager = Db.Roles.AsNoTracking().Single(r => r.Name == RoleNames.Manager);

        Field(page, "Username").Change("r.moyo");
        Field(page, "Full name").Change("Rudo Moyo");
        page.FindAll("input[type=checkbox]")[RoleIndex(page, officer.Name)].Change(true);
        Button(page, "Create user").Click();
        Button(page, "Done").Click();

        // Promote them.
        RowButton(page, "r.moyo", "Roles").Click();
        page.FindAll("input[type=checkbox]")[RoleIndex(page, officer.Name)].Change(false);
        page.FindAll("input[type=checkbox]")[RoleIndex(page, manager.Name)].Change(true);
        Button(page, "Save roles").Click();

        Db.ChangeTracker.Clear();
        var user = Db.Users.AsNoTracking().Single(u => u.Username == "r.moyo");
        var roleIds = Db.UserRoles.AsNoTracking()
            .Where(r => r.UserId == user.Id).Select(r => r.RoleId).ToList();

        Assert.Equal(new[] { manager.Id }, roleIds);
    }

    /// <summary>
    /// And segregation of duties still holds across the two roles taken together.
    /// </summary>
    [Fact]
    public void A_user_cannot_be_given_two_roles_that_conflict_between_them()
    {
        var page = UsersTab();
        var officer = Db.Roles.AsNoTracking().Single(r => r.Name == RoleNames.PayrollOfficer);
        var manager = Db.Roles.AsNoTracking().Single(r => r.Name == RoleNames.Manager);

        Field(page, "Username").Change("t.ncube");
        Field(page, "Full name").Change("Tapiwa Ncube");
        page.FindAll("input[type=checkbox]")[RoleIndex(page, officer.Name)].Change(true);
        Button(page, "Create user").Click();
        Button(page, "Done").Click();

        RowButton(page, "t.ncube", "Roles").Click();
        page.FindAll("input[type=checkbox]")[RoleIndex(page, manager.Name)].Change(true);
        Button(page, "Save roles").Click();

        Assert.Contains("Segregation of duties", page.Markup);

        Db.ChangeTracker.Clear();
        var user = Db.Users.AsNoTracking().Single(u => u.Username == "t.ncube");
        Assert.Single(Db.UserRoles.AsNoTracking().Where(r => r.UserId == user.Id).ToList());
    }

    [Fact]
    public void A_user_without_the_permission_is_shown_no_user_administration_at_all()
    {
        SignIn("Viewer", RoleNames.Viewer, Permissions.AuditView);

        var page = RenderComponent<Administration>(p => p.Add(x => x.Tab, "users"));

        Assert.DoesNotContain("Add a user", page.Markup);
        Assert.DoesNotContain("Create user", page.Markup);
    }

    /// <summary>The action button on the row for one username, not whichever row comes first.</summary>
    private static AngleSharp.Dom.IElement RowButton(
        IRenderedFragment page, string username, string caption)
    {
        foreach (var row in page.FindAll("tbody tr"))
        {
            if (row.QuerySelector("td.mono")?.TextContent.Trim() != username)
            {
                continue;
            }

            var button = row.QuerySelectorAll("button")
                .FirstOrDefault(b => b.TextContent.Trim() == caption);

            if (button is not null)
            {
                return button;
            }
        }

        throw new InvalidOperationException($"No '{caption}' button on the row for {username}.");
    }

    private static int RoleIndex(IRenderedFragment page, string roleName)
    {
        var labels = page.FindAll("label.checkbox");

        for (var i = 0; i < labels.Count; i++)
        {
            if (labels[i].TextContent.Contains(roleName, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"No role checkbox for '{roleName}'.");
    }
}
