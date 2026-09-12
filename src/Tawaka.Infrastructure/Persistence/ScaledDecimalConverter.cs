using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tawaka.Infrastructure.Persistence;

/// <summary>
/// Persists a decimal as a scaled integer (minor units).
/// <para>
/// SQLite has no native decimal type and stores <c>decimal</c> as TEXT, which silently breaks
/// ordering, comparison and SUM. For a payroll system that is unacceptable, so every monetary and
/// rate value is stored as an exact integer and scaled back on read (ADR-002).
/// </para>
/// </summary>
public sealed class ScaledDecimalConverter : ValueConverter<decimal, long>
{
    public ScaledDecimalConverter(int scale)
        : base(
            value => (long)decimal.Round(value * Pow10(scale), 0, MidpointRounding.AwayFromZero),
            stored => stored / Pow10(scale))
    {
        Scale = scale;
    }

    public int Scale { get; }

    private static decimal Pow10(int scale)
    {
        decimal result = 1m;
        for (var i = 0; i < scale; i++)
        {
            result *= 10m;
        }

        return result;
    }
}

/// <summary>Nullable counterpart of <see cref="ScaledDecimalConverter"/>.</summary>
public sealed class NullableScaledDecimalConverter : ValueConverter<decimal?, long?>
{
    public NullableScaledDecimalConverter(int scale)
        : base(
            value => value == null
                ? null
                : (long?)decimal.Round(value.Value * Pow10(scale), 0, MidpointRounding.AwayFromZero),
            stored => stored == null ? null : stored.Value / Pow10(scale))
    {
        Scale = scale;
    }

    public int Scale { get; }

    private static decimal Pow10(int scale)
    {
        decimal result = 1m;
        for (var i = 0; i < scale; i++)
        {
            result *= 10m;
        }

        return result;
    }
}

/// <summary>Scales used across the schema.</summary>
public static class MoneyScales
{
    /// <summary>Monetary amounts: four decimal places.</summary>
    public const int Money = 4;

    /// <summary>Rates and exchange rates: eight decimal places.</summary>
    public const int Rate = 8;
}
