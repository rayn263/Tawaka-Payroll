using Tawaka.Domain.Currencies;

namespace Tawaka.Payroll.Engine.Currencies;

/// <summary>
/// The conversion provenance attached to a traced figure. Mirrors
/// <see cref="CurrencyConversion"/> in a shape convenient for traces and snapshots.
/// </summary>
public sealed record ConversionRecord(
    decimal OriginalAmount,
    string OriginalCurrency,
    decimal Rate,
    DateOnly RateDate,
    string RateSource,
    RateType RateType,
    decimal ConvertedAmount,
    string ConvertedCurrency,
    ConversionPurpose Purpose)
{
    public static ConversionRecord From(CurrencyConversion conversion) => new(
        conversion.OriginalAmount.Amount,
        conversion.OriginalCurrency.Value,
        conversion.Rate,
        conversion.RateDate,
        conversion.RateSource,
        conversion.RateType,
        conversion.ConvertedAmount.Amount,
        conversion.ConvertedCurrency.Value,
        conversion.Purpose);
}
