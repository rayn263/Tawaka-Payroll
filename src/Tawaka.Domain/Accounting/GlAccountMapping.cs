using Tawaka.Domain.Common;

namespace Tawaka.Domain.Accounting;

/// <summary>
/// The payroll amounts that need a general ledger account.
/// <para>
/// Deliberately short. This is a payroll system producing a journal an accountant can post, not an
/// accounting system: the split is the one payroll actually knows about, and no more.
/// </para>
/// </summary>
public enum GlMappingType
{
    /// <summary>Gross wages — the expense.</summary>
    WagesExpense = 0,

    /// <summary>Net pay owed to employees — a liability until they are paid.</summary>
    NetPayLiability = 1,

    PayeLiability = 2,
    AidsLevyLiability = 3,
    NssaEmployeeLiability = 4,

    /// <summary>The employer's NSSA contribution: an expense and a liability.</summary>
    NssaEmployerExpense = 5,
    NssaEmployerLiability = 6,

    /// <summary>APWCS, ZIMDEF, SDF and any other employer-borne statutory cost.</summary>
    EmployerStatutoryExpense = 7,
    EmployerStatutoryLiability = 8,

    /// <summary>Non-statutory deductions withheld from employees — union dues, medical aid.</summary>
    OtherDeductionLiability = 9,

    /// <summary>Loan and advance recoveries, which reduce a receivable rather than create a liability.</summary>
    LoanRecoveryReceivable = 10
}

/// <summary>
/// One general ledger account, configured per company and per currency.
/// <para>
/// Per currency, because a USD wages account and a ZiG wages account are different accounts in
/// every chart of accounts this system will meet. Mapping them to one account is how a journal
/// ends up mixing currencies, which this system does not permit anywhere else and will not permit
/// here (ADR-036).
/// </para>
/// </summary>
public class GlAccountMapping : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public GlMappingType MappingType { get; set; }

    /// <summary>ISO currency code. A mapping is always specific to one currency.</summary>
    public string CurrencyCode { get; set; } = "USD";

    public string AccountCode { get; set; } = string.Empty;

    public string AccountName { get; set; } = string.Empty;

    /// <summary>Optional cost centre or department dimension, where the ledger uses one.</summary>
    public string? CostCentre { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    public CurrencyCode Currency => new(CurrencyCode);
}
