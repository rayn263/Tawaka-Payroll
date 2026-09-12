using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Earnings;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory;
using Xunit;

namespace Tawaka.Infrastructure.Tests;

public class CompanySeedingTests
{
    [Fact]
    public async Task Migration_creates_the_company_and_employee_tables()
    {
        using var db = new TestDatabase();

        var tables = db.TableNames();

        foreach (var expected in new[]
                 {
                     "Companies", "CompanyCurrencies", "CompanyBankAccounts",
                     "Departments", "JobTitles", "Locations", "Clients", "Projects", "ProjectSites",
                     "EmploymentTypes", "Employees", "EmployeeContracts",
                     "EmployeeStatutoryProfiles", "EmployeePaymentAccounts",
                     "EmployeeProjectAssignments", "EmployeeStatusHistory", "EmployeeNextOfKin",
                     "EmployeeDocuments", "EarningTypes", "DeductionTypes",
                     "EmployeeRecurringEarnings", "EmployeeRecurringDeductions",
                     "Users", "Roles", "Permissions", "RolePermissions", "UserRoles", "LoginAttempts"
                 })
        {
            Assert.Contains(expected, tables);
        }

        await Task.CompletedTask;
    }

    /// <summary>The company is created unconfigured rather than with invented details.</summary>
    [Fact]
    public async Task Seeding_creates_exactly_one_unconfigured_company()
    {
        using var db = new TestDatabase();
        var outcome = await db.SeedAllAsync();

        Assert.Equal(1, await db.Context.Companies.CountAsync());
        Assert.Equal("Unconfigured Company", outcome.Company.LegalName);
        Assert.Null(outcome.Company.TaxNumber);
        Assert.Null(outcome.Company.NssaEmployerNumber);
    }

    [Fact]
    public async Task Both_payroll_currencies_are_enabled_with_usd_as_default()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var currencies = await db.Context.CompanyCurrencies.AsNoTracking()
            .OrderBy(c => c.SortOrder).ToListAsync();

