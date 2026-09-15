using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Release;
using Tawaka.Domain.Statutory;
using Tawaka.Infrastructure.Administration;
using Tawaka.Infrastructure.Seeding;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// Structural guarantees: the schema, the scoping, the release gate and the backup.
/// </summary>
public class IntegrityTests : EmployeeTestBase
{
    [Fact]
    public async Task Foreign_keys_are_enforced_by_the_database_itself()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        await using var connection = new SqliteConnection(db.Context.Database.GetConnectionString());
        await connection.OpenAsync();

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, Convert.ToInt64(await pragma.ExecuteScalarAsync()));

        // And they bite: a time entry pointing at no timesheet is refused by SQLite, not merely
        // discouraged by the application.
        await using var insert = connection.CreateCommand();
        insert.CommandText =
            "INSERT INTO TimeEntries (Id, TimesheetId, WorkDate, OrdinaryHours, DaysWorked, " +
            "IsPublicHoliday, IsAbsence) VALUES (@id, @orphan, '2026-09-01', 0, 0, 0, 0);";
        insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        insert.Parameters.AddWithValue("@orphan", Guid.NewGuid().ToString());

        await Assert.ThrowsAsync<SqliteException>(() => insert.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Every_company_scoped_table_carries_a_company()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;

        await new DemoDataSeeder(db.Context, db.Clock).SeedAsync();
        db.Context.ChangeTracker.Clear();

        // Nothing seeded belongs to a different company, or to none.
        Assert.All(await db.Context.Employees.AsNoTracking().ToListAsync(),
            e => Assert.Equal(companyId, e.CompanyId));
        Assert.All(await db.Context.EmployeeContracts.AsNoTracking().ToListAsync(),
            c => Assert.Equal(companyId, c.CompanyId));
        Assert.All(await db.Context.Projects.AsNoTracking().ToListAsync(),
            p => Assert.Equal(companyId, p.CompanyId));
        Assert.All(await db.Context.LeaveTypes.AsNoTracking().ToListAsync(),
            t => Assert.Equal(companyId, t.CompanyId));
        Assert.All(await db.Context.EmployeeLoans.AsNoTracking().ToListAsync(),
            l => Assert.Equal(companyId, l.CompanyId));
        Assert.All(await db.Context.Timesheets.AsNoTracking().ToListAsync(),
            t => Assert.Equal(companyId, t.CompanyId));
        Assert.All(await db.Context.HolidayCalendars.AsNoTracking().ToListAsync(),
            c => Assert.Equal(companyId, c.CompanyId));
    }

    /// <summary>
    /// A national rule has no company; an employer-specific one does. Both must be possible, and
    /// the distinction is what lets an APWCS rate coexist with a PAYE table.
    /// </summary>
    [Fact]
    public async Task National_rules_are_company_neutral_and_employer_rules_are_not()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var rules = await db.Context.StatutoryRules.AsNoTracking().ToListAsync();

        Assert.NotEmpty(rules);
        Assert.All(rules, r => Assert.Null(r.CompanyId));
    }

    [Fact]
    public async Task Currency_bearing_columns_never_hold_a_blank()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        await new DemoDataSeeder(db.Context, db.Clock).SeedAsync();
        db.Context.ChangeTracker.Clear();

        Assert.All(await db.Context.EmployeeContracts.AsNoTracking().ToListAsync(),
            c => Assert.False(string.IsNullOrWhiteSpace(c.PayrollCurrency)));
        Assert.All(await db.Context.EmployeeLoans.AsNoTracking().ToListAsync(),
            l => Assert.False(string.IsNullOrWhiteSpace(l.CurrencyCode)));
        Assert.All(await db.Context.LoanTransactions.AsNoTracking().ToListAsync(),
            t => Assert.False(string.IsNullOrWhiteSpace(t.CurrencyCode)));
    }

    // ---- Release readiness ------------------------------------------------------------------

    [Fact]
    public async Task A_fresh_installation_reports_itself_as_development()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var readiness = await new ReleaseReadinessService(db.Context).EvaluateAsync();

        Assert.Equal(ReleaseStage.Development, readiness.Stage);
        Assert.Contains(readiness.Blockers, b => b.Area == "Company");
        Assert.Contains(readiness.Blockers, b => b.Area == "Employees");
    }

    [Fact]
    public async Task A_configured_installation_with_unverified_rules_says_so()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;

        await new DemoDataSeeder(db.Context, db.Clock).SeedAsync();
        db.Context.ChangeTracker.Clear();

        var readiness = await new ReleaseReadinessService(db.Context).EvaluateAsync();

        Assert.Equal(ReleaseStage.ComplianceUnverified, readiness.Stage);
        Assert.True(readiness.RulesUnverified > 0);
        Assert.Contains(readiness.ComplianceBlockers, b => b.Area == "Statutory rules");
    }

    /// <summary>
    /// The gate that must not be reachable by a switch: live payroll is refused while any rule is
    /// unverified, whatever reason is given.
    /// </summary>
    [Fact]
    public async Task Live_payroll_cannot_be_enabled_while_a_rule_is_unverified()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var service = new ReleaseReadinessService(db.Context);
        var result = await service.SetLivePayrollAsync(true, "The payroll is late.");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("unverified"));
    }

    [Fact]
    public async Task Enabling_live_payroll_requires_a_recorded_reason()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;

        await VerifyAllAsync(db);
        var service = new ReleaseReadinessService(db.Context);

        Assert.False((await service.SetLivePayrollAsync(true, string.Empty)).IsValid);
        Assert.True((await service.SetLivePayrollAsync(
            true, "Parallel payrolls reconciled for September and October 2026.")).IsValid);
    }

    [Fact]
    public async Task A_verified_and_configured_installation_reaches_controlled_testing()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;

        await new DemoDataSeeder(db.Context, db.Clock).SeedAsync();
        await VerifyAllAsync(db);

        var readiness = await new ReleaseReadinessService(db.Context).EvaluateAsync();

        // Verified and configured is as far as the software can take itself. Going live is a
        // decision a person records.
        Assert.Equal(ReleaseStage.ReadyForControlledTesting, readiness.Stage);
    }

    // ---- Backup and restore -------------------------------------------------------------------

    [Fact]
    public async Task A_backup_is_written_with_a_manifest_and_restores_cleanly()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;

        await new DemoDataSeeder(db.Context, db.Clock).SeedAsync();
        db.Context.ChangeTracker.Clear();

        var folder = Path.Combine(Path.GetTempPath(), $"tawaka-backup-test-{Guid.NewGuid():N}");
        var service = new BackupService(db.Context, db.User, db.Clock);

        try
        {
            var backup = await service.BackupAsync(folder, "Before the October payroll");
            Assert.True(backup.Succeeded, backup.Validation.ToString());
            Assert.True(File.Exists(Path.Combine(backup.Value!.BackupPath, BackupManifest.FileName)));
            Assert.NotEmpty(backup.Value.Manifest.AppliedMigrations);
            Assert.Equal("Before the October payroll", backup.Value.Manifest.Notes);

            var inspection = await service.InspectAsync(backup.Value.BackupPath);
            Assert.True(inspection.CanRestore);
            Assert.Empty(inspection.Problems);

            // Restoring without confirming is refused: this replaces every payroll in the system.
            var unconfirmed = await service.RestoreAsync(backup.Value.BackupPath, confirmed: false);
            Assert.False(unconfirmed.Succeeded);

            var restored = await service.RestoreAsync(backup.Value.BackupPath, confirmed: true);
            Assert.True(restored.Succeeded, restored.Validation.ToString());

            // The database that was in use is kept, not discarded.
            Assert.True(File.Exists(restored.Value!));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_folder_that_is_not_a_backup_is_refused_rather_than_attempted()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var folder = Path.Combine(Path.GetTempPath(), $"tawaka-not-a-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            var service = new BackupService(db.Context, db.User, db.Clock);
            var inspection = await service.InspectAsync(folder);

            Assert.False(inspection.IsReadable);
            Assert.False(inspection.CanRestore);

            var restore = await service.RestoreAsync(folder, confirmed: true);
            Assert.False(restore.Succeeded);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_backup_whose_database_has_been_altered_is_refused()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var folder = Path.Combine(Path.GetTempPath(), $"tawaka-backup-test-{Guid.NewGuid():N}");
        var service = new BackupService(db.Context, db.User, db.Clock);

        try
        {
            var backup = await service.BackupAsync(folder);
            var file = Path.Combine(
                backup.Value!.BackupPath, backup.Value.Manifest.DatabaseFileName);

            await File.AppendAllTextAsync(file, "tampered");

            var inspection = await service.InspectAsync(backup.Value.BackupPath);
            Assert.False(inspection.CanRestore);
            Assert.Contains(inspection.Problems, p => p.Contains("does not match the manifest"));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static async Task VerifyAllAsync(TestDatabase db)
    {
        foreach (var rule in await db.Context.StatutoryRules.ToListAsync())
        {
            if (rule.VerificationStatus != VerificationStatus.Disabled)
            {
                rule.VerificationStatus = VerificationStatus.Verified;
            }
        }

        var company = await db.Context.Companies.FirstAsync();
        company.LegalName = "Tawaka Construction (Private) Limited";
        company.TaxNumber = "BP1234567";

        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
