using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tawaka.Domain.Common;

namespace Tawaka.Infrastructure.Interceptors;

/// <summary>
/// Refuses modification or deletion of any entity that is locked. Enforcement lives here rather
/// than in the UI so that locked payroll history cannot change by any route.
/// </summary>
public sealed class PeriodLockInterceptor : SaveChangesInterceptor
{
    private readonly ILockOverride _lockOverride;

    public PeriodLockInterceptor(ILockOverride lockOverride)
    {
        _lockOverride = lockOverride;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Guard(DbContext? context)
    {
        if (context is null || _lockOverride.IsActive)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not ILockable lockable)
            {
                continue;
            }

            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            // Test the value as it was loaded: an entity that is being locked right now is a
            // legitimate transition, whereas one that was already locked is not touchable.
            if (WasLockedWhenLoaded(entry, lockable))
            {
                throw new PeriodLockedException(
                    entry.Entity.GetType().Name,
                    entry.Entity is Entity e ? e.Id.ToString() : "(unknown)");
            }
        }
    }

    private static bool WasLockedWhenLoaded(EntityEntry entry, ILockable current)
    {
        var statusProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Status");
        if (statusProperty is null)
        {
            return current.IsLocked;
        }

        var original = statusProperty.OriginalValue?.ToString();
        return string.Equals(original, "Locked", StringComparison.Ordinal);
    }
}

/// <summary>
/// Allows an authorised, audited operation (reopening a locked payroll) to write to locked data.
/// Scoped and explicit, so bypassing the lock is always a deliberate act.
/// </summary>
public interface ILockOverride
{
    bool IsActive { get; }

    IDisposable Begin(string reason);
}

public sealed class LockOverride : ILockOverride
{
    private int _depth;

    public bool IsActive => _depth > 0;

    public string? CurrentReason { get; private set; }

    public IDisposable Begin(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Reopening locked payroll requires a reason.", nameof(reason));
        }

        _depth++;
        CurrentReason = reason;
        return new Scope(this);
    }

    private sealed class Scope : IDisposable
    {
        private readonly LockOverride _owner;
        private bool _disposed;

        public Scope(LockOverride owner) => _owner = owner;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner._depth--;
            if (_owner._depth == 0)
            {
                _owner.CurrentReason = null;
            }
        }
    }
}
