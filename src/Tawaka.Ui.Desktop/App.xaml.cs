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

        // Apply migrations and seed the statutory baseline on first run.
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        context.Database.Migrate();
        scope.ServiceProvider.GetRequiredService<StatutoryRuleSeeder>()
            .SeedAsync().GetAwaiter().GetResult();
    }
}
