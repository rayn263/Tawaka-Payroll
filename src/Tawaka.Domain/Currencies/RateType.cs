namespace Tawaka.Domain.Currencies;

/// <summary>
/// Which published rate a stored exchange rate represents. Which of these is legally required for
/// PAYE conversion is unresolved (compliance spec Q1/§3), so the rate type is recorded rather
/// than assumed.
/// </summary>
public enum RateType
{
    Interbank = 0,
    Official = 1,
    Auction = 2,
    Custom = 3
}

/// <summary>How the applicable rate for a payroll run is determined.</summary>
public enum RateDeterminationRule
{
    /// <summary>Not yet determined. Blocks live payroll rather than defaulting.</summary>
    NotDetermined = 0,
    PayDate = 1,
    PeriodEndDate = 2,
    DateOfPayment = 3,
    PinnedRate = 4
}
