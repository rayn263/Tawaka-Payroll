using System.Globalization;

namespace Tawaka.Domain.Common;

/// <summary>
/// A monetary amount and the currency it is denominated in. Arithmetic between different
/// currencies throws <see cref="CurrencyMismatchException"/>: there is no implicit conversion
/// anywhere in this system (ADR-002).
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    /// <summary>Scale used when money is persisted as a scaled integer (minor units).</summary>
    public const int StorageScale = 4;

    public Money(decimal amount, CurrencyCode currency)
    {
        if (currency.IsEmpty)
        {
            throw new ArgumentException("Money must carry a currency.", nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public CurrencyCode Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public static Money Zero(CurrencyCode currency) => new(0m, currency);

    public static Money Usd(decimal amount) => new(amount, CurrencyCode.Usd);

    public static Money Zwg(decimal amount) => new(amount, CurrencyCode.Zwg);

    /// <summary>
    /// Rounds to the given number of decimal places. Payroll rounds away from zero by default so
    /// that half-cent results do not drift systematically in the employer's favour.
    /// </summary>
    public Money Round(int decimals = 2, MidpointRounding mode = MidpointRounding.AwayFromZero) =>
        new(decimal.Round(Amount, decimals, mode), Currency);

    /// <summary>Returns the smaller of two amounts in the same currency (used for ceilings).</summary>
    public static Money Min(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount <= right.Amount ? left : right;
    }

    /// <summary>Returns the larger of two amounts in the same currency (used for floors).</summary>
    public static Money Max(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount >= right.Amount ? left : right;
    }

    /// <summary>Sums amounts that must all share <paramref name="currency"/>.</summary>
    public static Money Sum(CurrencyCode currency, IEnumerable<Money> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        var total = Zero(currency);
        foreach (var amount in amounts)
        {
            total += amount;
        }

        return total;
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new CurrencyMismatchException(left.Currency, right.Currency);
        }
    }

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator -(Money value) => new(-value.Amount, value.Currency);

    public static Money operator *(Money left, decimal factor) =>
        new(left.Amount * factor, left.Currency);

    public static Money operator *(decimal factor, Money right) => right * factor;

    public static Money operator /(Money left, decimal divisor) =>
        new(left.Amount / divisor, left.Currency);

    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    public static bool operator >=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount >= right.Amount;
    }

    public static bool operator <=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount <= right.Amount;
    }

    public static bool operator ==(Money left, Money right) => left.Equals(right);

    public static bool operator !=(Money left, Money right) => !left.Equals(right);

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public bool Equals(Money other) => Amount == other.Amount && Currency == other.Currency;

    public override bool Equals(object? obj) => obj is Money other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Amount, Currency);

    /// <summary>Renders as "USD 1,075.00". Display labels such as "ZiG" are applied by the UI.</summary>
    public override string ToString() =>
        $"{Currency} {Amount.ToString("N2", CultureInfo.InvariantCulture)}";
}
