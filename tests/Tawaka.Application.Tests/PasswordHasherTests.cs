using Tawaka.Application.Security;
using Xunit;

namespace Tawaka.Application.Tests;

public class PasswordHasherTests
{
    // A low iteration count keeps the suite fast; production uses the default 210,000.
    private static PasswordHasher Fast() => new(iterations: 15_000);

    [Fact]
    public void Hash_never_contains_the_password()
    {
        var hash = Fast().Hash("Nyanga2026!");

        Assert.DoesNotContain("Nyanga2026!", hash, StringComparison.Ordinal);
        Assert.StartsWith("PBKDF2-SHA256$", hash, StringComparison.Ordinal);
    }

    [Fact]
    public void Correct_password_verifies()
    {
        var hasher = Fast();
        var hash = hasher.Hash("Nyanga2026!");

        Assert.True(hasher.Verify("Nyanga2026!", hash));
    }

    [Theory]
    [InlineData("nyanga2026!")]
    [InlineData("Nyanga2026")]
    [InlineData("")]
    [InlineData("   ")]
    public void Wrong_password_does_not_verify(string attempt)
    {
        var hasher = Fast();
        var hash = hasher.Hash("Nyanga2026!");

        Assert.False(hasher.Verify(attempt, hash));
    }

    [Fact]
    public void The_same_password_hashes_differently_each_time()
    {
        var hasher = Fast();

        var first = hasher.Hash("Nyanga2026!");
        var second = hasher.Hash("Nyanga2026!");

        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify("Nyanga2026!", first));
        Assert.True(hasher.Verify("Nyanga2026!", second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("PBKDF2-SHA256$abc$def")]
    [InlineData("MD5$1000$c2FsdA==$aGFzaA==")]
    public void Malformed_stored_hashes_fail_closed(string stored)
    {
        Assert.False(Fast().Verify("anything", stored));
    }

    [Fact]
    public void A_hash_made_with_fewer_iterations_is_flagged_for_upgrade()
    {
        var weak = new PasswordHasher(iterations: 12_000).Hash("Nyanga2026!");
        var strong = new PasswordHasher(iterations: 50_000);

        Assert.True(strong.NeedsRehash(weak));
        Assert.False(strong.NeedsRehash(strong.Hash("Nyanga2026!")));
    }

    [Fact]
    public void An_unsafe_iteration_count_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PasswordHasher(iterations: 100));
    }

    [Fact]
    public void Hashing_an_empty_password_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Fast().Hash(string.Empty));
    }
}

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Short1")]
    [InlineData("alllowercase1")]
    [InlineData("ALLUPPERCASE1")]
    [InlineData("NoDigitsAtAll")]
    public void Weak_passwords_are_rejected(string password)
    {
        Assert.False(new PasswordPolicy().Validate(password).IsValid);
    }

    [Fact]
    public void A_compliant_password_is_accepted()
    {
        Assert.True(new PasswordPolicy().Validate("Nyanga2026Site").IsValid);
    }

    [Fact]
    public void A_missing_password_reports_one_clear_error()
    {
        var result = new PasswordPolicy().Validate(null);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }
}
