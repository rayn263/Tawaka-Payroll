using Tawaka.Domain.Common;

namespace Tawaka.Domain.Currencies;

/// <summary>
/// A currency supported for payroll. The ISO code is the identity; <see cref="DisplayCode"/>
/// carries the label users expect to see ("ZiG" for ZWG).
/// </summary>
public class Currency : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayCode { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; } = 2;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public CurrencyCode AsCurrencyCode() => new(Code);

    /// <summary>Formats as "ZiG 18,500.00" using the user-facing label.</summary>
    public string Format(decimal amount) =>
        $"{DisplayCode} {amount.ToString("N" + DecimalPlaces, System.Globalization.CultureInfo.InvariantCulture)}";
}
