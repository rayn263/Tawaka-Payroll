namespace Tawaka.Domain.Common;

/// <summary>
/// A half-open effective-dated range: inclusive of <see cref="From"/>, inclusive of
/// <see cref="To"/> where set, open-ended where <see cref="To"/> is null.
/// </summary>
public readonly struct DateRange : IEquatable<DateRange>
{
    public DateRange(DateOnly from, DateOnly? to)
    {
        if (to.HasValue && to.Value < from)
        {
            throw new ArgumentException(
                $"Effective-to ({to}) cannot precede effective-from ({from}).", nameof(to));
        }

        From = from;
        To = to;
    }

    public DateOnly From { get; }

    public DateOnly? To { get; }

    public bool IsOpenEnded => To is null;

    public bool Contains(DateOnly date) => date >= From && (To is null || date <= To.Value);

    public bool Overlaps(DateRange other) =>
        (To is null || other.From <= To.Value) && (other.To is null || From <= other.To.Value);

    public bool Equals(DateRange other) => From == other.From && To == other.To;

    public override bool Equals(object? obj) => obj is DateRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(From, To);

    public override string ToString() => To is null ? $"{From} onwards" : $"{From} to {To}";

    public static bool operator ==(DateRange left, DateRange right) => left.Equals(right);

    public static bool operator !=(DateRange left, DateRange right) => !left.Equals(right);
}
