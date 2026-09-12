using Tawaka.Domain.Common;

namespace Tawaka.Domain.Currencies;

/// <summary>Why a conversion was performed. Recorded so conversions can be audited by intent.</summary>
public enum ConversionPurpose
{
    TaxBaseAggregation = 0,
    StatutoryCeiling = 1,
    TaxCredit = 2,
    BenefitValuation = 3,
    Reporting = 4,
    ConsolidatedManagementReport = 5
}

/// <summary>
/// The complete record of a currency conversion. The original amount and currency are carried
/// alongside the converted value and are never overwritten — this is the design principle the
/// whole dual-currency design rests on.
/// </summary>
public sealed record CurrencyConversion(
    Money OriginalAmount,
    Money ConvertedAmount,
    decimal Rate,
    DateOnly RateDate,
    string RateSource,
    RateType RateType,
    ConversionPurpose Purpose,
    Guid? ExchangeRateId = null)
{
    public CurrencyCode OriginalCurrency => OriginalAmount.Currency;

    public CurrencyCode ConvertedCurrency => ConvertedAmount.Currency;

    public override string ToString() =>
        $"{OriginalAmount} -> {ConvertedAmount} @ {Rate} ({RateType}, {RateSource}, {RateDate}) for {Purpose}";
}
