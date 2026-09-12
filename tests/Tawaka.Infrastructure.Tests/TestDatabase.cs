using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Domain.Statutory;
using Tawaka.Infrastructure.Interceptors;
using Tawaka.Infrastructure.Persistence;

namespace Tawaka.Infrastructure.Tests;

public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; }

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
}

public sealed class TestUser : ICurrentUser
{
    public TestUser(string userId = "u-payroll", string userName = "R. Nyakuhwa")
    {
        UserId = userId;
        UserName = userName;
    }

    public string UserId { get; }

    public string UserName { get; }

    public string? Machine => "TEST";

    public bool HasPermission(string permissionCode) => true;
}

/// <summary>
/// A real SQLite database per test, created by running the actual migrations, so the migration
/// itself is under test rather than an EnsureCreated approximation of it.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _path;

    public TestDatabase(ICurrentUser? user = null)
    {
        _path = Path.Combine(Path.GetTempPath(), $"tawaka-test-{Guid.NewGuid():N}.db");
        User = user ?? new TestUser();
        LockOverride = new LockOverride();

        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseSqlite($"Data Source={_path}")
            .AddInterceptors(
                new PeriodLockInterceptor(LockOverride),
                new AuditInterceptor(User, Clock))
            .Options;

        Context = new PayrollDbContext(options);
        Context.Database.Migrate();
    }

    public PayrollDbContext Context { get; }

    public ICurrentUser User { get; }

    public LockOverride LockOverride { get; }

    public IClock Clock { get; } = new FixedClock(new DateTimeOffset(2026, 9, 12, 14, 32, 0, TimeSpan.Zero));

    public IReadOnlyList<string> TableNames()
    {
        var names = new List<string>();
        using var command = Context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' " +
            "AND name <> '__EFMigrationsHistory' ORDER BY name";
        Context.Database.OpenConnection();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public IReadOnlyList<StatutoryRule> AllRules() => Context.StatutoryRules.AsNoTracking().ToList();

    public void Dispose()
    {
        Context.Database.CloseConnection();
        Context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
                // A leftover temp file is harmless.
            }
        }
    }
}
