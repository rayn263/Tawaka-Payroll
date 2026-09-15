using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;

namespace Tawaka.Application.Administration;

/// <summary>
/// Resolves user identifiers to display names.
/// <para>
/// Actor fields across the system — who submitted a timesheet, who approved a payroll run, who
/// recorded a statutory payment — store the user's identifier, because that is what stays stable
/// when somebody's name changes. A screen showing a raw GUID is useless to the person who has to
/// read it, so every display resolves through here.
/// </para>
/// <para>
/// Scoped and cached for the lifetime of one request: resolving the same handful of ids once per
/// row would turn an audit page into a few hundred queries.
/// </para>
/// </summary>
public sealed class UserDirectory
{
    private readonly IPayrollDataContext _context;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public UserDirectory(IPayrollDataContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Resolves one identifier. Returns the identifier itself when it names nobody, so the caller
    /// always has something to show and never silently blanks an actor.
    /// </summary>
    public async Task<string> ResolveAsync(
        string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return "—";
        }

        var resolved = await ResolveAsync(new[] { userId }, cancellationToken).ConfigureAwait(false);
        return resolved.TryGetValue(userId, out var name) ? name : userId;
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IEnumerable<string?> userIds, CancellationToken cancellationToken = default)
    {
        var wanted = userIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var missing = wanted.Where(id => !_cache.ContainsKey(id)).ToList();

        if (missing.Count > 0)
        {
            var guids = missing
                .Select(id => Guid.TryParse(id, out var guid) ? guid : (Guid?)null)
                .Where(guid => guid is not null)
                .Select(guid => guid!.Value)
                .ToList();

            if (guids.Count > 0)
            {
                var users = await _context.Users.AsNoTracking()
                    .Where(u => guids.Contains(u.Id))
                    .Select(u => new { u.Id, u.FullName, u.Username })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var user in users)
                {
                    _cache[user.Id.ToString()] = string.IsNullOrWhiteSpace(user.FullName)
                        ? user.Username
                        : $"{user.FullName} ({user.Username})";
                }
            }

            // Anything still unresolved is cached as itself, so a missing user is not re-queried
            // on every row of a long page.
            foreach (var id in missing.Where(id => !_cache.ContainsKey(id)))
            {
                _cache[id] = id;
            }
        }

        return wanted.ToDictionary(id => id, id => _cache[id], StringComparer.OrdinalIgnoreCase);
    }
}
