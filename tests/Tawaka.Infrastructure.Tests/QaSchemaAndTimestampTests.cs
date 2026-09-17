using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Audit;
using Tawaka.Infrastructure.Persistence;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

/// <summary>
/// What the migrations actually build, checked against the database rather than against the model:
/// the indexes, the unique constraints, the required columns and the deletion rules.
/// </summary>
public class QaSchemaAndTimestampTests : EmployeeTestBase
{
    private static async Task<List<string>> ScalarsAsync(TestDatabase db, string sql)
    {
        var values = new List<string>();
        await db.Context.Database.OpenConnectionAsync();

        await using var command = db.Context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static async Task<Exception?> ExecuteAsync(TestDatabase db, string sql)
    {
        try
        {
            await db.Context.Database.ExecuteSqlRawAsync(sql);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task A_database_built_from_zero_has_every_migration_and_none_pending()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var applied = await db.Context.Database.GetAppliedMigrationsAsync();
        var pending = await db.Context.Database.GetPendingMigrationsAsync();

        Assert.Empty(pending);
        Assert.Equal(
            db.Context.Database.GetMigrations().ToList(),
            applied.ToList());

        // Applied in order, oldest first, with the initial migration at the front.
        Assert.StartsWith("20260912221408_", applied.First());
        Assert.Equal(applied.OrderBy(m => m, StringComparer.Ordinal), applied);
    }

    [Fact]
    public async Task The_indexes_the_application_relies_on_exist()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var indexes = await ScalarsAsync(db,
            "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%'");

        foreach (var expected in new[]
                 {
                     "IX_Employees_CompanyId_EmployeeNumber",
                     "IX_EmployeeContracts_EmployeeId_VersionNumber",
                     "IX_PayrollPeriods_CompanyId_Code",
                     "IX_PayrollRuns_PayrollPeriodId_RunNumber",
                     "IX_PayrollRunEmployees_PayrollRunId_EmployeeId",
                     "IX_EmployeeLoans_CompanyId_LoanNumber",
                     "IX_GlAccountMappings_CompanyId_MappingType_CurrencyCode",
                     "IX_Users_Username",
                     "IX_Permissions_Code"
                 })
        {
            Assert.Contains(expected, indexes);
        }

        // The audit trail is queried by entity and by time, so it is indexed that way.
        Assert.Contains(indexes, name => name.StartsWith("IX_AuditLogs_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_unique_constraint_is_enforced_by_the_database_not_only_by_the_service()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;

        var employees = new Application.Employees.EmployeeService(db.Context, db.User, db.Clock);
        Assert.True((await employees.CreateAsync(NewEmployee(companyId, "EMP-UNIQUE"))).Succeeded);
        db.Context.ChangeTracker.Clear();

        // Straight past the service's own duplicate check, as an import or a script would.
        var duplicate = NewEmployee(companyId, "EMP-UNIQUE");
        duplicate.NationalId = "63-8888888 Z 99";
        db.Context.Employees.Add(duplicate);

        var error = await Record.ExceptionAsync(() => db.Context.SaveChangesAsync());

        Assert.NotNull(error);
        Assert.Contains("UNIQUE", Innermost(error!).Message, StringComparison.OrdinalIgnoreCase);
        db.Context.ChangeTracker.Clear();
        Assert.Equal(1, await db.Context.Employees.CountAsync());
    }

    [Fact]
    public async Task A_required_column_refuses_a_row_without_it()
    {
        var (db, companyId, _, _) = await SetUpAsync();
        using var _db = db;

        var nameless = NewEmployee(companyId);
        nameless.EmployeeNumber = null!;
        db.Context.Employees.Add(nameless);

        var error = await Record.ExceptionAsync(() => db.Context.SaveChangesAsync());

        Assert.NotNull(error);
        Assert.Contains("NOT NULL", Innermost(error!).Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An employee is never deleted — they are terminated and their record kept. The database
    /// enforces that too: a row something else depends on cannot be removed behind the
    /// application's back.
    /// </summary>
    [Fact]
    public async Task An_employee_with_history_cannot_simply_be_deleted()
    {
        var (db, companyId, permanentTypeId, _) = await SetUpAsync();
        using var _db = db;

        var employees = new Application.Employees.EmployeeService(db.Context, db.User, db.Clock);
        var contracts = new Application.Employees.EmployeeContractService(db.Context, db.User);

        var employee = (await employees.CreateAsync(NewEmployee(companyId))).Value!;
        Assert.True((await contracts.CreateInitialAsync(
            NewContract(companyId, employee.Id, permanentTypeId))).Succeeded);

        db.Context.ChangeTracker.Clear();

        // Microsoft.Data.Sqlite stores a Guid as uppercase text, so that is how it has to be
        // matched from hand-written SQL.
        var error = await ExecuteAsync(db,
            $"DELETE FROM Employees WHERE Id = '{employee.Id.ToString().ToUpperInvariant()}'");

        Assert.IsType<SqliteException>(error);
        Assert.Contains("FOREIGN KEY", error!.Message, StringComparison.OrdinalIgnoreCase);

        db.Context.ChangeTracker.Clear();
        Assert.Equal(1, await db.Context.Employees.CountAsync());
    }

    private static Exception Innermost(Exception exception)
    {
        while (exception.InnerException is not null)
        {
            exception = exception.InnerException;
        }

        return exception;
    }

    // ---- Timestamps (ADR-040) --------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-5)]
    [InlineData(13)]
    public void A_timestamp_round_trips_through_storage_with_its_offset(int offsetHours)
    {
        var original = new DateTimeOffset(
            2026, 9, 17, 14, 32, 11, 1234567 / 10000, TimeSpan.FromHours(offsetHours));

        var stored = SortableDateTimeOffsetConverter.Write(original);
        var read = SortableDateTimeOffsetConverter.Read(stored);

        Assert.Equal(original, read);
        Assert.Equal(original.Offset, read.Offset);
        Assert.Equal(SortableDateTimeOffsetConverter.Width, stored.Length);
    }

    [Fact]
    public void Stored_timestamps_sort_chronologically_even_across_different_offsets()
    {
        // The same instant written from two machines in different time zones, and one an hour later.
        var harare = new DateTimeOffset(2026, 9, 17, 14, 0, 0, TimeSpan.FromHours(2));
        var london = new DateTimeOffset(2026, 9, 17, 13, 0, 0, TimeSpan.FromHours(1));
        var later = new DateTimeOffset(2026, 9, 17, 16, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(harare, london);
        Assert.Equal(
            SortableDateTimeOffsetConverter.Write(harare)[..28],
            SortableDateTimeOffsetConverter.Write(london)[..28]);

        Assert.True(string.CompareOrdinal(
            SortableDateTimeOffsetConverter.Write(harare),
            SortableDateTimeOffsetConverter.Write(later)) < 0);
    }

    /// <summary>
    /// The defect this guards against took out every screen in the application: SQLite refuses to
    /// order by EF's own DateTimeOffset mapping, and the refusal only appears at query time.
    /// </summary>
    [Fact]
    public async Task The_database_can_order_by_a_timestamp_and_puts_them_in_the_right_order()
    {
        var (db, _, _, _) = await SetUpAsync();
        using var _db = db;

        var instants = new[]
        {
            new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 1, 1, 23, 0, 0, TimeSpan.FromHours(-5)),
            new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero)
        };

        var index = 0;
        foreach (var instant in instants)
        {
            db.Context.AuditLogs.Add(new AuditLog
            {
                OccurredAt = instant,
                UserId = "qa",
                UserName = "QA",
                EntityName = "Ordering",
                EntityId = (++index).ToString(),
                Action = AuditAction.Create
            });
        }

        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        // Ordered by the database, not in memory.
        var ordered = await db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "Ordering")
            .OrderBy(a => a.OccurredAt)
            .Select(a => a.OccurredAt)
            .ToListAsync();

        Assert.Equal(instants.OrderBy(i => i).ToList(), ordered);

        var newest = await db.Context.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "Ordering")
            .OrderByDescending(a => a.OccurredAt)
            .FirstAsync();

        Assert.Equal(instants.Max(), newest.OccurredAt);
    }
}
