using System.Globalization;
using System.Text;

namespace Tawaka.Application.Reports;

/// <summary>One exportable table: a title, its currency, headers and rows.</summary>
public sealed record ExportTable(
    string Title, string? CurrencyCode, IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Turns reports into CSV.
/// <para>
/// CSV first, deliberately: it opens in every spreadsheet an accountant owns, it carries no
/// formatting to go wrong, and it needs no dependency. Excel and PDF are a later convenience, not
/// a prerequisite for getting figures out of the system.
/// </para>
/// <para>
/// Currency is part of the export, not an assumption the reader makes. Each table names its
/// currency in the header block and every amount column is plain, unformatted decimal so that a
/// spreadsheet reads it as a number rather than as text.
/// </para>
/// </summary>
public static class ReportExporter
{
    /// <summary>
    /// Writes one or more tables into a single CSV, separated by blank lines with their own
    /// headings. A per-currency report exports as one table per currency, never merged.
    /// </summary>
    public static string ToCsv(
        string reportName, string periodName, string filterDescription,
        IReadOnlyList<ExportTable> tables)
    {
        var builder = new StringBuilder();

        builder.AppendLine(Escape($"Tawaka Payroll — {reportName}"));
        builder.AppendLine(Escape($"Period: {periodName}"));
        builder.AppendLine(Escape($"Filter: {filterDescription}"));
        builder.AppendLine(Escape(
            $"Exported: {DateTime.Now.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)}"));
        builder.AppendLine();

        foreach (var table in tables)
        {
            var heading = table.CurrencyCode is null
                ? table.Title
                : $"{table.Title} — {Label(table.CurrencyCode)}";

            builder.AppendLine(Escape(heading));

            if (table.CurrencyCode is not null)
            {
                // Stated on every table. Two tables in one file that did not say which currency
                // they were in is precisely how somebody adds them together.
                builder.AppendLine(Escape($"Currency: {Label(table.CurrencyCode)}"));
            }

            builder.AppendLine(string.Join(",", table.Headers.Select(Escape)));

            foreach (var row in table.Rows)
            {
                builder.AppendLine(string.Join(",", row.Select(Escape)));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string Amount(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// An absent figure exports as an empty cell, never as zero. A spreadsheet that shows 0.00
    /// where the engine could not produce a number is the same lie in a different medium.
    /// </summary>
    public static string Amount(decimal? value) =>
        value is null ? string.Empty : Amount(value.Value);

    public static string Text(string? value) => value ?? string.Empty;

    public static string Date(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    public static string Date(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

    public static string Label(string currencyCode) =>
        currencyCode == "ZWG" ? "ZiG" : currencyCode;

    /// <summary>
    /// RFC 4180 quoting. A payroll export containing an employee called "Moyo, John" must not
    /// silently become two columns.
    /// </summary>
    private static string Escape(string? value)
    {
        value ??= string.Empty;

        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
