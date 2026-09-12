using Tawaka.Domain.Statutory;

namespace Tawaka.Application.Abstractions;

/// <summary>Time, injected so calculations and tests are deterministic.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }

    DateOnly Today { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}

/// <summary>Who is acting. The audit interceptor attributes every change through this.</summary>
public interface ICurrentUser
{
    string UserId { get; }

    string UserName { get; }

    string? Machine { get; }

    bool HasPermission(string permissionCode);
}

/// <summary>
/// Placeholder identity used before authentication exists (Milestone 2). It is deliberately
/// named so that an unattributed audit entry is obvious rather than looking like a real user.
/// </summary>
public sealed class SystemUser : ICurrentUser
{
    public string UserId => "system";

    public string UserName => "SYSTEM (unauthenticated)";

    public string? Machine => Environment.MachineName;

    public bool HasPermission(string permissionCode) => true;
}

/// <summary>
/// Supplies candidate statutory rules to the resolver. Implemented over EF Core in
/// infrastructure, and over plain lists in tests, so resolution logic is testable without a
/// database.
/// </summary>
public interface IStatutoryRuleSource
{
    IReadOnlyList<StatutoryRule> GetRules(StatutoryRuleType ruleType);
}
