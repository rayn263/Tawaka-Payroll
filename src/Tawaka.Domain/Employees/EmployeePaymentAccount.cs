using Tawaka.Domain.Common;

namespace Tawaka.Domain.Employees;

public enum PaymentMethod
{
    BankTransfer = 0,
    MobileMoney = 1,
    Cash = 2,
    Cheque = 3,
    Other = 4
}

/// <summary>How an employee's net pay is split across their accounts.</summary>
public enum AllocationType
{
    /// <summary>The whole net amount for this currency.</summary>
    FullBalance = 0,
    Percentage = 1,
    FixedAmount = 2
}

/// <summary>
/// Where an employee is paid. An employee may hold several accounts, and an account's currency is
/// independent of the currency their payroll is denominated in — someone paid in USD may hold a
/// ZiG account for another purpose, so the account currency is recorded per account rather than
/// inherited from the contract.
/// </summary>
public class EmployeePaymentAccount : AuditableEntity
{
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.BankTransfer;

    /// <summary>ISO code of the currency this account is denominated in.</summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>The name the account is held in, which may not match the employee's own name.</summary>
    public string? AccountName { get; set; }

    public string? BankName { get; set; }
    public string? BranchName { get; set; }
    public string? BranchCode { get; set; }
    public string? AccountNumber { get; set; }

    public string? MobileMoneyProvider { get; set; }
    public string? MobileMoneyNumber { get; set; }

    public AllocationType AllocationType { get; set; } = AllocationType.FullBalance;

    /// <summary>Percentage or fixed amount, depending on <see cref="AllocationType"/>.</summary>
    public decimal? AllocationValue { get; set; }

    /// <summary>One primary account per currency.</summary>
    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; } = true;

    public string? Notes { get; set; }

    public string Describe() => PaymentMethod switch
    {
        PaymentMethod.BankTransfer => $"{BankName} {MaskedAccountNumber}".Trim(),
        PaymentMethod.MobileMoney => $"{MobileMoneyProvider} {MobileMoneyNumber}".Trim(),
        _ => PaymentMethod.ToString()
    };

    public string MaskedAccountNumber => string.IsNullOrWhiteSpace(AccountNumber)
        ? string.Empty
        : AccountNumber.Length <= 4
            ? AccountNumber
            : $"****{AccountNumber[^4..]}";
}

/// <summary>An employee's assignment to a project or site, for labour costing.</summary>
public class EmployeeProjectAssignment : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public Guid ProjectId { get; set; }
    public Organisation.Project? Project { get; set; }

    public Guid? ProjectSiteId { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    /// <summary>Share of this employee's cost attributed to the project, where split.</summary>
    public decimal AllocationPercent { get; set; } = 100m;

    public decimal? RateOverrideAmount { get; set; }
    public string? RateOverrideCurrency { get; set; }

    public bool IsActive { get; set; } = true;

    public DateRange EffectivePeriod => new(StartDate, EndDate);
}
