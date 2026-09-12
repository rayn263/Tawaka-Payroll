using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Companies;
using Tawaka.Domain.Earnings;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Organisation;
using Tawaka.Domain.Statutory;
using Tawaka.Infrastructure.Persistence;

namespace Tawaka.Infrastructure.Seeding;

/// <summary>
/// Creates the single company the first release operates with, together with its currencies,
/// employment types, and earning and deduction types.
/// <para>
/// Nothing here invents company details: the company is created unconfigured and the user is
/// prompted to complete it. Statutory treatments on earning and deduction types carry their real
/// verification grade, exactly as statutory rules do — an assumed treatment blocks live payroll
/// just as an assumed tax table does.
/// </para>
/// </summary>
public sealed class CompanySeeder
{
    private const string SpecReference = "ZIMBABWE_PAYROLL_COMPLIANCE_SPEC_V1.md";

    private readonly PayrollDbContext _context;

    public CompanySeeder(PayrollDbContext context)
    {
        _context = context;
    }

    public async Task<Company> SeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _context.Companies
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var company = new Company
        {
            LegalName = "Unconfigured Company",
            Country = "Zimbabwe",
            DefaultPayrollCurrency = "USD",
            DefaultReportingCurrency = "USD",
            AllowMixedCurrencyPayroll = false,
            IsActive = true
        };
        _context.Companies.Add(company);

