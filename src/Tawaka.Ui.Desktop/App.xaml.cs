using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Tawaka.Infrastructure;
using Tawaka.Infrastructure.Persistence;
using Tawaka.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Tawaka.Ui.Desktop;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tawaka Payroll");
        Directory.CreateDirectory(dataDirectory);

        var databasePath = Path.Combine(dataDirectory, "tawaka-payroll.db");

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddTawakaInfrastructure($"Data Source={databasePath}");

        Services = services.BuildServiceProvider();

        // Apply migrations and run first-run seeding.
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        context.Database.Migrate();

        var outcome = scope.ServiceProvider.GetRequiredService<ApplicationSeeder>()
            .SeedAsync().GetAwaiter().GetResult();

        // The first administrator password is generated, not defaulted, and is shown exactly once.
        if (outcome.Security.AdministratorCreated)
        {
            MessageBox.Show(
                "A first administrator account has been created.\n\n" +
                $"Username: admin\nPassword: {outcome.Security.GeneratedPassword}\n\n" +
                "Write this down now — it is not stored anywhere and cannot be shown again. " +
                "You will be asked to change it when you sign in.",
                "Tawaka Payroll — first run",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
