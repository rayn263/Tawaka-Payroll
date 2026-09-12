using Tawaka.Domain.Common;

namespace Tawaka.Domain.Payroll;

public enum SettingCategory
{
    Company = 0,
    Payroll = 1,
    Tax = 2,
    Currency = 3,
    Payslip = 4,
    Security = 5
}

/// <summary>
/// Typed key/value configuration. Key/value rather than a wide table so a new setting does not
/// require a migration; typed accessors sit in the application layer.
/// </summary>
public class AppSetting : AuditableEntity
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string DataType { get; set; } = "string";
    public SettingCategory Category { get; set; }
    public string? Description { get; set; }
}

/// <summary>Well-known setting keys, so they are not scattered as string literals.</summary>
public static class SettingKeys
{
    public const string PayrollMode = "Payroll.Mode";
    public const string DefaultCurrency = "Payroll.DefaultCurrency";
    public const string ReportingCurrency = "Payroll.ReportingCurrency";
    public const string AllowMixedCurrencyPayroll = "Payroll.AllowMixedCurrency";
    public const string EnforceSegregationOfDuties = "Security.EnforceSegregationOfDuties";
    public const string CompanyName = "Company.Name";
}
