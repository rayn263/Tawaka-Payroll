using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tawaka.Infrastructure.Persistence;

/// <summary>
/// Persists a <see cref="DateTimeOffset"/> as text that sorts chronologically.
/// <para>
/// EF Core's own SQLite mapping stores a <see cref="DateTimeOffset"/> as its local date and time
/// followed by the offset, which does not sort chronologically once two rows carry different
/// offsets. Rather than sort it wrongly, the provider refuses: any <c>ORDER BY</c>, <c>Min</c> or
/// <c>Max</c> over such a column throws <see cref="NotSupportedException"/> at query time — which
/// is to say on the user's screen, not at compile time.
/// </para>
/// <para>
/// So the instant is written first, in UTC, in a fixed-width form, and the original offset is
/// appended: <c>2026-09-17T12:32:00.0000000Z+02:00</c>. Ordering the text is then exactly ordering
/// the instant, the offset still round-trips, and no precision is lost. See ADR-040.
/// </para>
/// </summary>
public sealed class SortableDateTimeOffsetConverter : ValueConverter<DateTimeOffset, string>
{
    /// <summary>Length of a value written in this form; used to recognise it on read.</summary>
    public const int Width = 34;

    private const string InstantFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public SortableDateTimeOffsetConverter()
        : base(value => Write(value), stored => Read(stored))
    {
    }

    public static string Write(DateTimeOffset value) =>
        value.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture)
        + (value.Offset < TimeSpan.Zero ? '-' : '+')
        + value.Offset.Duration().ToString("hh\\:mm", CultureInfo.InvariantCulture);

    public static DateTimeOffset Read(string stored)
    {
        // A database written before ADR-040 holds EF's own format. It is still read correctly;
        // only the ordering of those rows would have been wrong, and no such database was ever
        // put into service.
        if (stored.Length != Width || stored[10] != 'T' || stored[27] != 'Z')
        {
            return DateTimeOffset.Parse(
                stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        var instant = DateTime.ParseExact(
            stored[..28], InstantFormat, CultureInfo.InvariantCulture, DateTimeStyles.None);

        var hours = int.Parse(stored.Substring(29, 2), CultureInfo.InvariantCulture);
        var minutes = int.Parse(stored.Substring(32, 2), CultureInfo.InvariantCulture);
        var offset = new TimeSpan(hours, minutes, 0);

        if (stored[28] == '-')
        {
            offset = offset.Negate();
        }

        return new DateTimeOffset(instant, TimeSpan.Zero).ToOffset(offset);
    }
}
