using Tawaka.Domain.Common;

namespace Tawaka.Domain.Companies;

/// <summary>
/// Which currencies a company operates payroll in, and how they are presented. The currency
/// itself is global reference data; enablement and defaults are per company.
/// </summary>
public class CompanyCurrency : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public Company? Company { get; set; }

    /// <summary>ISO code, e.g. USD or ZWG.</summary>
    public string CurrencyCode { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public bool IsDefaultPayrollCurrency { get; set; }

    public bool IsDefaultReportingCurrency { get; set; }

    /// <summary>Overrides the global decimal places where a company needs something different.</summary>
    public int? DecimalPlacesOverride { get; set; }

    /// <summary>Overrides the display label, e.g. "ZiG" rather than "ZWG".</summary>
    public string? DisplayCodeOverride { get; set; }

    public int SortOrder { get; set; }
}
