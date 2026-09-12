namespace Tawaka.Domain.Common;

/// <summary>
/// An ISO-style three-letter currency code. Zimbabwe Gold is held as its ISO code <c>ZWG</c>;
/// the local "ZiG" label is a display concern carried by <see cref="Currencies.Currency"/>.
/// </summary>
public readonly struct CurrencyCode : IEquatable<CurrencyCode>
{
    /// <summary>United States Dollar.</summary>
    public static readonly CurrencyCode Usd = new("USD");

    /// <summary>Zimbabwe Gold (displayed to users as "ZiG").</summary>
    public static readonly CurrencyCode Zwg = new("ZWG");

    private readonly string? _value;

    public CurrencyCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Currency code must be supplied.", nameof(value));
        }

        var normalised = value.Trim().ToUpperInvariant();
        if (normalised.Length != 3 || !normalised.All(char.IsLetter))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid three-letter currency code.", nameof(value));
        }

        _value = normalised;
    }

    /// <summary>True when this code was default-constructed and carries no value.</summary>
    public bool IsEmpty => _value is null;

    public string Value => _value
        ?? throw new InvalidOperationException("Currency code has not been initialised.");

    public static CurrencyCode Parse(string value) => new(value);

    public static bool TryParse(string? value, out CurrencyCode code)
    {
        try
        {
            code = new CurrencyCode(value!);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException)
        {
            code = default;
            return false;
        }
    }

    public bool Equals(CurrencyCode other) =>
        string.Equals(_value, other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is CurrencyCode other && Equals(other);

    public override int GetHashCode() => _value?.GetHashCode(StringComparison.Ordinal) ?? 0;

    public override string ToString() => _value ?? "(none)";

    public static bool operator ==(CurrencyCode left, CurrencyCode right) => left.Equals(right);

    public static bool operator !=(CurrencyCode left, CurrencyCode right) => !left.Equals(right);
}