        Assert.Equal(2, currencies.Count);
        Assert.True(currencies.All(c => c.IsEnabled));
        Assert.Equal("USD", currencies.Single(c => c.IsDefaultPayrollCurrency).CurrencyCode);
        Assert.Equal("ZiG", currencies.Single(c => c.CurrencyCode == "ZWG").DisplayCodeOverride);
    }

    [Fact]
    public async Task All_eleven_employment_types_are_seeded()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var types = await db.Context.EmploymentTypes.AsNoTracking().ToListAsync();

        Assert.Equal(11, types.Count);
        foreach (var code in new[]
                 {
                     "Permanent", "Contract", "ProjectBased", "Temporary", "Casual", "Occasional",
                     "PartTime", "Seasonal", "Intern", "CommissionBased", "HourlyPaid"
                 })
        {
            Assert.Contains(types, t => t.Code == code);
        }
    }

    /// <summary>
    /// Every employment type code must have a matching NSSA eligibility rule, otherwise coverage
    /// would silently fall back to an assumption.
    /// </summary>
    [Fact]
    public async Task Every_employment_type_has_a_matching_nssa_eligibility_rule()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var typeCodes = await db.Context.EmploymentTypes.AsNoTracking()
            .Select(t => t.Code).ToListAsync();
        var ruleCodes = await db.Context.NssaEligibilityRules.AsNoTracking()
            .Select(r => r.EmploymentTypeCode).ToListAsync();

        foreach (var code in typeCodes)
        {
            Assert.Contains(code, ruleCodes);
        }
    }

    [Fact]
    public async Task Casual_employment_carries_the_six_week_warning_threshold()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var casual = await db.Context.EmploymentTypes.AsNoTracking()
            .SingleAsync(t => t.Code == "Casual");

        Assert.Equal(42, casual.EngagementWarningDays);
        Assert.True(casual.RequiresTimesheet);
        Assert.False(casual.AccruesLeave);
    }

    [Fact]
    public async Task Fixed_term_types_require_a_contract_end_date()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var types = await db.Context.EmploymentTypes.AsNoTracking().ToListAsync();

        Assert.True(types.Single(t => t.Code == "Contract").RequiresContractEndDate);
        Assert.False(types.Single(t => t.Code == "Permanent").RequiresContractEndDate);
    }

    /// <summary>
    /// Overtime is taxable but excluded from NSSA insurable earnings. A system that applied NSSA
    /// to gross would over-deduct from exactly the site staff who work the most overtime.
    /// </summary>
    [Fact]
    public async Task Overtime_is_taxable_but_excluded_from_nssa()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var overtime = await db.Context.EarningTypes.AsNoTracking().SingleAsync(e => e.Code == "OVERTIME");

        Assert.True(overtime.IsTaxable);
        Assert.False(overtime.IsNssaApplicable);
        Assert.Equal(1.5m, overtime.DefaultMultiplier);
    }

    [Fact]
    public async Task Bonuses_are_excluded_from_nssa_and_the_annual_bonus_is_exempt_up_to_a_limit()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var bonus = await db.Context.EarningTypes.AsNoTracking()
            .SingleAsync(e => e.Code == "BONUS_ANNUAL");

        Assert.False(bonus.IsNssaApplicable);
        Assert.True(bonus.IsExemptUpToLimit);
    }

    [Fact]
    public async Task A_reimbursement_is_not_taxable_and_not_part_of_gross_pay()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var reimbursement = await db.Context.EarningTypes.AsNoTracking()
            .SingleAsync(e => e.Code == "REIMB");

        Assert.False(reimbursement.IsTaxable);
        Assert.False(reimbursement.IsIncludedInGross);
        Assert.True(reimbursement.RequiresExpenseProof);
    }

    /// <summary>
    /// Statutory treatment of an allowance is configuration carrying its own verification grade —
    /// exactly like a tax table — so an assumed treatment is visible rather than silent.
    /// </summary>
    [Fact]
    public async Task Every_earning_and_deduction_type_records_its_treatment_grade_and_source()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var earnings = await db.Context.EarningTypes.AsNoTracking().ToListAsync();
        var deductions = await db.Context.DeductionTypes.AsNoTracking().ToListAsync();

        Assert.NotEmpty(earnings);
        Assert.NotEmpty(deductions);

        foreach (var type in earnings)
        {
            Assert.NotEqual(VerificationStatus.Verified, type.TreatmentVerificationStatus);
            Assert.False(string.IsNullOrWhiteSpace(type.TreatmentSource));
            Assert.False(string.IsNullOrWhiteSpace(type.TreatmentNotes));
        }

        foreach (var type in deductions)
        {
            Assert.NotEqual(VerificationStatus.Verified, type.TreatmentVerificationStatus);
            Assert.False(string.IsNullOrWhiteSpace(type.TreatmentNotes));
        }
    }

    [Fact]
    public async Task The_nssa_employee_deduction_reduces_taxable_income()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();

        var nssa = await db.Context.DeductionTypes.AsNoTracking().SingleAsync(d => d.Code == "NSSA_EE");
        var loan = await db.Context.DeductionTypes.AsNoTracking().SingleAsync(d => d.Code == "LOAN");

        Assert.True(nssa.ReducesTaxableIncome);
        Assert.False(loan.ReducesTaxableIncome);
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        using var db = new TestDatabase();
        await db.SeedAllAsync();
        var companies = await db.Context.Companies.CountAsync();
        var types = await db.Context.EmploymentTypes.CountAsync();
        var users = await db.Context.Users.CountAsync();

        await db.SeedAllAsync();

        Assert.Equal(companies, await db.Context.Companies.CountAsync());
        Assert.Equal(types, await db.Context.EmploymentTypes.CountAsync());
        Assert.Equal(users, await db.Context.Users.CountAsync());
    }

    [Fact]
    public async Task A_company_bank_account_is_named_independently_of_the_company()
    {
        using var db = new TestDatabase();
        var outcome = await db.SeedAllAsync();

        var company = await db.Context.Companies.SingleAsync();
        company.LegalName = "Tawaka Construction (Private) Limited";
        company.TradingName = "Tawaka Builders";

        db.Context.CompanyBankAccounts.Add(new Domain.Companies.CompanyBankAccount
        {
            CompanyId = outcome.Company.Id,
            AccountName = "TAWAKA CONSTR PVT LTD T/A TAWAKA BUILDERS",
            BankName = "CBZ Bank",
            AccountNumber = "01234567890",
            CurrencyCode = "USD",
            Purpose = Domain.Companies.PaymentPurpose.NetPay,
            IsDefaultForPurpose = true
        });
        await db.Context.SaveChangesAsync();

        var account = await db.Context.CompanyBankAccounts.AsNoTracking().SingleAsync();

        Assert.NotEqual(company.LegalName, account.AccountName);
        Assert.NotEqual(company.TradingName, account.AccountName);
        Assert.Equal("Tawaka Builders", company.DisplayName);
    }

    [Fact]
    public async Task A_company_may_hold_separate_accounts_per_currency_and_purpose()
    {
        using var db = new TestDatabase();
        var outcome = await db.SeedAllAsync();

        db.Context.CompanyBankAccounts.AddRange(
            new Domain.Companies.CompanyBankAccount
            {
                CompanyId = outcome.Company.Id, AccountName = "Tawaka USD",
                BankName = "CBZ Bank", AccountNumber = "0001", CurrencyCode = "USD",
                Purpose = Domain.Companies.PaymentPurpose.NetPay
            },
            new Domain.Companies.CompanyBankAccount
            {
                CompanyId = outcome.Company.Id, AccountName = "Tawaka ZiG",
                BankName = "CBZ Bank", AccountNumber = "0002", CurrencyCode = "ZWG",
                Purpose = Domain.Companies.PaymentPurpose.NetPay
            },
            new Domain.Companies.CompanyBankAccount
            {
                CompanyId = outcome.Company.Id, AccountName = "Tawaka Statutory USD",
                BankName = "CBZ Bank", AccountNumber = "0003", CurrencyCode = "USD",
                Purpose = Domain.Companies.PaymentPurpose.Paye
            });
        await db.Context.SaveChangesAsync();

        Assert.Equal(3, await db.Context.CompanyBankAccounts.CountAsync());
    }
}
