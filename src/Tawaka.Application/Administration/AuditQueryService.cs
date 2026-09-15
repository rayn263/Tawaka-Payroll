using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Security;
using Tawaka.Domain.Audit;
using Tawaka.Domain.Security;

namespace Tawaka.Application.Administration;

public sealed record AuditFilter
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? UserId { get; init; }
    public string? EntityName { get; init; }
    public string? EntityId { get; init; }
    public AuditAction? Action { get; init; }

    /// <summary>Free text matched against the field, old value, new value and reason.</summary>
    public string? Search { get; init; }

    public int Take { get; init; } = 300;
}

/// <summary>One audit entry with its actor resolved to a person.</summary>
public sealed record AuditEntryView(
    DateTimeOffset OccurredAt,
    string ActorId,
    string ActorName,
    AuditAction Action,
    string EntityName,
    string EntityId,
    string? FieldName,
    string? OldValue,
    string? NewValue,
    string? Reason,
    string? Machine);

/// <summary>
/// Reads the audit trail.
/// <para>
/// The trail stores the actor's id, which is a GUID. An auditor asking "who changed this salary"
/// is not helped by a GUID, so every entry is resolved against the user directory here — and falls
/// back to the name captured at the time if the user record has since gone.
/// </para>
/// </summary>
public sealed class AuditQueryService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly UserDirectory _directory;

    public AuditQueryService(
        IPayrollDataContext context, ICurrentUser currentUser, UserDirectory directory)
    {
        _context = context;
        _currentUser = currentUser;
        _directory = directory;
    }

    public async Task<IReadOnlyList<AuditEntryView>> QueryAsync(
        AuditFilter filter, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.AuditView);

        var query = _context.AuditLogs.AsNoTracking().AsQueryable();

        if (filter.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            query = query.Where(a => a.OccurredAt >= start);
        }

        if (filter.To is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(a => a.OccurredAt < end);
        }

        if (!string.IsNullOrWhiteSpace(filter.UserId))
        {
            query = query.Where(a => a.UserId == filter.UserId);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityName))
        {
            query = query.Where(a => a.EntityName == filter.EntityName);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            query = query.Where(a => a.EntityId == filter.EntityId);
        }

        if (filter.Action is { } action)
        {
            query = query.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search;
            query = query.Where(a =>
                (a.FieldName != null && a.FieldName.Contains(term)) ||
                (a.OldValue != null && a.OldValue.Contains(term)) ||
                (a.NewValue != null && a.NewValue.Contains(term)) ||
                (a.Reason != null && a.Reason.Contains(term)));
        }

        var rows = await query
            .OrderByDescending(a => a.OccurredAt)
            .Take(Math.Clamp(filter.Take, 1, 2000))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var names = await _directory.ResolveAsync(
            rows.Select(r => r.UserId), cancellationToken).ConfigureAwait(false);

        return rows.Select(row => new AuditEntryView(
            row.OccurredAt,
            row.UserId,
            names.TryGetValue(row.UserId, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : FallbackName(row),
            row.Action,
            row.EntityName,
            row.EntityId,
            row.FieldName,
            row.OldValue,
            row.NewValue,
            row.Reason,
            row.Machine)).ToList();
    }

    /// <summary>The entity names that actually appear, so the filter offers real choices.</summary>
    public Task<List<string>> GetEntityNamesAsync(CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.AuditView);

        return _context.AuditLogs.AsNoTracking()
            .Select(a => a.EntityName)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The audit history of one record, oldest first — what an auditor reconstructing a payroll,
    /// a salary change or an obligation actually needs.
    /// </summary>
    public async Task<IReadOnlyList<AuditEntryView>> GetHistoryAsync(
        string entityName, Guid entityId, CancellationToken cancellationToken = default)
    {
        var entries = await QueryAsync(new AuditFilter
        {
            EntityName = entityName,
            EntityId = entityId.ToString(),
            Take = 2000
        }, cancellationToken).ConfigureAwait(false);

        return entries.OrderBy(e => e.OccurredAt).ToList();
    }

    /// <summary>
    /// The name recorded on the entry itself. Kept because a user record can be removed, and an
    /// audit trail that forgets who did something is not an audit trail.
    /// </summary>
    private static string FallbackName(AuditLog row) =>
        string.IsNullOrWhiteSpace(row.UserName) ? row.UserId : row.UserName;
}
