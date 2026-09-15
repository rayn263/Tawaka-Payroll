using Tawaka.Domain.Statutory.Obligations;

namespace Tawaka.Application.Reports;

/// <summary>
/// Shapes each report into export tables.
/// <para>
/// One table per currency throughout, so the exported file has the same currency separation the
/// screen does. There is no combining step anywhere in this file.
/// </para>
/// </summary>
public static class PayrollReportCsv
{
    public static string Export(PayrollRunReports reports, string reportKey)
    {
        var tables = reportKey switch
        {
            "summary" => Summary(reports),
            "register" => Register(reports),
            "earnings" => Earnings(reports),
            "deductions" => Deductions(reports),
            "tax" => Tax(reports),
            "nssa" => Nssa(reports),
            "employer" => EmployerCost(reports),
            "project" => ProjectCost(reports),
            "currency" => CurrencySummary(reports),
            "inputs" => Inputs(reports),
            "statutory" => Statutory(reports),
            "payments" => Payments(reports),
            "audit" => Audit(reports),
            _ => new List<ExportTable>()
        };

        return ReportExporter.ToCsv(
            Name(reportKey), reports.PeriodName, reports.FilterDescription, tables);
    }

    public static string Name(string reportKey) => reportKey switch
    {
        "summary" => "Payroll summary",
        "register" => "Payroll register",
        "earnings" => "Employee earnings",
        "deductions" => "Deductions",
        "tax" => "PAYE and AIDS Levy",
        "nssa" => "NSSA",
        "employer" => "Employer cost",
        "project" => "Project and site labour cost",
        "currency" => "Currency summary",
        "inputs" => "Payroll inputs and snapshot",
        "statutory" => "Statutory obligations",
        "payments" => "Statutory payments",
        "audit" => "Payroll audit",
        _ => reportKey
    };

    private static List<ExportTable> Summary(PayrollRunReports reports) =>
        reports.Summary.Select(part => new ExportTable(
            "Payroll summary", part.CurrencyCode,
            new[]
            {
                "Employment type", "Employees", "Gross", "Taxable", "PAYE", "AIDS Levy",
                "NSSA employee", "Other deductions", "Net pay", "Employer cost"
            },
            part.Rows.Append(part.Totals).Select(r => (IReadOnlyList<string>)new[]
            {
                r.Key, r.Employees.ToString(), ReportExporter.Amount(r.Gross),
                ReportExporter.Amount(r.Taxable), ReportExporter.Amount(r.Paye),
                ReportExporter.Amount(r.AidsLevy), ReportExporter.Amount(r.NssaEmployee),
                ReportExporter.Amount(r.OtherDeductions), ReportExporter.Amount(r.NetPay),
                ReportExporter.Amount(r.EmployerCost)
            }).ToList())).ToList();

