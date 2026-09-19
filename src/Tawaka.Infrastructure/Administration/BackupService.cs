using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Audit;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Security;
using Tawaka.Domain.Security;
using Tawaka.Infrastructure.Persistence;

namespace Tawaka.Infrastructure.Administration;

/// <summary>What a backup carries beside the database file, so a restore can check itself.</summary>
public sealed record BackupManifest
{
    public required string Application { get; init; }
    public required string DatabaseFileName { get; init; }
    public required DateTimeOffset TakenAt { get; init; }
    public required string TakenBy { get; init; }
    public required string SchemaVersion { get; init; }
    public required IReadOnlyList<string> AppliedMigrations { get; init; }
    public required long DatabaseBytes { get; init; }
    public string? CompanyName { get; init; }
    public string? Notes { get; init; }

    public const string FileName = "tawaka-backup.json";
}

public sealed record BackupResult(string BackupPath, BackupManifest Manifest);

public sealed record RestoreInspection(
    bool IsReadable, BackupManifest? Manifest, IReadOnlyList<string> Problems)
{
    public bool CanRestore => IsReadable && Problems.Count == 0;
}

/// <summary>
/// Backing up and restoring the SQLite database.
/// <para>
/// The rules that matter: a backup is a consistent copy taken through SQLite's own backup
/// mechanism rather than a file copy of a database that may be mid-write; a restore never
/// overwrites the live database without first setting the current one aside, so a failed restore
/// leaves the business with something to go back to; and a backup taken on a newer schema is
/// refused rather than restored into an older application.
/// </para>
/// </summary>
public sealed class BackupService
{
    private readonly PayrollDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public BackupService(PayrollDbContext context, ICurrentUser currentUser, IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>
    /// Writes a backup into <paramref name="directory"/>: the database file plus a manifest naming
    /// the schema it was taken on.
    /// </summary>
    public async Task<OperationResult<BackupResult>> BackupAsync(
        string directory, string? notes = null, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.SettingsEdit);

        var validation = ValidationResult.Success();
        validation.Require(directory, "Directory", "A backup folder is required.");

        var databasePath = DatabasePath();
        if (databasePath is null)
        {
            return OperationResult<BackupResult>.Failed(validation.Add("Database",
                "This installation is not backed by a database file, so there is nothing to copy."));
        }

        if (!validation.IsValid)
        {
            return OperationResult<BackupResult>.Failed(validation);
        }

        Directory.CreateDirectory(directory);

        var timestamp = _clock.Now;
        var folderName = $"tawaka-backup-{timestamp:yyyyMMdd-HHmmss}";
        var target = Path.Combine(directory, folderName);
        Directory.CreateDirectory(target);

        var databaseFileName = Path.GetFileName(databasePath);
        var databaseTarget = Path.Combine(target, databaseFileName);

        // SQLite's own backup API, not File.Copy: a copy taken while a write is in flight can be
        // a database that will not open, and nobody discovers that until they need it.
        await BackupDatabaseAsync(databasePath, databaseTarget, cancellationToken)
            .ConfigureAwait(false);

        var migrations = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)
            .ConfigureAwait(false)).ToList();

        var company = await _context.Companies.AsNoTracking()
            .Select(c => c.LegalName)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var manifest = new BackupManifest
        {
            Application = "Tawaka Payroll",
            DatabaseFileName = databaseFileName,
            TakenAt = timestamp,
            TakenBy = _currentUser.UserName,
            SchemaVersion = migrations.LastOrDefault() ?? "(none)",
            AppliedMigrations = migrations,
            DatabaseBytes = new FileInfo(databaseTarget).Length,
            CompanyName = company,
            Notes = notes
        };

        await File.WriteAllTextAsync(
            Path.Combine(target, BackupManifest.FileName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken)
            .ConfigureAwait(false);

        await RecordAsync(
            AuditAction.Export, "Backup", target,
            $"Backup of {manifest.DatabaseBytes:N0} bytes on schema {manifest.SchemaVersion}",
            notes, cancellationToken).ConfigureAwait(false);

        return OperationResult<BackupResult>.Success(new BackupResult(target, manifest));
    }

    /// <summary>
    /// Reads a backup folder and reports whether it can be restored into this application — before
    /// anything is touched. A restore that discovers its problems halfway through is a disaster.
    /// </summary>
    public async Task<RestoreInspection> InspectAsync(
        string backupPath, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.SettingsEdit);

        var problems = new List<string>();
        var manifestPath = Path.Combine(backupPath, BackupManifest.FileName);

        if (!Directory.Exists(backupPath) || !File.Exists(manifestPath))
        {
            return new RestoreInspection(false, null, new[]
            {
                "This folder does not contain a Tawaka Payroll backup manifest."
            });
        }

        BackupManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BackupManifest>(
                await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false));
        }
        catch (JsonException)
        {
            return new RestoreInspection(false, null, new[] { "The backup manifest is unreadable." });
        }

        if (manifest is null)
        {
            return new RestoreInspection(false, null, new[] { "The backup manifest is empty." });
        }

        var databaseFile = Path.Combine(backupPath, manifest.DatabaseFileName);
        if (!File.Exists(databaseFile))
        {
            problems.Add($"The backup names '{manifest.DatabaseFileName}' but that file is missing.");
        }
        else if (new FileInfo(databaseFile).Length != manifest.DatabaseBytes)
        {
            problems.Add(
                "The database file's size does not match the manifest. The backup is incomplete " +
                "or has been altered.");
        }

        // A backup taken on a newer schema cannot be restored into an older application: its
        // tables would not match the model, and the failure would surface as corrupt payroll
        // rather than as an error.
        var known = _context.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var unknown = manifest.AppliedMigrations.Where(m => !known.Contains(m)).ToList();
        if (unknown.Count > 0)
        {
            problems.Add(
                $"The backup was taken on a newer version of Tawaka Payroll ({unknown.Count} " +
                "migrations this build does not have). Upgrade the application before restoring.");
        }

        return new RestoreInspection(true, manifest, problems);
    }

    /// <summary>
    /// Replaces the live database with the backup, after setting the current one aside.
    /// <para>
    /// The displaced database is kept next to the original with a timestamped name. If the restore
    /// fails partway, it is put back. Nothing is ever silently overwritten.
    /// </para>
    /// </summary>
    public async Task<OperationResult<string>> RestoreAsync(
        string backupPath, bool confirmed, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.SettingsEdit);

        var validation = ValidationResult.Success();
        validation.AddIf(!confirmed, "Confirmation",
            "Restoring replaces all current payroll data. Confirm explicitly to proceed.");

        var inspection = await InspectAsync(backupPath, cancellationToken).ConfigureAwait(false);
        foreach (var problem in inspection.Problems)
        {
            validation.Add("Backup", problem);
        }

        if (!inspection.IsReadable)
        {
            validation.Add("Backup", "The backup could not be read.");
        }

        var databasePath = DatabasePath();
        if (databasePath is null)
        {
            validation.Add("Database", "This installation is not backed by a database file.");
        }

        if (!validation.IsValid)
        {
            return OperationResult<string>.Failed(validation);
        }

        var source = Path.Combine(backupPath, inspection.Manifest!.DatabaseFileName);
        var displaced = $"{databasePath}.replaced-{_clock.Now:yyyyMMdd-HHmmss}";

        // Connections are pooled; a file still held open cannot be replaced on Windows.
        SqliteConnection.ClearAllPools();
        await _context.Database.CloseConnectionAsync().ConfigureAwait(false);

        try
        {
            if (File.Exists(databasePath))
            {
                File.Move(databasePath!, displaced);
            }

            File.Copy(source, databasePath!, overwrite: false);
        }
        catch (IOException ex)
        {
            // Put back what was there. A half-restored payroll database is the worst outcome
            // available, so recovery is not optional.
            if (File.Exists(displaced) && !File.Exists(databasePath))
            {
                File.Move(displaced, databasePath!);
            }

            return OperationResult<string>.Failed(validation.Add("Restore",
                $"The restore failed and the previous database has been put back: {ex.Message}"));
        }

        // Written into the database that is now live, so it survives: an entry made before the
        // swap would have gone with the database it was replacing. It is the first thing in the
        // restored history that says the history was replaced.
        await RecordAsync(
            AuditAction.Update, "Restore", backupPath,
            $"Database restored from a backup taken {inspection.Manifest.TakenAt:u} by " +
            $"{inspection.Manifest.TakenBy} on schema {inspection.Manifest.SchemaVersion}. " +
            $"The database in use was set aside as {Path.GetFileName(displaced)}.",
            inspection.Manifest.Notes, cancellationToken).ConfigureAwait(false);

        return OperationResult<string>.Success(displaced);
    }

    /// <summary>
    /// Backing up and restoring are not entity changes, so the audit interceptor cannot see them.
    /// They are recorded here instead, because replacing every payroll in the system is the single
    /// most consequential thing this application can be asked to do.
    /// </summary>
    private async Task RecordAsync(
        AuditAction action, string what, string where, string detail, string? reason,
        CancellationToken cancellationToken)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            OccurredAt = _clock.Now,
            UserId = _currentUser.UserId,
            UserName = _currentUser.UserName,
            Action = action,
            EntityName = what,
            EntityId = where,
            NewValue = detail,
            Reason = reason,
            Machine = _currentUser.Machine
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The database file behind this context, or null where it is not file-backed.</summary>
    public string? DatabasePath()
    {
        var connectionString = _context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        var path = builder.DataSource;

        if (string.IsNullOrWhiteSpace(path) ||
            path.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetFullPath(path);
    }

    private async Task BackupDatabaseAsync(
        string source, string target, CancellationToken cancellationToken)
    {
        await using var sourceConnection = new SqliteConnection($"Data Source={source}");
        await using var targetConnection = new SqliteConnection($"Data Source={target}");

        await sourceConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await targetConnection.OpenAsync(cancellationToken).ConfigureAwait(false);

        sourceConnection.BackupDatabase(targetConnection);
    }
}
