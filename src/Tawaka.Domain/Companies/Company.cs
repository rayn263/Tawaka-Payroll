using Tawaka.Domain.Common;

namespace Tawaka.Domain.Companies;

/// <summary>
/// The employing entity. Every company-scoped table carries <c>CompanyId</c> so that multi-company
/// support is a user-interface question later rather than a schema rewrite (ADR-010). The first
/// release operates with exactly one active company.
/// </summary>
public class Company : AuditableEntity
{
    public string LegalName { get; set; } = string.Empty;

    /// <summary>Trading name where it differs from the registered legal name.</summary>
    public string? TradingName { get; set; }

    public string? RegistrationNumber { get; set; }

    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; } = "Zimbabwe";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? LogoPath { get; set; }

    /// <summary>ZIMRA business partner / tax identification number.</summary>
    public string? TaxNumber { get; set; }

    /// <summary>ZIMRA PAYE registration reference where it differs from the tax number.</summary>
    public string? PayeReference { get; set; }

    public string? NssaEmployerNumber { get; set; }

    public string? ZimdefNumber { get; set; }

    public string? StandardsDevelopmentFundNumber { get; set; }

    public string? NecCode { get; set; }

    public string? NecMembershipNumber { get; set; }

    /// <summary>NSSA industry classification, which drives the APWCS assessed rate.</summary>
    public string? IndustryClassification { get; set; }

    public string? ApwcsIndustryCode { get; set; }

    /// <summary>ISO code of the currency new employees default to.</summary>
    public string DefaultPayrollCurrency { get; set; } = "USD";

    /// <summary>ISO code used for reports. Never used to merge totals across currencies.</summary>
    public string DefaultReportingCurrency { get; set; } = "USD";

    public bool AllowMixedCurrencyPayroll { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<CompanyCurrency> Currencies { get; set; } = new List<CompanyCurrency>();

    public ICollection<CompanyBankAccount> BankAccounts { get; set; } = new List<CompanyBankAccount>();

    /// <summary>The name to show on documents: trading name where set, otherwise legal name.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(TradingName) ? LegalName : TradingName!;
}
