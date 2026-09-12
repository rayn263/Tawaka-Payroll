using System.Text.RegularExpressions;
using Tawaka.Application.Common;
using Tawaka.Domain.Employees;

namespace Tawaka.Application.Employees;

/// <summary>
/// Validation for employee and contract data.
/// <para>
/// The national identity format is treated as a <i>warning-grade</i> pattern, not a hard rule:
/// the common Zimbabwean form is "63-1234567 X 42", but registration district codes and check
/// letters vary, and rejecting a genuine identity number is worse than accepting an unusual one.
/// Format checking is therefore opt-in per company.
/// </para>
/// </summary>
public static partial class EmployeeValidation
{
    [GeneratedRegex(@"^\d{2}-\d{6,7}\s?[A-Za-z]\s?\d{2}$")]
    private static partial Regex NationalIdPattern();

    public static bool LooksLikeNationalId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && NationalIdPattern().IsMatch(value.Trim());

    public static ValidationResult ValidateEmployee(
        Employee employee, bool enforceNationalIdFormat = false)
    {
        ArgumentNullException.ThrowIfNull(employee);
        var result = ValidationResult.Success();

        result.Require(employee.EmployeeNumber, nameof(employee.EmployeeNumber));
        result.Require(employee.FirstName, nameof(employee.FirstName));
        result.Require(employee.LastName, nameof(employee.LastName));

        result.AddIf(employee.CompanyId == Guid.Empty, nameof(employee.CompanyId),
            "Employee must belong to a company.");

        result.AddIf(employee.HireDate == default, nameof(employee.HireDate),
            "Engagement date is required.");

        result.AddIf(
            employee.DateOfBirth.HasValue && employee.HireDate != default &&
            employee.DateOfBirth.Value >= employee.HireDate,
            nameof(employee.DateOfBirth),
            "Date of birth must precede the engagement date.");

        result.AddIf(
            employee.TerminationDate.HasValue && employee.TerminationDate.Value < employee.HireDate,
            nameof(employee.TerminationDate),
            "Termination date cannot precede the engagement date.");

        result.AddIf(
            employee.Status == EmployeeStatus.Terminated && employee.TerminationDate is null,
            nameof(employee.TerminationDate),
            "A terminated employee must have a termination date.");

        result.AddIf(
            enforceNationalIdFormat && !string.IsNullOrWhiteSpace(employee.NationalId) &&
            !LooksLikeNationalId(employee.NationalId),
            nameof(employee.NationalId),
            "National ID does not match the expected format (for example 63-1234567 X 42).");

        result.AddIf(
            !string.IsNullOrWhiteSpace(employee.Email) && !employee.Email.Contains('@'),
            nameof(employee.Email),
            "Email address is not valid.");

        return result;
    }

    public static ValidationResult ValidateContract(EmployeeContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var result = ValidationResult.Success();

        result.AddIf(contract.EmployeeId == Guid.Empty, nameof(contract.EmployeeId),
            "Contract must belong to an employee.");
        result.AddIf(contract.EmploymentTypeId == Guid.Empty, nameof(contract.EmploymentTypeId),
            "Employment type is required.");
        result.AddIf(contract.StartDate == default, nameof(contract.StartDate),
            "Contract start date is required.");

        result.AddIf(
            contract.EndDate.HasValue && contract.EndDate.Value < contract.StartDate,
            nameof(contract.EndDate),
            "Contract end date cannot precede the start date.");

        result.Require(contract.PayrollCurrency, nameof(contract.PayrollCurrency),
            "Payroll currency is required.");

        foreach (var (value, name) in new (decimal?, string)[]
                 {
                     (contract.BasicSalary, nameof(contract.BasicSalary)),
                     (contract.HourlyRate, nameof(contract.HourlyRate)),
                     (contract.DailyRate, nameof(contract.DailyRate)),
                     (contract.WeeklyRate, nameof(contract.WeeklyRate)),
                     (contract.MonthlyRate, nameof(contract.MonthlyRate)),
                     (contract.ProjectRate, nameof(contract.ProjectRate))
                 })
        {
            result.AddIf(value is < 0m, name, "Amount cannot be negative.");
        }

        result.AddIf(contract.PrimaryRate is null or 0m, nameof(contract.EarningsBasis),
            $"A rate is required for the '{contract.EarningsBasis}' earnings basis.");

        result.AddIf(contract.StandardHoursPerDay <= 0m, nameof(contract.StandardHoursPerDay),
            "Standard hours per day must be greater than zero.");
        result.AddIf(contract.StandardDaysPerWeek <= 0m, nameof(contract.StandardDaysPerWeek),
            "Standard days per week must be greater than zero.");

        return result;
    }

    public static ValidationResult ValidatePaymentAccount(EmployeePaymentAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var result = ValidationResult.Success();

        result.Require(account.CurrencyCode, nameof(account.CurrencyCode),
            "Account currency is required.");

        switch (account.PaymentMethod)
        {
            case PaymentMethod.BankTransfer:
                result.Require(account.BankName, nameof(account.BankName));
                result.Require(account.AccountNumber, nameof(account.AccountNumber));
                result.Require(account.AccountName, nameof(account.AccountName),
                    "The name the account is held in is required.");
                break;
            case PaymentMethod.MobileMoney:
                result.Require(account.MobileMoneyProvider, nameof(account.MobileMoneyProvider));
                result.Require(account.MobileMoneyNumber, nameof(account.MobileMoneyNumber));
                break;
        }

        result.AddIf(
            account.AllocationType != AllocationType.FullBalance && account.AllocationValue is null,
            nameof(account.AllocationValue),
            "An allocation value is required for a percentage or fixed-amount split.");

        result.AddIf(
            account.AllocationType == AllocationType.Percentage &&
            account.AllocationValue is < 0m or > 100m,
            nameof(account.AllocationValue),
            "Percentage allocation must be between 0 and 100.");

        result.AddIf(account.AllocationValue is < 0m, nameof(account.AllocationValue),
            "Allocation cannot be negative.");

        return result;
    }
}
