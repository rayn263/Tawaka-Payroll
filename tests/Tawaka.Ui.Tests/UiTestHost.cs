using AngleSharp.Dom;
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
            UserId: IdentityOf(fullName),
            Username: fullName.Replace(" ", ".").ToLowerInvariant(),
            FullName: fullName,
            CompanyId: CompanyId,
            Roles: new[] { role },
            Permissions: permissions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            MustChangePassword: false));

    /// <summary>
    /// The form control sitting under a given label.
    /// <para>
    /// Found by its label rather than by position, so a test says what a person filling the form
    /// in would say — "Employee number" — and does not quietly start filling in a different box
    /// when a field is added above it.
    /// </para>
    /// </summary>
    protected static IElement Field(IRenderedFragment page, string label)
    {
        foreach (var cell in page.FindAll("div"))
        {
            var caption = cell.QuerySelector("label");
            if (caption is null)
            {
                continue;
            }

            var text = caption.TextContent.Trim().TrimEnd('*').Trim();
            if (!text.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var control = cell.QuerySelector("input, select, textarea");
            if (control is not null)
            {
                return control;
            }
        }

        throw new InvalidOperationException(
            $"The rendered page has no form control labelled '{label}'.");
    }

    /// <summary>The button whose caption is exactly this text.</summary>
    protected static IElement Button(IRenderedFragment page, string caption) =>
        page.FindAll("button").FirstOrDefault(
            b => b.TextContent.Trim().Equals(caption, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"The rendered page has no '{caption}' button.");

    /// <summary>
    /// The same person always has the same identity, across sign-outs and sign-ins. Segregation of
    /// duties turns on who someone is, so a test that signs the same person back in under a fresh
    /// identity would quietly stop testing it.
    /// </summary>
    private static Guid IdentityOf(string fullName) =>
        new(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(fullName)));

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
