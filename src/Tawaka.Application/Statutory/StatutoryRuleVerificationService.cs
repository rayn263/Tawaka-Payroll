using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Security;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory;

namespace Tawaka.Application.Statutory;

/// <summary>What a person read, and where, before saying a rule is right.</summary>
public sealed record RuleVerification
{
    /// <summary>E.g. "ZIMRA PAYE tables 2026", "SI 393 of 1993 s.12".</summary>
    public required string Source { get; init; }

    /// <summary>Where in it — a page, a table, a section.</summary>
    public string? Reference { get; init; }

    /// <summary>The date of the document, not the date it was read.</summary>
    public DateOnly? SourceDate { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// Marks a statutory rule verified, or takes that back.
/// <para>
/// This is the only way an installation leaves COMPLIANCE-UNVERIFIED, and the gate is the point of
/// the product, so the act is deliberate: one rule at a time, with the document it was checked
/// against recorded on the rule, and audited. Verifying without naming a source is refused — a
/// rule marked Verified by somebody who cannot say what they read is worth less than one left
/// Unverified, because it looks settled.
/// </para>
/// </summary>
public sealed class StatutoryRuleVerificationService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public StatutoryRuleVerificationService(
        IPayrollDataContext context, ICurrentUser currentUser, IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<ValidationResult> VerifyAsync(
        Guid ruleId, RuleVerification evidence, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.StatutoryVerifyRules);

        var validation = ValidationResult.Success();
        validation.Require(evidence.Source, "Source",
            "Name the document this rule was checked against. A rule cannot be verified against " +
            "nothing.");

        var rule = await _context.StatutoryRules
            .Include(r => r.Source)
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken)
            .ConfigureAwait(false);

        if (rule is null)
        {
            return validation.Add("Rule", "Statutory rule not found.");
        }

        validation.AddIf(rule.VerificationStatus == VerificationStatus.Disabled, "Rule",
            "This rule is disabled. Enable it before verifying it.");

        if (!validation.IsValid)
        {
            return validation;
        }

        rule.Source ??= new RuleSource();
        rule.Source.Source = evidence.Source.Trim();
        rule.Source.SourceReference = Trimmed(evidence.Reference);
        rule.Source.SourceDate = evidence.SourceDate;
        rule.Source.VerifiedBy = _currentUser.UserName;
        rule.Source.VerifiedAt = _clock.Now;
        rule.VerificationStatus = VerificationStatus.Verified;
        rule.Notes = Trimmed(evidence.Notes) ?? rule.Notes;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// Takes a verification back — because the document turned out to say something else, or
    /// because a new one superseded it. The evidence that was recorded stays on the rule: an
    /// auditor asking what was believed, and when, is entitled to an answer.
    /// </summary>
    public async Task<ValidationResult> WithdrawAsync(
        Guid ruleId, string reason, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.StatutoryVerifyRules);

        var validation = ValidationResult.Success();
        validation.Require(reason, "Reason",
            "Say why the verification no longer stands. Payrolls were run on this rule.");

        var rule = await _context.StatutoryRules
            .Include(r => r.Source)
            .FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken)
            .ConfigureAwait(false);

        if (rule is null)
        {
            return validation.Add("Rule", "Statutory rule not found.");
        }

        if (!validation.IsValid)
        {
            return validation;
        }

        rule.VerificationStatus = VerificationStatus.Unverified;
        rule.Notes = $"Verification withdrawn {_clock.Now:yyyy-MM-dd} by {_currentUser.UserName}: " +
                     $"{reason.Trim()}";

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
