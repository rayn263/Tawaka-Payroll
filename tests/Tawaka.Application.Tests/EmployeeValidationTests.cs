using Tawaka.Application.Employees;
using Tawaka.Domain.Employees;
using Xunit;

namespace Tawaka.Application.Tests;

public class EmployeeValidationTests
{
    private static Employee Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeNumber = "EMP-0031",
        FirstName = "John",
        LastName = "Moyo",
        NationalId = "63-1234567 X 42",
        DateOfBirth = new DateOnly(1988, 4, 2),
        HireDate = new DateOnly(2021, 3, 4),
        Status = EmployeeStatus.Active
    };

    [Fact]
    public void A_complete_employee_passes()
    {
        Assert.True(EmployeeValidation.ValidateEmployee(Valid()).IsValid);
    }

    [Theory]
    [InlineData(nameof(Employee.EmployeeNumber))]
    [InlineData(nameof(Employee.FirstName))]
    [InlineData(nameof(Employee.LastName))]
    public void Required_fields_are_enforced(string field)
    {
        var employee = Valid();
        typeof(Employee).GetProperty(field)!.SetValue(employee, string.Empty);

        var result = EmployeeValidation.ValidateEmployee(employee);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Field == field);
    }

    [Fact]
    public void Date_of_birth_must_precede_engagement()
    {
        var employee = Valid();
        employee.DateOfBirth = new DateOnly(2022, 1, 1);

        Assert.False(EmployeeValidation.ValidateEmployee(employee).IsValid);
    }

    [Fact]
    public void Termination_date_cannot_precede_engagement()
    {
        var employee = Valid();
        employee.Status = EmployeeStatus.Terminated;
        employee.TerminationDate = new DateOnly(2020, 1, 1);

        Assert.False(EmployeeValidation.ValidateEmployee(employee).IsValid);
    }

    [Fact]
    public void A_terminated_employee_needs_a_termination_date()
    {
        var employee = Valid();
        employee.Status = EmployeeStatus.Terminated;

        var result = EmployeeValidation.ValidateEmployee(employee);

        Assert.Contains(result.Errors, e => e.Field == nameof(Employee.TerminationDate));
    }

    [Theory]
    [InlineData("63-1234567 X 42", true)]
    [InlineData("63-123456 A 07", true)]
    [InlineData("631234567X42", false)]
    [InlineData("not an id", false)]
    public void National_id_pattern_recognises_the_common_form(string value, bool expected)
    {
        Assert.Equal(expected, EmployeeValidation.LooksLikeNationalId(value));
    }

    /// <summary>
    /// Format checking is opt-in: district codes and check letters vary, and rejecting a genuine
    /// identity number is worse than accepting an unusual one.
    /// </summary>
    [Fact]
    public void An_unusual_national_id_is_accepted_unless_format_checking_is_enabled()
    {
        var employee = Valid();
        employee.NationalId = "08-99999999 Z 08";

        Assert.True(EmployeeValidation.ValidateEmployee(employee).IsValid);
        Assert.False(EmployeeValidation
            .ValidateEmployee(employee, enforceNationalIdFormat: true).IsValid);
    }
}

public class ContractValidationTests
{
    private static EmployeeContract Valid() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        EmploymentTypeId = Guid.NewGuid(),
        StartDate = new DateOnly(2026, 1, 1),
        PayrollCurrency = "USD",
        PaymentFrequency = PaymentFrequency.Monthly,
        EarningsBasis = EarningsBasis.MonthlySalary,
        MonthlyRate = 850m,
        StandardHoursPerDay = 8m,
        StandardDaysPerWeek = 5m
    };

    [Fact]
    public void A_complete_contract_passes()
    {
        Assert.True(EmployeeValidation.ValidateContract(Valid()).IsValid);
    }

    [Fact]
    public void End_date_cannot_precede_start_date()
    {
        var contract = Valid();
        contract.EndDate = new DateOnly(2025, 12, 31);

        var result = EmployeeValidation.ValidateContract(contract);

        Assert.Contains(result.Errors, e => e.Field == nameof(EmployeeContract.EndDate));
    }

    [Fact]
    public void Salary_cannot_be_negative()
    {
        var contract = Valid();
        contract.MonthlyRate = -1m;

        Assert.False(EmployeeValidation.ValidateContract(contract).IsValid);
    }

    [Fact]
    public void Currency_is_required()
    {
        var contract = Valid();
        contract.PayrollCurrency = string.Empty;

        Assert.False(EmployeeValidation.ValidateContract(contract).IsValid);
    }

    [Fact]
    public void The_rate_matching_the_earnings_basis_is_required()
    {
        var contract = Valid();
        contract.EarningsBasis = EarningsBasis.DailyRate;
        contract.DailyRate = null;

        var result = EmployeeValidation.ValidateContract(contract);

        Assert.Contains(result.Errors, e => e.Field == nameof(EmployeeContract.EarningsBasis));
    }

    [Fact]
    public void A_daily_rate_contract_passes_when_the_daily_rate_is_set()
    {
        var contract = Valid();
        contract.EarningsBasis = EarningsBasis.DailyRate;
        contract.MonthlyRate = null;
        contract.DailyRate = 25m;

        Assert.True(EmployeeValidation.ValidateContract(contract).IsValid);
    }
}

public class PaymentAccountValidationTests
{
    [Fact]
    public void A_bank_account_needs_bank_number_and_account_name()
    {
        var account = new EmployeePaymentAccount
        {
            PaymentMethod = PaymentMethod.BankTransfer,
            CurrencyCode = "USD"
        };

        var result = EmployeeValidation.ValidatePaymentAccount(account);

        Assert.Contains(result.Errors, e => e.Field == nameof(account.BankName));
        Assert.Contains(result.Errors, e => e.Field == nameof(account.AccountNumber));
        Assert.Contains(result.Errors, e => e.Field == nameof(account.AccountName));
    }

    [Fact]
    public void A_mobile_money_account_needs_provider_and_number()
    {
        var account = new EmployeePaymentAccount
        {
            PaymentMethod = PaymentMethod.MobileMoney,
            CurrencyCode = "ZWG"
        };

        var result = EmployeeValidation.ValidatePaymentAccount(account);

        Assert.Contains(result.Errors, e => e.Field == nameof(account.MobileMoneyProvider));
        Assert.Contains(result.Errors, e => e.Field == nameof(account.MobileMoneyNumber));
    }

    [Fact]
    public void Account_currency_is_always_required()
    {
        var account = new EmployeePaymentAccount { PaymentMethod = PaymentMethod.Cash };

        Assert.Contains(
            EmployeeValidation.ValidatePaymentAccount(account).Errors,
            e => e.Field == nameof(account.CurrencyCode));
    }

    [Fact]
    public void A_percentage_split_must_be_between_zero_and_one_hundred()
    {
        var account = new EmployeePaymentAccount
        {
            PaymentMethod = PaymentMethod.Cash,
            CurrencyCode = "USD",
            AllocationType = AllocationType.Percentage,
            AllocationValue = 120m
        };

        Assert.False(EmployeeValidation.ValidatePaymentAccount(account).IsValid);
    }

    [Fact]
    public void Account_number_is_masked_for_display()
    {
        var account = new EmployeePaymentAccount { AccountNumber = "0123456784821" };

        Assert.Equal("****4821", account.MaskedAccountNumber);
    }
}
