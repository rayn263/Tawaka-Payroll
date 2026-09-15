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
    Guid? ProjectId, string ProjectName, int Employees, decimal AllocatedCost, decimal Percent,
    Guid? ProjectSiteId = null, string? SiteName = null);

/// <summary>Side-by-side currency totals. Deliberately has no combined figure.</summary>
public sealed record CurrencySummaryRow(
    string CurrencyCode, int Employees, decimal Gross, decimal TotalDeductions, decimal NetPay,
    decimal EmployerContributions, decimal TotalEmployerCost)
{
    public string CurrencyLabel => CurrencyCode == "ZWG" ? "ZiG" : CurrencyCode;
}

/// <summary>
/// Filters a report may be narrowed by. Applied before any grouping, so a filtered report's totals
/// are the totals of what is shown — never a subset presented under a full-run heading.
/// </summary>
public sealed record ReportFilter
{
    public Guid? EmployeeId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? ProjectId { get; init; }
    public Guid? ProjectSiteId { get; init; }
    public string? EmploymentTypeCode { get; init; }
    public string? CurrencyCode { get; init; }

    /// <summary>True to include only employees whose figures fully calculated.</summary>
    public bool? IsCalculated { get; init; }

    public bool IsEmpty =>
        EmployeeId is null && DepartmentId is null && ProjectId is null && ProjectSiteId is null &&
        EmploymentTypeCode is null && CurrencyCode is null && IsCalculated is null;

    /// <summary>A plain description of what was applied, for the report header and the export.</summary>
    public string Describe(IReadOnlyDictionary<Guid, string>? names = null)
    {
        if (IsEmpty)
        {
            return "All employees";
        }

        var parts = new List<string>();
        void Add(string label, Guid? id)
        {
            if (id is { } value)
            {
                parts.Add(names is not null && names.TryGetValue(value, out var name)
                    ? $"{label}: {name}"
                    : $"{label}: {value}");
            }
        }

        Add("Employee", EmployeeId);
        Add("Department", DepartmentId);
        Add("Project", ProjectId);
        Add("Site", ProjectSiteId);

        if (EmploymentTypeCode is not null) parts.Add($"Employment type: {EmploymentTypeCode}");
        if (CurrencyCode is not null) parts.Add($"Currency: {CurrencyCode}");
        if (IsCalculated is { } calculated)
        {
            parts.Add(calculated ? "Fully calculated only" : "With unresolved figures only");
        }

        return string.Join(" · ", parts);
    }
}

/// <summary>Per-employee earnings, broken down by earning code.</summary>
public sealed record EmployeeEarningsRow(
    string EmployeeNumber, string EmployeeName, string Code, string Name,
    decimal? Quantity, decimal? Rate, decimal Amount, decimal Taxable, decimal Exempt);

/// <summary>PAYE and AIDS Levy per employee — the two ZIMRA obligations, reported together.</summary>
public sealed record TaxReportRow(
    string EmployeeNumber, string EmployeeName, string? TaxNumber,
    decimal? Gross, decimal? Taxable, decimal? PayeBeforeCredits, decimal? Credits,
    decimal? PayeAfterCredits, decimal? AidsLevy);

/// <summary>NSSA per employee: insurable earnings and both sides of the contribution.</summary>
public sealed record NssaReportRow(
    string EmployeeNumber, string EmployeeName, string? NssaNumber,
    decimal? InsurableEarnings, decimal? EmployeeContribution, decimal? EmployerContribution);

/// <summary>What a run actually consumed, per employee — the audit answer to "based on what?".</summary>
public sealed record PayrollInputRow(
    string EmployeeNumber, string EmployeeName, string InputType, string Description,
    string? ApprovedBy, DateTimeOffset? ApprovedAt, string SnapshotHash);

/// <summary>Statutory payments actually made, with their evidence.</summary>
public sealed record StatutoryPaymentRow(
    StatutoryObligationType ObligationType, StatutoryAuthority Authority, DateOnly PaymentDate,
    decimal Amount, decimal PrincipalAmount, decimal PenaltyOrInterest, string PaymentMethod,
    string PaymentReference, string? ReceiptNumber, bool IsReversed, string? ReversalReason);

/// <summary>The lifecycle of a payroll run, with who did what and when.</summary>
public sealed record PayrollAuditRow(
    string Stage, DateTimeOffset? At, string? ActorId, string? ActorName, string? Detail);

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

    public IReadOnlyList<CurrencySection<EmployeeEarningsRow>> EmployeeEarnings { get; init; } =
        Array.Empty<CurrencySection<EmployeeEarningsRow>>();

    public IReadOnlyList<CurrencySection<TaxReportRow>> Tax { get; init; } =
        Array.Empty<CurrencySection<TaxReportRow>>();

    public IReadOnlyList<CurrencySection<NssaReportRow>> Nssa { get; init; } =
        Array.Empty<CurrencySection<NssaReportRow>>();

    public IReadOnlyList<PayrollInputRow> Inputs { get; init; } = Array.Empty<PayrollInputRow>();

    public IReadOnlyList<CurrencySection<StatutoryPaymentRow>> StatutoryPayments { get; init; } =
        Array.Empty<CurrencySection<StatutoryPaymentRow>>();

    public IReadOnlyList<PayrollAuditRow> Audit { get; init; } = Array.Empty<PayrollAuditRow>();

    /// <summary>The filter these figures were produced under, stated on the report itself.</summary>
    public ReportFilter Filter { get; init; } = new();

    public string FilterDescription { get; init; } = "All employees";

    public IReadOnlyList<CurrencySummaryRow> CurrencySummary { get; init; } =
        Array.Empty<CurrencySummaryRow>();

    /// <summary>Currencies present in this run, so a reader can see at a glance it is mixed.</summary>
    public IReadOnlyList<string> Currencies => CurrencySummary.Select(c => c.CurrencyCode).ToList();
}
