using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tawaka.Application.Abstractions;
using Tawaka.Domain.Audit;
using Tawaka.Domain.Common;

namespace Tawaka.Infrastructure.Interceptors;

/// <summary>
/// Stamps audit fields and writes one append-only <see cref="AuditLog"/> row per changed field.
/// Runs at the data layer so every route into the database is covered (ADR-006).
/// </summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    /// <summary>Fields whose values are never copied into the audit trail.</summary>
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash",
        "PasswordSalt"
    };

    public AuditInterceptor(ICurrentUser currentUser, IClock clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.Now;
        var correlationId = Guid.NewGuid();
        var logs = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            // The audit trail never audits itself.
            if (entry.Entity is AuditLog)
            {
                continue;
            }

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            StampAuditableFields(entry, now);
            logs.AddRange(BuildLogs(entry, now, correlationId));
        }

        if (logs.Count > 0)
        {
            context.Set<AuditLog>().AddRange(logs);
        }
    }

    private void StampAuditableFields(EntityEntry entry, DateTimeOffset now)
    {
        if (entry.Entity is not AuditableEntity auditable)
        {
            return;
        }

        switch (entry.State)
        {
            case EntityState.Added:
                auditable.CreatedAt = now;
                auditable.CreatedBy = _currentUser.UserId;
                break;
            case EntityState.Modified:
                auditable.ModifiedAt = now;
                auditable.ModifiedBy = _currentUser.UserId;
                break;
        }
    }

    private IEnumerable<AuditLog> BuildLogs(
        EntityEntry entry, DateTimeOffset now, Guid correlationId)
    {
        var entityName = entry.Entity.GetType().Name;
        var entityId = entry.Entity is Entity e ? e.Id.ToString() : "(keyless)";

        switch (entry.State)
        {
            case EntityState.Added:
                yield return NewLog(AuditAction.Create, entityName, entityId, now, correlationId);
                break;

            case EntityState.Deleted:
                yield return NewLog(AuditAction.Delete, entityName, entityId, now, correlationId);
                break;

            case EntityState.Modified:
                foreach (var property in entry.Properties)
                {
                    if (!property.IsModified)
                    {
                        continue;
                    }

                    var name = property.Metadata.Name;
                    if (name is "ModifiedAt" or "ModifiedBy")
                    {
                        continue;
                    }

                    var oldValue = Format(name, property.OriginalValue);
                    var newValue = Format(name, property.CurrentValue);
                    if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var log = NewLog(AuditAction.Update, entityName, entityId, now, correlationId);
                    log.FieldName = name;
                    log.OldValue = oldValue;
                    log.NewValue = newValue;
                    yield return log;
                }

                break;
        }
    }

    private static string? Format(string fieldName, object? value)
    {
        if (SensitiveFields.Contains(fieldName))
        {
            return "(redacted)";
        }

        return value?.ToString();
    }

    private AuditLog NewLog(
        AuditAction action,
        string entityName,
        string entityId,
        DateTimeOffset now,
        Guid correlationId) => new()
        {
            OccurredAt = now,
            UserId = _currentUser.UserId,
            UserName = _currentUser.UserName,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            Machine = _currentUser.Machine,
            CorrelationId = correlationId
        };
}
