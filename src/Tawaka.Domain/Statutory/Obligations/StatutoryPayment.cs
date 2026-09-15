using Tawaka.Domain.Common;

namespace Tawaka.Domain.Statutory.Obligations;

public enum StatutoryPaymentMethod
{
    BankTransfer = 0,
    Rtgs = 1,
    Cash = 2,
    Cheque = 3,
    MobileMoney = 4,
    Other = 5
}

/// <summary>
/// Evidence that an authority was actually paid.
/// <para>
/// The existence of a row here is the <b>only</b> thing that can make an obligation paid. It
/// carries the date, method, reference and amount, so "did we pay ZIMRA for September?" is
/// answerable with a document reference rather than a recollection.
/// </para>
/// <para>
/// Payments are never deleted. A payment entered in error is reversed, with a reason, and both the
/// payment and its reversal remain visible.
/// </para>
/// </summary>
public class StatutoryPayment : AuditableEntity
{
    public Guid StatutoryObligationId { get; set; }

    public StatutoryObligation? Obligation { get; set; }

    public decimal Amount { get; set; }

    /// <summary>Must match the obligation's currency. A USD liability is not settled in ZiG.</summary>
    public string CurrencyCode { get; set; } = "USD";

    public DateOnly PaymentDate { get; set; }

    public StatutoryPaymentMethod PaymentMethod { get; set; } = StatutoryPaymentMethod.BankTransfer;

    /// <summary>The employer's own reference: transfer number, cheque number, RTGS reference.</summary>
    public string PaymentReference { get; set; } = string.Empty;

    /// <summary>The authority's receipt or acknowledgement number, once received.</summary>
    public string? AuthorityReceiptNumber { get; set; }

    public Guid? CompanyBankAccountId { get; set; }

    public string? ReceiptFilePath { get; set; }

    public string? ReceiptFileHash { get; set; }

    /// <summary>
    /// Set where the payment includes penalty or interest, so the extra is not mistaken for an
    /// overpayment of the obligation itself.
    /// </summary>
    public decimal PenaltyOrInterestIncluded { get; set; }

    public string? Notes { get; set; }

    public bool IsReversed { get; set; }

    public string? ReversalReason { get; set; }

    public string? ReversedBy { get; set; }

    public DateTimeOffset? ReversedAt { get; set; }

    public Money AsMoney() => new(Amount, new CurrencyCode(CurrencyCode));

    /// <summary>The amount actually settling the obligation, excluding penalty and interest.</summary>
    public decimal PrincipalAmount => Amount - PenaltyOrInterestIncluded;
}
