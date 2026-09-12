using Tawaka.Domain.Common;

namespace Tawaka.Domain.Companies;

/// <summary>
/// What a company payment account is used for. A company commonly pays net wages from one account
/// and remits statutory obligations from another, and USD and ZiG are separate accounts entirely.
/// </summary>
public enum PaymentPurpose
{
    General = 0,
    NetPay = 1,
    Paye = 2,
    Nssa = 3,
    Zimdef = 4,
    StandardsDevelopmentFund = 5,
    Nec = 6,
    Other = 7
}

/// <summary>
/// A company bank account. There is no assumption that a company has only one, nor that an
/// account is named after the company: <see cref="AccountName"/> is captured independently of the
/// company's legal and trading names, because banks routinely hold accounts under a different
/// style, and paying to the wrong name is how transfers get rejected.
/// </summary>
public class CompanyBankAccount : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public Company? Company { get; set; }

    /// <summary>The name the account is actually held in, as the bank has it.</summary>
    public string AccountName { get; set; } = string.Empty;

    public string BankName { get; set; } = string.Empty;

    public string? BranchName { get; set; }

    public string? BranchCode { get; set; }

    public string AccountNumber { get; set; } = string.Empty;

    public string? SwiftCode { get; set; }

    /// <summary>ISO code. An account holds exactly one currency.</summary>
    public string CurrencyCode { get; set; } = string.Empty;

    public PaymentPurpose Purpose { get; set; } = PaymentPurpose.General;

    /// <summary>The default account for this purpose and currency.</summary>
    public bool IsDefaultForPurpose { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }
}
