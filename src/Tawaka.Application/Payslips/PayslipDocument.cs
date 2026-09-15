using Tawaka.Domain.Common;
using Tawaka.Domain.Payroll;

namespace Tawaka.Application.Payslips;

/// <summary>
/// How a figure on a payslip should be read.
/// <para>
/// The distinction that matters: a <see cref="Zero"/> is a real calculated 0.00 and is shown as
/// such, while <see cref="Unresolved"/> means the rule was missing or unverified and the figure
/// could not be produced. Rendering the second as "0.00" would state that nothing was due, which
/// is a different and possibly false claim.
/// </para>
/// </summary>
public enum PayslipValueState
{
    Calculated = 0,
    Zero = 1,
    Unresolved = 2
}

/// <summary>One figure on a payslip, with its state and currency.</summary>
public sealed record PayslipAmount(Money? Value, PayslipValueState State, string? UnresolvedReason = null)
{
    public static PayslipAmount From(decimal? amount, CurrencyCode currency, string? reason = null)
    {
        if (amount is null)
        {
            return new PayslipAmount(null, PayslipValueState.Unresolved, reason);
        }

        var money = new Money(amount.Value, currency);
        return new PayslipAmount(money,
            money.IsZero ? PayslipValueState.Zero : PayslipValueState.Calculated);
    }

    /// <summary>Renders as an amount, or as an em dash where the figure could not be produced.</summary>
    public string Display() => State == PayslipValueState.Unresolved
        ? "—"
        : Value!.Value.Amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

    public decimal AmountOrZero => Value?.Amount ?? 0m;
}

public sealed record PayslipLine(
    string Code, string Name, PayslipAmount Amount, string? Detail = null,
    decimal? OriginalAmount = null, string? OriginalCurrency = null,
    decimal? ExchangeRate = null, DateOnly? RateDate = null, string? RateSource = null)
{
    /// <summary>True where the line originated in a currency other than the payslip's.</summary>
    public bool IsConverted => OriginalCurrency is not null;
}

/// <summary>Where a statutory amount has reached, without asserting payment that has not happened.</summary>
public sealed record PayslipStatutoryStatus(
    string Name, PayslipAmount Amount, bool IsDeducted, bool IsApproved, bool IsRemitted,
    bool DeductionApplicable = true);

public sealed record PayslipCompany(
    string Name, string? Address, string? Phone, string? Email, string? TaxNumber,
    string? NssaEmployerNumber, string? LogoPath);

public sealed record PayslipEmployee(
    string Name, string Number, string? NationalId, string? JobTitle, string? Department,
    string EmploymentType, string? Project, DateOnly HireDate, string? TaxNumber,
    string? NssaNumber, string? PaymentMethod, string? AccountDetail);

/// <summary>
/// The canonical payslip.
/// <para>
/// Assembled entirely from persisted payroll results — it performs no payroll arithmetic. One
/// document type serves the screen, the printer and the PDF, so they cannot drift apart.
/// </para>
/// </summary>
public sealed record PayslipDocument
{
    public const string TemplateVersion = "1.0";

    public required PayslipCompany Company { get; init; }
    public required PayslipEmployee Employee { get; init; }

    public required string PeriodName { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required DateOnly PayDate { get; init; }
    public required CurrencyCode Currency { get; init; }

    public string? PayslipNumber { get; init; }
    public int Revision { get; init; } = 1;

    /// <summary>
    /// True when the run was a development calculation. Such a payslip is watermarked and is not
    /// an issuable document.
    /// </summary>
    public required bool IsDevelopmentCopy { get; init; }

    public IReadOnlyList<PayslipLine> Earnings { get; init; } = Array.Empty<PayslipLine>();
    public IReadOnlyList<PayslipLine> Deductions { get; init; } = Array.Empty<PayslipLine>();
    public IReadOnlyList<PayslipLine> EmployerContributions { get; init; } = Array.Empty<PayslipLine>();
    public IReadOnlyList<PayslipStatutoryStatus> StatutoryStatus { get; init; } =
        Array.Empty<PayslipStatutoryStatus>();

    public required PayslipAmount GrossEarnings { get; init; }
    public required PayslipAmount TotalDeductions { get; init; }
    public required PayslipAmount NetPay { get; init; }
    public required PayslipAmount TotalEmployerCost { get; init; }

    public IReadOnlyList<string> UnresolvedNotes { get; init; } = Array.Empty<string>();

    /// <summary>The currency label a Zimbabwean reader expects: ZiG rather than ZWG.</summary>
    public string CurrencyLabel => Currency.Value == "ZWG" ? "ZiG" : Currency.Value;

    public bool HasUnresolvedFigures => UnresolvedNotes.Count > 0;

    /// <summary>
    /// The sum of the earning lines included in gross. Used by a test to assert the rendered lines
    /// reconcile to the stored total, which is the guard against the document quietly diverging
    /// from the payroll.
    /// </summary>
    public decimal EarningLineTotal => Earnings
        .Where(l => l.Amount.State != PayslipValueState.Unresolved)
        .Sum(l => l.Amount.AmountOrZero);

    public decimal DeductionLineTotal => Deductions
        .Where(l => l.Amount.State != PayslipValueState.Unresolved)
        .Sum(l => l.Amount.AmountOrZero);
}
