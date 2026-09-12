using System.Security.Cryptography;

namespace Tawaka.Application.Security;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string encodedHash);

    /// <summary>True when the stored hash used weaker parameters and should be upgraded on login.</summary>
    bool NeedsRehash(string encodedHash);
}

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing.
/// <para>
/// Passwords are never stored, and never encrypted reversibly — only a salted, iterated hash is
/// kept. Verification is constant-time so that a wrong password cannot be narrowed down by timing.
/// </para>
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "PBKDF2-SHA256";
    private const int SaltBytes = 16;
    private const int SubkeyBytes = 32;

    /// <summary>OWASP guidance for PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 210_000;

    private readonly int _iterations;

    public PasswordHasher(int iterations = DefaultIterations)
    {
        if (iterations < 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(iterations), "Iteration count is too low to be safe.");
        }

        _iterations = iterations;
    }

    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password must be supplied.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var subkey = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, _iterations, HashAlgorithmName.SHA256, SubkeyBytes);

        return string.Join('$',
            Algorithm,
            _iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(subkey));
    }

    public bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        if (!TryParse(encodedHash, out var iterations, out var salt, out var expected))
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string encodedHash) =>
        !TryParse(encodedHash, out var iterations, out _, out _) || iterations < _iterations;

    private static bool TryParse(
        string encodedHash, out int iterations, out byte[] salt, out byte[] subkey)
    {
        iterations = 0;
        salt = Array.Empty<byte>();
        subkey = Array.Empty<byte>();

        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Algorithm)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            subkey = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && subkey.Length > 0;
    }
}
