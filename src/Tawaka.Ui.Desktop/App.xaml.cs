using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tawaka.Infrastructure;
using Tawaka.Infrastructure.Persistence;
using Tawaka.Infrastructure.Seeding;

namespace Tawaka.Ui.Desktop;

/// <summary>
/// The desktop host's startup.
/// <para>
/// Everything that can fail on a user's machine fails here: the data directory, the migrations,
/// the seed. Each is reported plainly rather than as a crash dialog, because the person reading it
/// is a payroll officer on a Windows laptop, not a developer with a debugger.
/// </para>
/// </summary>
public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    /// <summary>
    /// Where the database, logs and backups live: per-user application data, which needs no
    /// administrator rights and is included in a normal Windows profile backup.
    /// </summary>
    public static string DataDirectory { get; private set; } = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // An unhandled exception anywhere in the UI must not vanish into a silent crash: payroll
        // staff need something they can read out over the phone.
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog(args.ExceptionObject as Exception);

        try
        {
            DataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tawaka Payroll");
            Directory.CreateDirectory(DataDirectory);
        }
        catch (Exception ex)
        {
            Fail("Tawaka Payroll could not create its data folder.", ex);
            return;
        }

        var databasePath = Path.Combine(DataDirectory, "tawaka-payroll.db");

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddTawakaInfrastructure($"Data Source={databasePath}");

        Services = services.BuildServiceProvider();

        try
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();

            // Migrations are applied at startup so an upgraded application meets its database
            // already in the shape it expects.
            context.Database.Migrate();

            var outcome = scope.ServiceProvider.GetRequiredService<ApplicationSeeder>()
                .SeedAsync().GetAwaiter().GetResult();

            // The first administrator password is generated, never defaulted, and is shown exactly
            // once. A well-known default would be in every installation of this software.
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
        catch (Exception ex)
        {
            Fail(
                "Tawaka Payroll could not prepare its database.\n\n" +
                $"Database: {databasePath}\n\n" +
                "If this database was restored from a backup taken on a newer version, install " +
                "that version first.",
                ex);
            return;
        }

        // Only now: the window's BlazorWebView binds App.Services, which had to exist first.
        new MainWindow().Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);

        MessageBox.Show(
            "Something went wrong.\n\n" +
            $"{e.Exception.Message}\n\n" +
            $"Details have been written to {Path.Combine(DataDirectory, "logs")}. " +
            "No payroll data has been changed by this error.",
            "Tawaka Payroll",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Handled: a payroll officer mid-entry should not lose the screen to one bad click.
        e.Handled = true;
    }

    private static void Fail(string message, Exception ex)
    {
        WriteCrashLog(ex);

        MessageBox.Show(
            $"{message}\n\n{ex.Message}",
            "Tawaka Payroll — cannot start",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Current.Shutdown(1);
    }

    private static void WriteCrashLog(Exception? ex)
    {
        if (ex is null || string.IsNullOrEmpty(DataDirectory))
        {
            return;
        }

        try
        {
            var logs = Path.Combine(DataDirectory, "logs");
            Directory.CreateDirectory(logs);

            File.AppendAllText(
                Path.Combine(logs, $"error-{DateTime.Now:yyyyMMdd}.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging must never be the thing that brings the application down.
        }
    }
}
