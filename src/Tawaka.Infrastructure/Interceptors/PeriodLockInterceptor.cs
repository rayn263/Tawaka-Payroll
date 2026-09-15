using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tawaka.Domain.Common;
using Tawaka.Domain.Payroll;

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

        var changed = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in changed)
        {
            if (entry.Entity is not ILockable lockable)
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

        GuardResultRows(context, changed);
    }

    /// <summary>
    /// Protects the figures underneath a locked run. The run row carries the lock, but the result
    /// rows — the per-employee results, their lines, traces, unresolved items and allocations —
    /// carry no status of their own, so without this a locked run's header would be frozen while
    /// the numbers it locked could still be edited.
    /// <para>
    /// Statutory obligations and payments are deliberately excluded: settling an authority happens
    /// after a run is locked, and must keep working.
    /// </para>
    /// </summary>
    private static void GuardResultRows(DbContext context, List<EntityEntry> changed)
    {
        var runIds = changed
            .Where(e => e.Entity is PayrollRunEmployee)
            .Select(e => ((PayrollRunEmployee)e.Entity).PayrollRunId)
            .ToHashSet();

        var runEmployeeIds = changed
            .Where(e => e.Entity is IPayrollResultRow)
            .Select(e => ((IPayrollResultRow)e.Entity).PayrollRunEmployeeId)
            .ToHashSet();

        if (runIds.Count == 0 && runEmployeeIds.Count == 0)
        {
            return;
        }

        if (runEmployeeIds.Count > 0)
        {
            var parents = context.Set<PayrollRunEmployee>().AsNoTracking()
                .Where(e => runEmployeeIds.Contains(e.Id))
                .Select(e => e.PayrollRunId)
                .ToList();

            foreach (var parent in parents)
            {
                runIds.Add(parent);
            }
        }

        var locked = context.Set<PayrollRun>().AsNoTracking()
            .Where(r => runIds.Contains(r.Id) && r.Status == PayrollRunStatus.Locked)
            .Select(r => r.Id)
            .ToHashSet();

        if (locked.Count == 0)
        {
            return;
        }

        foreach (var entry in changed)
        {
            var belongsToLockedRun = entry.Entity switch
            {
                PayrollRunEmployee employee => locked.Contains(employee.PayrollRunId),
                IPayrollResultRow row => IsUnderLockedRun(context, row, locked),
                _ => false
            };

            if (belongsToLockedRun)
            {
                throw new PeriodLockedException(
                    entry.Entity.GetType().Name,
                    entry.Entity is Entity e ? e.Id.ToString() : "(unknown)");
            }
        }
    }

    private static bool IsUnderLockedRun(
        DbContext context, IPayrollResultRow row, IReadOnlySet<Guid> lockedRunIds) =>
        context.Set<PayrollRunEmployee>().AsNoTracking()
            .Any(e => e.Id == row.PayrollRunEmployeeId && lockedRunIds.Contains(e.PayrollRunId));

    /// <summary>
    /// Tests the status as it was <em>loaded</em>, not as it stands now. Locking something is a
    /// legitimate transition and must be allowed to save; touching something that was already
    /// locked is not.
    /// </summary>
    private static bool WasLockedWhenLoaded(EntityEntry entry, ILockable current)
    {
        // Payroll runs and periods carry "Status"; inputs carry "ApprovalStatus". Both end in a
        // Locked member, and both need the original value rather than the pending one.
        var statusProperty =
            entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Status") ??
            entry.Properties.FirstOrDefault(p => p.Metadata.Name == "ApprovalStatus");

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
