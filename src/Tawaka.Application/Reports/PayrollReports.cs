using Tawaka.Domain.Common;
using Tawaka.Domain.Statutory.Obligations;

namespace Tawaka.Application.Reports;

/// <summary>
/// A report section for exactly one currency.
/// <para>
/// Reports are shaped this way deliberately. There is no field anywhere in this file that holds a
/// total across currencies, because USD and ZiG are not commensurable: adding them produces a
/// number that means nothing and that somebody will eventually put in front of a director.
/// </para>
/// </summary>
public sealed record CurrencySection<T>(string CurrencyCode, T Totals, IReadOnlyList<T> Rows)
{
    public string CurrencyLabel => CurrencyCode == "ZWG" ? "ZiG" : CurrencyCode;
}

public sealed record PayrollSummaryRow(
    string Key, int Employees, decimal Gross, decimal Taxable, decimal Paye, decimal AidsLevy,
    decimal NssaEmployee, decimal OtherDeductions, decimal NetPay, decimal EmployerCost);

public sealed record PayrollRegisterRow(
    string EmployeeNumber, string EmployeeName, string EmploymentType, string? Department,
    string? Project, decimal? Gross, decimal? Taxable, decimal? Paye, decimal? AidsLevy,
    decimal? NssaEmployee, decimal? OtherDeductions, decimal? NetPay, decimal? EmployerCost,
    bool IsCalculated, int UnresolvedCount);

public sealed record DeductionReportRow(
    string Code, string Name, bool IsStatutory, int Employees, decimal Total);

public sealed record EmployerCostRow(
    string Code, string Name, int Employees, decimal Base, decimal Amount);

public sealed record StatutoryReportRow(
    StatutoryObligationType ObligationType, StatutoryAuthority Authority, DateOnly? DueDate,
    decimal Calculated, decimal Deducted, decimal Approved, decimal Paid, decimal Outstanding,
    bool DeductionApplicable, StatutoryObligationStatus Status);

public sealed record ProjectLabourCostRow(
    Guid? ProjectId, string ProjectName, int Employees, decimal AllocatedCost, decimal Percent);

/// <summary>Side-by-side currency totals. Deliberately has no combined figure.</summary>
public sealed record CurrencySummaryRow(
    string CurrencyCode, int Employees, decimal Gross, decimal TotalDeductions, decimal NetPay,
    decimal EmployerContributions, decimal TotalEmployerCost)
{
    public string CurrencyLabel => CurrencyCode == "ZWG" ? "ZiG" : CurrencyCode;
}

/// <summary>The full set of reports for one payroll run.</summary>
public sealed record PayrollRunReports
{
    public required string PeriodName { get; init; }
    public required DateOnly PayDate { get; init; }
    public required bool IsDevelopmentRun { get; init; }

    public IReadOnlyList<CurrencySection<PayrollSummaryRow>> Summary { get; init; } =
        Array.Empty<CurrencySection<PayrollSummaryRow>>();

    public IReadOnlyList<CurrencySection<PayrollRegisterRow>> Register { get; init; } =
        Array.Empty<CurrencySection<PayrollRegisterRow>>();

    public IReadOnlyList<CurrencySection<DeductionReportRow>> Deductions { get; init; } =
        Array.Empty<CurrencySection<DeductionReportRow>>();

    public IReadOnlyList<CurrencySection<EmployerCostRow>> EmployerCosts { get; init; } =
        Array.Empty<CurrencySection<EmployerCostRow>>();

    public IReadOnlyList<CurrencySection<StatutoryReportRow>> Statutory { get; init; } =
        Array.Empty<CurrencySection<StatutoryReportRow>>();

    public IReadOnlyList<CurrencySection<ProjectLabourCostRow>> ProjectLabourCost { get; init; } =
        Array.Empty<CurrencySection<ProjectLabourCostRow>>();

    public IReadOnlyList<CurrencySummaryRow> CurrencySummary { get; init; } =
        Array.Empty<CurrencySummaryRow>();

    /// <summary>Currencies present in this run, so a reader can see at a glance it is mixed.</summary>
    public IReadOnlyList<string> Currencies => CurrencySummary.Select(c => c.CurrencyCode).ToList();
}
