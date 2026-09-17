using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tawaka.Application.Security;
using Tawaka.Domain.Security;
using Tawaka.Infrastructure;
using Tawaka.Infrastructure.Persistence;
using Tawaka.Infrastructure.Seeding;

namespace Tawaka.Ui.Tests;

/// <summary>
/// A headless host for the real Razor components.
/// <para>
/// This is deliberately not a mock: the components are rendered against the same composition root
/// the desktop host uses (<see cref="DependencyInjection.AddTawakaInfrastructure"/>), the same
/// migrations, the same seeders and a real SQLite database on disk. What it cannot do is prove
/// anything about WPF, WebView2 or Windows; that validation is described in
/// docs/WINDOWS_VALIDATION.md and has not been performed.
/// </para>
/// </summary>
public abstract class UiTestHost : TestContext
{
    private readonly string _databasePath;

    protected UiTestHost()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"tawaka-ui-{Guid.NewGuid():N}.db");

        Services.AddTawakaInfrastructure($"Data Source={_databasePath}");

        // The components never call into JavaScript for anything payroll-related; printing is the
        // one exception and it is allowed to be a no-op here.
        JSInterop.Mode = JSRuntimeMode.Loose;

        Db = Services.GetRequiredService<PayrollDbContext>();
        Db.Database.Migrate();

        SeedOutcome = Services.GetRequiredService<ApplicationSeeder>()
            .SeedAsync().GetAwaiter().GetResult();

        CompanyId = Db.Companies.AsNoTracking().Select(c => c.Id).First();
        Session = Services.GetRequiredService<UserSession>();
    }

    protected PayrollDbContext Db { get; }

    protected SeedOutcome SeedOutcome { get; }

    protected Guid CompanyId { get; }

    protected UserSession Session { get; }

    /// <summary>Signs a user in holding every permission in the catalogue.</summary>
    protected void SignInWithEverything(string fullName = "QA Administrator") =>
        SignIn(fullName, RoleNames.Administrator, Permissions.All.Select(p => p.Code).ToArray());

    /// <summary>
    /// Signs a user in holding exactly the permissions named, so a screen can be checked for what
    /// it offers a payroll officer as against what it offers a manager.
    /// </summary>
    protected void SignIn(string fullName, string role, params string[] permissions) =>
        Session.SignIn(new AuthenticatedUser(
            UserId: Guid.NewGuid(),
            Username: fullName.Replace(" ", ".").ToLowerInvariant(),
            FullName: fullName,
            CompanyId: CompanyId,
            Roles: new[] { role },
            Permissions: permissions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            MustChangePassword: false));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // The container — and with it the DbContext — has already been disposed by the base call.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException)
            {
                // A leftover temporary database is harmless.
            }
        }
    }
}
