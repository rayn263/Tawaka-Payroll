using Tawaka.Domain.Common;

namespace Tawaka.Domain.Currencies;

/// <summary>
/// A dated exchange rate. Rates are never edited in place once used by a calculated payroll run:
/// the run holds its own frozen copy (ADR-004), so changing today's rate cannot alter history.
/// </summary>
public class ExchangeRate : AuditableEntity
{
    public string FromCurrency { get; set; } = string.Empty;
    public string ToCurrency { get; set; } = string.Empty;

    /// <summary>Units of <see cref="ToCurrency"/> per one unit of <see cref="FromCurrency"/>.</summary>
    public decimal Rate { get; set; }

    public RateType RateType { get; set; } = RateType.Interbank;

    /// <summary>Where the rate came from, e.g. "RBZ interbank". Required.</summary>
    public string Source { get; set; } = string.Empty;

    public string? SourceReference { get; set; }

    public DateOnly RateDate { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    public DateRange EffectivePeriod => new(EffectiveFrom, EffectiveTo);

    public bool AppliesOn(DateOnly date) => IsActive && EffectivePeriod.Contains(date);

    /// <summary>
    /// Converts an amount, returning the full provenance record rather than a bare number.
    /// </summary>
    public CurrencyConversion Convert(Money original, ConversionPurpose purpose)
    {
        var from = new CurrencyCode(FromCurrency);
        var to = new CurrencyCode(ToCurrency);

        if (original.Currency != from)
        {
            throw new CurrencyMismatchException(original.Currency, from);
        }

        return new CurrencyConversion(
            OriginalAmount: original,
            ConvertedAmount: new Money(original.Amount * Rate, to),
            Rate: Rate,
            RateDate: RateDate,
            RateSource: Source,
            RateType: RateType,
            Purpose: purpose,
            ExchangeRateId: Id);
    }
}
