using Tawaka.Application.Common;

namespace Tawaka.Application.Security;

/// <summary>Minimum password requirements, configurable rather than scattered through the code.</summary>
public sealed class PasswordPolicy
{
    public int MinimumLength { get; init; } = 10;

    public bool RequireUppercase { get; init; } = true;

    public bool RequireLowercase { get; init; } = true;

    public bool RequireDigit { get; init; } = true;

    public int MaximumFailedAttempts { get; init; } = 5;

    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(15);

    public ValidationResult Validate(string? password, string field = "Password")
    {
        var result = ValidationResult.Success();

        if (string.IsNullOrWhiteSpace(password))
        {
            return result.Add(field, "Password is required.");
        }

        result.AddIf(password.Length < MinimumLength, field,
            $"Password must be at least {MinimumLength} characters.");
        result.AddIf(RequireUppercase && !password.Any(char.IsUpper), field,
            "Password must contain an uppercase letter.");
        result.AddIf(RequireLowercase && !password.Any(char.IsLower), field,
            "Password must contain a lowercase letter.");
        result.AddIf(RequireDigit && !password.Any(char.IsDigit), field,
            "Password must contain a digit.");

        return result;
    }
}
