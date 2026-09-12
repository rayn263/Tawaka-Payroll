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
    private readonly HashSet<string>? _permissions;

    public TestUser(string userId = "u-payroll", string userName = "R. Nyakuhwa")
    {
        UserId = userId;
        UserName = userName;
    }

    private TestUser(string userId, string userName, IEnumerable<string> permissions)
        : this(userId, userName)
    {
        _permissions = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A user holding only the named permissions, for testing permission enforcement.</summary>
    public static TestUser WithPermissions(params string[] permissions) =>
        new("u-limited", "Limited User", permissions);

    public string UserId { get; }

    public string UserName { get; }

    public string? Machine => "TEST";

    /// <summary>Holds every permission unless a specific set was supplied.</summary>
    public bool HasPermission(string permissionCode) =>
        _permissions is null || _permissions.Contains(permissionCode);
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

    public Tawaka.Application.Security.PasswordHasher Hasher { get; } =
        new(iterations: 15_000);

    /// <summary>Runs first-run seeding: statutory rules, the company, and security.</summary>
    public async Task<Tawaka.Infrastructure.Seeding.SeedOutcome> SeedAllAsync()
    {
        var statutory = new Tawaka.Infrastructure.Seeding.StatutoryRuleSeeder(Context);
        var company = new Tawaka.Infrastructure.Seeding.CompanySeeder(Context);
        var security = new Tawaka.Infrastructure.Seeding.SecuritySeeder(Context, Hasher);
        var seeder = new Tawaka.Infrastructure.Seeding.ApplicationSeeder(statutory, company, security);
        return await seeder.SeedAsync();
    }

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