        SeedCurrencies(company);
        SeedEmploymentTypes(company);
        SeedEarningTypes(company);
        SeedDeductionTypes(company);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return company;
    }

    private void SeedCurrencies(Company company)
    {
        _context.CompanyCurrencies.AddRange(
            new CompanyCurrency
            {
                CompanyId = company.Id,
                CurrencyCode = "USD",
                IsEnabled = true,
                IsDefaultPayrollCurrency = true,
                IsDefaultReportingCurrency = true,
                SortOrder = 1
            },
            new CompanyCurrency
            {
                CompanyId = company.Id,
                CurrencyCode = "ZWG",
                IsEnabled = true,
                IsDefaultPayrollCurrency = false,
                IsDefaultReportingCurrency = false,
                DisplayCodeOverride = "ZiG",
                SortOrder = 2
            });
    }

    /// <summary>
    /// The 11 employment categories. These carry payroll-processing defaults only: NSSA coverage
    /// comes from the eligibility rule matching <see cref="EmploymentType.Code"/>, and PAYE from
    /// the table matching the payment frequency.
    /// </summary>
    private void SeedEmploymentTypes(Company company)
    {
        var types = new (string Code, string Name, PaymentFrequency Frequency, EarningsBasis Basis,
            bool EndDate, bool Timesheet, bool Leave, int? WarnDays, string Description)[]
        {
            ("Permanent", "Permanent", PaymentFrequency.Monthly, EarningsBasis.MonthlySalary,
                false, false, true, null, "Employment without limit of time."),
            ("Contract", "Fixed-term contract", PaymentFrequency.Monthly, EarningsBasis.MonthlySalary,
                true, false, true, null, "Fixed duration; watch renewal under Labour Act s.12(3a)."),
            ("ProjectBased", "Project-based", PaymentFrequency.Monthly, EarningsBasis.DailyRate,
                true, true, false, null, "Engaged for a project; cost attributed to the project."),
            ("Temporary", "Temporary", PaymentFrequency.Monthly, EarningsBasis.MonthlySalary,
                true, true, true, null, "Short-term engagement; NSSA coverage applies."),
            ("Casual", "Casual", PaymentFrequency.Weekly, EarningsBasis.DailyRate,
                false, true, false, 42,
                "Not more than six weeks in any four consecutive months (Labour Act s.12(3)). " +
                "NSSA contributions apply only where engaged 18 days or more in a month."),
            ("Occasional", "Occasional", PaymentFrequency.PerEngagement, EarningsBasis.DailyRate,
                false, true, false, 42, "Engaged occasionally; treated as casual pending verification."),
            ("PartTime", "Part-time", PaymentFrequency.Monthly, EarningsBasis.HourlyRate,
                false, true, true, null, "Reduced hours; leave accrues pro rata."),
            ("Seasonal", "Seasonal", PaymentFrequency.Weekly, EarningsBasis.DailyRate,
                true, true, false, null, "Recurring seasonal engagement; NSSA coverage applies."),
            ("Intern", "Intern / Trainee", PaymentFrequency.Monthly, EarningsBasis.MonthlySalary,
                true, false, false, null, "Training placement; stipend often below the tax threshold."),
            ("CommissionBased", "Commission-based", PaymentFrequency.Monthly, EarningsBasis.Commission,
                false, false, true, null, "Paid on commission, with or without a retainer."),
            ("HourlyPaid", "Hourly-paid", PaymentFrequency.Weekly, EarningsBasis.HourlyRate,
                false, true, true, null, "Paid by the hour; uses the weekly PAYE table.")
        };

        var order = 1;
        foreach (var (code, name, frequency, basis, endDate, timesheet, leave, warnDays, description)
                 in types)
        {
            _context.EmploymentTypes.Add(new EmploymentType
            {
                CompanyId = company.Id,
                Code = code,
                Name = name,
                Description = description,
                DefaultPaymentFrequency = frequency,
                DefaultEarningsBasis = basis,
                RequiresContractEndDate = endDate,
                RequiresTimesheet = timesheet,
                AccruesLeave = leave,
                EngagementWarningDays = warnDays,
                DisplayOrder = order++,
                IsActive = true
            });
        }
    }

    private void SeedEarningTypes(Company company)
    {
        // (code, name, category, taxable, nssa, gross, levyBase, showZero, status, note)
        var types = new (string Code, string Name, EarningCategory Category, bool Taxable, bool Nssa,
            bool Gross, bool LevyBase, bool ShowZero, VerificationStatus Status, string Note)[]
        {
            ("BASIC", "Basic Salary", EarningCategory.Basic, true, true, true, true, true,
                VerificationStatus.Supported,
                "Insurable earnings are based on basic salary."),
            ("OVERTIME", "Overtime", EarningCategory.Overtime, true, false, true, true, true,
                VerificationStatus.Supported,
                "Taxable, but excluded from NSSA insurable earnings."),
            ("SUNDAY", "Sunday Work", EarningCategory.Overtime, true, false, true, true, false,
                VerificationStatus.Supported, "Treated as overtime for statutory purposes."),
            ("PUBHOL", "Public Holiday Work", EarningCategory.Overtime, true, false, true, true, false,
                VerificationStatus.Supported, "Treated as overtime for statutory purposes."),
            ("NIGHTSHIFT", "Night Shift Allowance", EarningCategory.Allowance, true, false, true, true, false,
                VerificationStatus.Unverified, "NSSA treatment not established."),
            ("HOUSING", "Housing Allowance", EarningCategory.Allowance, true, false, true, true, false,
                VerificationStatus.Unverified,
                "Taxable as a cash allowance. Whether it counts towards insurable earnings depends " +
                "on the SI 393/93 s.12 gross-up, which is unresolved (spec Q4a)."),
            ("TRANSPORT", "Transport Allowance", EarningCategory.Allowance, true, false, true, true, false,
                VerificationStatus.Supported,
                "A cash transport allowance is fully taxable."),
            ("TELEPHONE", "Telephone Allowance", EarningCategory.Allowance, true, false, true, true, false,
                VerificationStatus.Unverified, "Taxable unless reimbursive with proof."),
            ("MEAL", "Meal Allowance", EarningCategory.Allowance, true, false, true, true, false,
                VerificationStatus.Unverified, "Taxable; NSSA treatment not established."),
            ("TRAVEL", "Travel Allowance", EarningCategory.Allowance, true, false, true, true, false,
                VerificationStatus.Supported,
                "Taxable to the extent not expended on the employer's business."),
            ("SUBSIST", "Subsistence Allowance", EarningCategory.Allowance, true, false, true, true, false,
                VerificationStatus.Supported,
                "Taxable to the extent not expended on the employer's business."),
            ("RESPONS", "Responsibility Allowance", EarningCategory.Allowance, true, false, true, true, false,
                VerificationStatus.Unverified, "Not specifically addressed in the evidence."),
            ("RISK", "Risk Allowance", EarningCategory.Allowance, true, false, true, true, false,
                VerificationStatus.Unverified, "Not specifically addressed in the evidence."),
            ("PROJECT", "Project Allowance", EarningCategory.Allowance, true, false, true, true, false,
                VerificationStatus.Unverified, "Not specifically addressed in the evidence."),
            ("COMMISSION", "Commission", EarningCategory.Commission, true, false, true, true, false,
                VerificationStatus.Unverified,
                "Taxable remuneration; status in insurable earnings unclear."),
            ("BONUS_ANNUAL", "Annual Bonus", EarningCategory.Bonus, true, false, true, true, true,
                VerificationStatus.Supported,
                "Exempt up to the configured limit; excluded from NSSA insurable earnings."),
            ("BONUS_PERF", "Performance Bonus", EarningCategory.Bonus, true, false, true, true, false,
                VerificationStatus.Unverified,
                "Whether the bonus exemption extends beyond an annual bonus is unresolved (spec Q28)."),
            ("REIMB", "Expense Reimbursement", EarningCategory.Other, false, false, false, false, false,
                VerificationStatus.Supported,
                "Exempt where proof of the business expense is held. Not included in gross pay.")
        };

        var order = 1;
        foreach (var t in types)
        {
            _context.EarningTypes.Add(new EarningType
            {
                CompanyId = company.Id,
                Code = t.Code,
                Name = t.Name,
                Category = t.Category,
                IsTaxable = t.Taxable,
                IsNssaApplicable = t.Nssa,
                IsIncludedInGross = t.Gross,
                IsEmployerLevyBase = t.LevyBase,
                IsExemptUpToLimit = t.Code == "BONUS_ANNUAL",
                IsReimbursive = t.Code == "REIMB",
                RequiresExpenseProof = t.Code == "REIMB",
                DefaultMultiplier = t.Code switch
                {
                    "OVERTIME" => 1.5m,
                    "SUNDAY" => 2.0m,
                    "PUBHOL" => 2.0m,
                    _ => null
                },
                ShowOnPayslipWhenZero = t.ShowZero,
                IsSystemType = t.Code is "BASIC" or "OVERTIME",
                DisplayOrder = order++,
                TreatmentVerificationStatus = t.Status,
                TreatmentSource = "Compliance specification §14–16 (secondary sources)",
                TreatmentNotes = t.Note,
                IsActive = true
            });
        }
    }

    private void SeedDeductionTypes(Company company)
    {
        var types = new (string Code, string Name, DeductionCategory Category, bool ReducesTaxable,
            bool ShowZero, bool System, VerificationStatus Status, string Note)[]
        {
            ("PAYE", "PAYE", DeductionCategory.Statutory, false, true, true,
                VerificationStatus.Supported, "Calculated from the applicable PAYE table."),
            ("AIDSLEVY", "AIDS Levy", DeductionCategory.Statutory, false, true, true,
                VerificationStatus.Supported, "3% of tax after credits."),
            ("NSSA_EE", "NSSA Employee (POBS)", DeductionCategory.Statutory, true, true, true,
                VerificationStatus.Supported,
                "An allowable deduction against taxable income."),
            ("MEDAID", "Medical Aid", DeductionCategory.Benefit, false, true, false,
                VerificationStatus.Unverified,
                "Interacts with the medical credit, whose percentage is disputed (spec Q24)."),
            ("PENSION", "Pension Contribution", DeductionCategory.Benefit, true, true, false,
                VerificationStatus.Unverified,
                "Approved pension contributions are deductible within limits not yet established."),
            ("NECDUES", "NEC Dues", DeductionCategory.Union, false, false, false,
                VerificationStatus.Unverified,
                "Applies only to employees marked as members of a configured NEC (spec Q23)."),
            ("UNION", "Union Subscription", DeductionCategory.Union, false, false, false,
                VerificationStatus.Supported, "Deducted under the employee's written authority."),
            ("LOAN", "Loan Repayment", DeductionCategory.Loan, false, true, true,
                VerificationStatus.Supported, "Post-tax recovery of an employee loan."),
            ("ADVANCE", "Salary Advance", DeductionCategory.Advance, false, true, true,
                VerificationStatus.Supported, "Post-tax recovery of an advance."),
            ("STAFFPUR", "Staff Purchases", DeductionCategory.Other, false, false, false,
                VerificationStatus.Supported, "Deducted under the employee's written authority."),
            ("ACCOMM", "Accommodation", DeductionCategory.Other, false, false, false,
                VerificationStatus.Unverified, "May interact with the housing benefit rules."),
            ("GARNISH", "Garnishment", DeductionCategory.Garnishment, false, false, false,
                VerificationStatus.Unverified, "Applied only under a valid court order.")
        };

        var order = 1;
        foreach (var t in types)
        {
            _context.DeductionTypes.Add(new DeductionType
            {
                CompanyId = company.Id,
                Code = t.Code,
                Name = t.Name,
                Category = t.Category,
                ReducesTaxableIncome = t.ReducesTaxable,
                AppliesBeforeTax = t.ReducesTaxable,
                Priority = t.Category == DeductionCategory.Statutory ? 10 : 100 + order,
                ShowOnPayslipWhenZero = t.ShowZero,
                IsSystemType = t.System,
                DisplayOrder = order++,
                TreatmentVerificationStatus = t.Status,
                TreatmentSource = "Compliance specification (secondary sources)",
                TreatmentNotes = t.Note,
                IsActive = true
            });
        }
    }
}