    private static List<ExportTable> Register(PayrollRunReports reports) =>
        reports.Register.Select(part => new ExportTable(
            "Payroll register", part.CurrencyCode,
            new[]
            {
                "Employee number", "Employee", "Type", "Department", "Project", "Gross", "Taxable",
                "PAYE", "AIDS Levy", "NSSA employee", "Other deductions", "Net pay",
                "Employer cost", "Unresolved"
            },
            part.Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.EmployeeNumber, r.EmployeeName, r.EmploymentType,
                ReportExporter.Text(r.Department), ReportExporter.Text(r.Project),
                ReportExporter.Amount(r.Gross), ReportExporter.Amount(r.Taxable),
                ReportExporter.Amount(r.Paye), ReportExporter.Amount(r.AidsLevy),
                ReportExporter.Amount(r.NssaEmployee), ReportExporter.Amount(r.OtherDeductions),
                ReportExporter.Amount(r.NetPay), ReportExporter.Amount(r.EmployerCost),
                r.UnresolvedCount == 0 ? string.Empty : r.UnresolvedCount.ToString()
            }).ToList())).ToList();

    private static List<ExportTable> Earnings(PayrollRunReports reports) =>
        reports.EmployeeEarnings.Select(part => new ExportTable(
            "Employee earnings", part.CurrencyCode,
            new[]
            {
                "Employee number", "Employee", "Code", "Earning", "Quantity", "Rate", "Amount",
                "Taxable", "Exempt"
            },
            part.Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.EmployeeNumber, r.EmployeeName, r.Code, r.Name,
                ReportExporter.Amount(r.Quantity), ReportExporter.Amount(r.Rate),
                ReportExporter.Amount(r.Amount), ReportExporter.Amount(r.Taxable),
                ReportExporter.Amount(r.Exempt)
            }).ToList())).ToList();

    private static List<ExportTable> Deductions(PayrollRunReports reports) =>
        reports.Deductions.Select(part => new ExportTable(
            "Deductions", part.CurrencyCode,
            new[] { "Code", "Deduction", "Type", "Employees", "Total" },
            part.Rows.Append(part.Totals).Select(r => (IReadOnlyList<string>)new[]
            {
                r.Code, r.Name, r.IsStatutory ? "Statutory" : "Other",
                r.Employees.ToString(), ReportExporter.Amount(r.Total)
            }).ToList())).ToList();

    private static List<ExportTable> Tax(PayrollRunReports reports) =>
        reports.Tax.Select(part => new ExportTable(
            "PAYE and AIDS Levy", part.CurrencyCode,
            new[]
            {
                "Employee number", "Employee", "Tax number", "Gross", "Taxable",
                "PAYE before credits", "Credits", "PAYE after credits", "AIDS Levy"
            },
            part.Rows.Append(part.Totals).Select(r => (IReadOnlyList<string>)new[]
            {
                r.EmployeeNumber, r.EmployeeName, ReportExporter.Text(r.TaxNumber),
                ReportExporter.Amount(r.Gross), ReportExporter.Amount(r.Taxable),
                ReportExporter.Amount(r.PayeBeforeCredits), ReportExporter.Amount(r.Credits),
                ReportExporter.Amount(r.PayeAfterCredits), ReportExporter.Amount(r.AidsLevy)
            }).ToList())).ToList();

    private static List<ExportTable> Nssa(PayrollRunReports reports) =>
        reports.Nssa.Select(part => new ExportTable(
            "NSSA", part.CurrencyCode,
            new[]
            {
                "Employee number", "Employee", "NSSA number", "Insurable earnings",
                "Employee contribution", "Employer contribution"
            },
            part.Rows.Append(part.Totals).Select(r => (IReadOnlyList<string>)new[]
            {
                r.EmployeeNumber, r.EmployeeName, ReportExporter.Text(r.NssaNumber),
                ReportExporter.Amount(r.InsurableEarnings),
                ReportExporter.Amount(r.EmployeeContribution),
                ReportExporter.Amount(r.EmployerContribution)
            }).ToList())).ToList();

    private static List<ExportTable> EmployerCost(PayrollRunReports reports) =>
        reports.EmployerCosts.Select(part => new ExportTable(
            "Employer cost", part.CurrencyCode,
            new[] { "Code", "Component", "Employees", "Base", "Amount" },
            part.Rows.Append(part.Totals).Select(r => (IReadOnlyList<string>)new[]
            {
                r.Code, r.Name, r.Employees.ToString(),
                ReportExporter.Amount(r.Base), ReportExporter.Amount(r.Amount)
            }).ToList())).ToList();

    private static List<ExportTable> ProjectCost(PayrollRunReports reports) =>
        reports.ProjectLabourCost.Select(part => new ExportTable(
            "Project and site labour cost", part.CurrencyCode,
            new[] { "Project", "Site", "Allocations", "Allocated cost", "Share %" },
            part.Rows.Append(part.Totals).Select(r => (IReadOnlyList<string>)new[]
            {
                r.ProjectName, ReportExporter.Text(r.SiteName), r.Employees.ToString(),
                ReportExporter.Amount(r.AllocatedCost), ReportExporter.Amount(r.Percent)
            }).ToList())).ToList();

    private static List<ExportTable> CurrencySummary(PayrollRunReports reports) =>
        new()
        {
            // One table, one row per currency, side by side and never totalled.
            new ExportTable(
                "Currency summary", null,
                new[]
                {
                    "Currency", "Employees", "Gross", "Deductions", "Net pay",
                    "Employer contributions", "Total employer cost"
                },
                reports.CurrencySummary.Select(r => (IReadOnlyList<string>)new[]
                {
                    r.CurrencyLabel, r.Employees.ToString(), ReportExporter.Amount(r.Gross),
                    ReportExporter.Amount(r.TotalDeductions), ReportExporter.Amount(r.NetPay),
                    ReportExporter.Amount(r.EmployerContributions),
                    ReportExporter.Amount(r.TotalEmployerCost)
                }).ToList())
        };

    private static List<ExportTable> Inputs(PayrollRunReports reports) =>
        new()
        {
            new ExportTable(
                "Payroll inputs and snapshot", null,
                new[]
                {
                    "Employee number", "Employee", "Input type", "Description", "Approved by",
                    "Approved at", "Snapshot hash"
                },
                reports.Inputs.Select(r => (IReadOnlyList<string>)new[]
                {
                    r.EmployeeNumber, r.EmployeeName, r.InputType, r.Description,
                    ReportExporter.Text(r.ApprovedBy), ReportExporter.Date(r.ApprovedAt),
                    r.SnapshotHash
                }).ToList())
        };

    private static List<ExportTable> Statutory(PayrollRunReports reports) =>
        reports.Statutory.Select(part => new ExportTable(
            "Statutory obligations", part.CurrencyCode,
            new[]
            {
                "Authority", "Obligation", "Due", "Calculated", "Deducted", "Approved", "Paid",
                "Outstanding", "Status"
            },
            part.Rows.Append(part.Totals).Select(r => (IReadOnlyList<string>)new[]
            {
                r.Authority.ToString(), r.ObligationType.ToString(), ReportExporter.Date(r.DueDate),
                ReportExporter.Amount(r.Calculated),
                r.DeductionApplicable ? ReportExporter.Amount(r.Deducted) : "n/a",
                ReportExporter.Amount(r.Approved), ReportExporter.Amount(r.Paid),
                ReportExporter.Amount(r.Outstanding), r.Status.ToString()
            }).ToList())).ToList();

    private static List<ExportTable> Payments(PayrollRunReports reports) =>
        reports.StatutoryPayments.Select(part => new ExportTable(
            "Statutory payments", part.CurrencyCode,
            new[]
            {
                "Obligation", "Authority", "Date", "Amount", "Principal", "Penalty or interest",
                "Method", "Reference", "Receipt", "Reversed", "Reversal reason"
            },
            part.Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.ObligationType.ToString(), r.Authority.ToString(),
                ReportExporter.Date(r.PaymentDate), ReportExporter.Amount(r.Amount),
                ReportExporter.Amount(r.PrincipalAmount),
                ReportExporter.Amount(r.PenaltyOrInterest), r.PaymentMethod, r.PaymentReference,
                ReportExporter.Text(r.ReceiptNumber), r.IsReversed ? "yes" : "no",
                ReportExporter.Text(r.ReversalReason)
            }).ToList())).ToList();

    private static List<ExportTable> Audit(PayrollRunReports reports) =>
        new()
        {
            new ExportTable(
                "Payroll audit", null,
                new[] { "Stage", "At", "Actor", "Detail" },
                reports.Audit.Select(r => (IReadOnlyList<string>)new[]
                {
                    r.Stage, ReportExporter.Date(r.At), ReportExporter.Text(r.ActorName),
                    ReportExporter.Text(r.Detail)
                }).ToList())
        };
}
