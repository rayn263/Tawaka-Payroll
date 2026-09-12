using Tawaka.Domain.Companies;

namespace Tawaka.Infrastructure.Seeding;

/// <summary>What first-run seeding produced, including any one-time administrator password.</summary>
public sealed record SeedOutcome(Company Company, SecuritySeedResult Security);

/// <summary>
/// Runs first-run seeding in the order the data depends on: statutory rules, then the company and
/// its configuration, then security. Every step is idempotent, so running it again is harmless.
/// </summary>
public sealed class ApplicationSeeder
{
    private readonly StatutoryRuleSeeder _statutory;
    private readonly CompanySeeder _company;
    private readonly SecuritySeeder _security;

    public ApplicationSeeder(
        StatutoryRuleSeeder statutory, CompanySeeder company, SecuritySeeder security)
    {
        _statutory = statutory;
        _company = company;
        _security = security;
    }

    public async Task<SeedOutcome> SeedAsync(CancellationToken cancellationToken = default)
    {
        await _statutory.SeedAsync(cancellationToken).ConfigureAwait(false);
        var company = await _company.SeedAsync(cancellationToken).ConfigureAwait(false);
        var security = await _security.SeedAsync(company.Id, cancellationToken).ConfigureAwait(false);
        return new SeedOutcome(company, security);
    }
}
