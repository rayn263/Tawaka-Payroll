using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tawaka.Infrastructure.Administration;
using Tawaka.Infrastructure.Persistence;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// Backup and restore, as a business would actually use them: take a backup, carry on working,
/// take another, and then go back to the first because something was wrong. What must not happen
/// is a restore that half-succeeds and leaves no way back.
/// </summary>
public class QaBackupRestoreTests : EmployeeTestBase
{
    private static Domain.Employees.Employee Person(Guid companyId, string number, string id)
    {
        var employee = NewEmployee(companyId, number);
        employee.NationalId = id;
        return employee;
    }

    private static PayrollDbContext OpenAgain(string path)
    {
        SqliteConnection.ClearAllPools();

        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;

        return new PayrollDbContext(options);
    }

    [Fact]
    public async Task Restoring_an_earlier_backup_undoes_the_later_work_and_keeps_the_earlier()
    {
        var (db, companyId, permanentTypeId, _) = await SetUpAsync();
        using var _db = db;

        var employees = new Application.Employees.EmployeeService(db.Context, db.User, db.Clock);
        var service = new BackupService(db.Context, db.User, db.Clock);
        var databasePath = service.DatabasePath();
        Assert.NotNull(databasePath);

        var root = Path.Combine(Path.GetTempPath(), $"tawaka-qa-backup-{Guid.NewGuid():N}");

        try
        {
            // --- Monday: one employee, then a backup -------------------------------------------
            Assert.True((await employees.CreateAsync(
                Person(companyId, "EMP-MON", "63-1111111 A 11"))).Succeeded);
            db.Context.ChangeTracker.Clear();

            var monday = await service.BackupAsync(Path.Combine(root, "monday"), "Monday");
            Assert.True(monday.Succeeded, monday.Validation.ToString());

            // --- Tuesday: a second employee, then a second backup -------------------------------
            Assert.True((await employees.CreateAsync(
                Person(companyId, "EMP-TUE", "63-2222222 B 22"))).Succeeded);
            db.Context.ChangeTracker.Clear();

            var tuesday = await service.BackupAsync(Path.Combine(root, "tuesday"), "Tuesday");
            Assert.True(tuesday.Succeeded, tuesday.Validation.ToString());

            Assert.Equal(2, await db.Context.Employees.CountAsync());

            // --- Tuesday was a mistake: go back to Monday ---------------------------------------
            var restored = await service.RestoreAsync(monday.Value!.BackupPath, confirmed: true);
            Assert.True(restored.Succeeded, restored.Validation.ToString());

            using var reopened = OpenAgain(databasePath!);

            var names = await reopened.Employees.AsNoTracking()
                .Select(e => e.EmployeeNumber).ToListAsync();

            Assert.Contains("EMP-MON", names);
            Assert.DoesNotContain("EMP-TUE", names);

            // The schema is intact and the application can start on it: every migration applied,
            // nothing pending.
            Assert.Empty(await reopened.Database.GetPendingMigrationsAsync());
            Assert.NotEmpty(await reopened.Database.GetAppliedMigrationsAsync());
            Assert.NotEmpty(await reopened.StatutoryRules.AsNoTracking().ToListAsync());

            // The database that was replaced is kept, so Tuesday is not lost either.
            Assert.True(File.Exists(restored.Value!));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_restore_that_cannot_proceed_leaves_the_working_database_untouched()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;

        var employees = new Application.Employees.EmployeeService(db.Context, db.User, db.Clock);
        Assert.True((await employees.CreateAsync(
            Person(companyId, "EMP-LIVE", "63-3333333 C 33"))).Succeeded);
        db.Context.ChangeTracker.Clear();

        var service = new BackupService(db.Context, db.User, db.Clock);
        var databasePath = service.DatabasePath()!;
        var root = Path.Combine(Path.GetTempPath(), $"tawaka-qa-badbackup-{Guid.NewGuid():N}");

        try
        {
            // A folder that looks like a backup but is not one.
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(
                Path.Combine(root, BackupManifest.FileName), "{ not a manifest ");

            var inspection = await service.InspectAsync(root);
            Assert.False(inspection.CanRestore);

            var restore = await service.RestoreAsync(root, confirmed: true);
            Assert.False(restore.Succeeded);

            // Nothing was moved, nothing was replaced, and the data is still there.
            Assert.True(File.Exists(databasePath));
            Assert.Empty(Directory.GetFiles(
                Path.GetDirectoryName(databasePath)!,
                $"{Path.GetFileName(databasePath)}.replaced-*"));

            using var reopened = OpenAgain(databasePath);
            Assert.Equal(
                "EMP-LIVE",
                await reopened.Employees.AsNoTracking().Select(e => e.EmployeeNumber).SingleAsync());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_backup_records_what_it_needs_to_be_restored_onto_the_right_version()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;

        var service = new BackupService(db.Context, db.User, db.Clock);
        var root = Path.Combine(Path.GetTempPath(), $"tawaka-qa-manifest-{Guid.NewGuid():N}");

        try
        {
            var backup = await service.BackupAsync(root, "Month end");
            Assert.True(backup.Succeeded, backup.Validation.ToString());

            var manifest = backup.Value!.Manifest;
            Assert.Equal("Tawaka Payroll", manifest.Application);
            Assert.Equal(db.User.UserName, manifest.TakenBy);
            Assert.Equal(db.Clock.Now, manifest.TakenAt);
            Assert.True(manifest.DatabaseBytes > 0);
            Assert.Equal(
                await db.Context.Database.GetAppliedMigrationsAsync(),
                manifest.AppliedMigrations);
            Assert.Equal(
                manifest.AppliedMigrations[^1],
                manifest.SchemaVersion);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
