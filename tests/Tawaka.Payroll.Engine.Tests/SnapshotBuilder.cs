using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Inputs;

namespace Tawaka.Payroll.Engine.Tests;

/// <summary>
/// Builds snapshots for tests, using the seed statutory rules from the compliance specification.
/// <para>
/// Everything is a named default that a test can override, so each test states only what it is
/// actually about.
/// </para>
/// </summary>
public sealed class SnapshotBuilder
{
    private readonly List<EarningInput> _earnings = new();
    private readonly List<DeductionInput> _deductions = new();
    private readonly List<CostAllocationInput> _allocations = new();

    private CurrencyCode _currency = CurrencyCode.Usd;
    private PeriodBasis _periodBasis = PeriodBasis.Monthly;
    private PayrollMode _mode = PayrollMode.Development;
    private string _employmentType = "Permanent";
    private StatutoryProfileInput _profile = new() { Age = 38, TaxNumber = "BP123", NssaNumber = "N1" };
    private TimesheetInput? _timesheet;
    private ResolvedRules? _rules;
    private Money? _bonusExemptionUsed;

    public static SnapshotBuilder Usd() => new();

    public static SnapshotBuilder Zwg() => new SnapshotBuilder().InCurrency(CurrencyCode.Zwg);

    public SnapshotBuilder InCurrency(CurrencyCode currency)
    {
        _currency = currency;
        return this;
    }

    public SnapshotBuilder WithPeriod(PeriodBasis basis)
    {
        _periodBasis = basis;
        return this;
    }

    public SnapshotBuilder InLiveMode()
    {
        _mode = PayrollMode.Live;
        return this;
    }

    public SnapshotBuilder OfType(string employmentTypeCode)
    {
        _employmentType = employmentTypeCode;
        return this;
    }

    public SnapshotBuilder WithProfile(Func<StatutoryProfileInput, StatutoryProfileInput> configure)
    {
        _profile = configure(_profile);
        return this;
    }

    public SnapshotBuilder WithTimesheet(TimesheetInput timesheet)
    {
        _timesheet = timesheet;
        return this;
    }

    public SnapshotBuilder WithRules(ResolvedRules rules)
    {
        _rules = rules;
        return this;
    }

    public SnapshotBuilder BonusExemptionUsed(decimal amount)
    {
        _bonusExemptionUsed = new Money(amount, _currency);
        return this;
    }

    public SnapshotBuilder Basic(decimal amount)
    {
        _earnings.Add(new EarningInput
        {
            Code = "BASIC", Name = "Basic Salary", Amount = new Money(amount, _currency),
            IsTaxable = true, IsNssaApplicable = true, IsIncludedInGross = true, IsBasic = true,
            TreatmentVerification = VerificationStatus.Verified
        });
        return this;
    }

    public SnapshotBuilder Allowance(string code, string name, decimal amount,
        bool nssaApplicable = false, VerificationStatus treatment = VerificationStatus.Verified)
    {
        _earnings.Add(new EarningInput
        {
            Code = code, Name = name, Amount = new Money(amount, _currency),
            IsTaxable = true, IsNssaApplicable = nssaApplicable, IsIncludedInGross = true,
            TreatmentVerification = treatment
        });
        return this;
    }

    public SnapshotBuilder AllowanceInCurrency(string code, string name, decimal amount,
        CurrencyCode currency)
    {
        _earnings.Add(new EarningInput
        {
            Code = code, Name = name, Amount = new Money(amount, currency),
            IsTaxable = true, IsIncludedInGross = true,
            TreatmentVerification = VerificationStatus.Verified
        });
        return this;
    }

    public SnapshotBuilder Overtime(decimal amount, decimal hours = 10m)
    {
        _earnings.Add(new EarningInput
        {
            Code = "OVERTIME", Name = "Overtime", Amount = new Money(amount, _currency),
            Quantity = hours, IsTaxable = true, IsNssaApplicable = false, IsIncludedInGross = true,
            IsOvertime = true, TreatmentVerification = VerificationStatus.Verified
        });
        return this;
    }

    public SnapshotBuilder Bonus(decimal amount, bool exemptUpToLimit = true)
    {
        _earnings.Add(new EarningInput
        {
            Code = "BONUS_ANNUAL", Name = "Annual Bonus", Amount = new Money(amount, _currency),
            IsTaxable = true, IsNssaApplicable = false, IsIncludedInGross = true, IsBonus = true,
            IsExemptUpToLimit = exemptUpToLimit, TreatmentVerification = VerificationStatus.Verified
        });
        return this;
    }

    public SnapshotBuilder Reimbursement(decimal amount)
    {
        _earnings.Add(new EarningInput
        {
            Code = "REIMB", Name = "Expense Reimbursement", Amount = new Money(amount, _currency),
            IsTaxable = false, IsNssaApplicable = false, IsIncludedInGross = false,
            IsEmployerLevyBase = false, IsReimbursive = true,
            TreatmentVerification = VerificationStatus.Verified
        });
        return this;
    }

    public SnapshotBuilder Deduction(string code, string name, decimal amount,
        bool reducesTaxable = false, int priority = 100)
    {
        _deductions.Add(new DeductionInput
        {
            Code = code, Name = name, Amount = new Money(amount, _currency),
            ReducesTaxableIncome = reducesTaxable, AppliesBeforeTax = reducesTaxable,
            Priority = priority, TreatmentVerification = VerificationStatus.Verified
        });
        return this;
    }

    public SnapshotBuilder AllocateTo(string projectName, decimal percent, Guid? projectId = null)
    {
        _allocations.Add(new CostAllocationInput(
            projectId ?? Guid.NewGuid(), projectName, null, null, null, percent));
        return this;
    }

    public PayrollInputSnapshot Build() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        EmployeeNumber = "EMP-0031",
        EmployeeName = "John Moyo",
        PayrollPeriodId = Guid.NewGuid(),
        PeriodStart = new DateOnly(2026, 9, 1),
        PeriodEnd = new DateOnly(2026, 9, 30),
        PayDate = new DateOnly(2026, 9, 30),
        PeriodBasis = _periodBasis,
        Mode = _mode,
        ContractId = Guid.NewGuid(),
        ContractVersion = 1,
        PayrollCurrency = _currency,
        EarningsBasis = EarningsBasis.MonthlySalary,
        PaymentFrequency = PaymentFrequency.Monthly,
        EmploymentTypeCode = _employmentType,
        Earnings = _earnings,
        Deductions = _deductions,
        Timesheet = _timesheet,
        StatutoryProfile = _profile,
        CostAllocations = _allocations,
        BonusExemptionUsedYearToDate = _bonusExemptionUsed,
        Rules = _rules ?? SeedRules.For(_currency, _periodBasis, _employmentType)
    };
}
