using Tawaka.Domain.Common;

namespace Tawaka.Domain.Employees;

public enum ContractStatus
{
    Draft = 0,

    /// <summary>The terms currently in force.</summary>
    Active = 1,

    /// <summary>Replaced by a later version. Retained permanently.</summary>
    Superseded = 2,

    /// <summary>Ran to its end date or was terminated.</summary>
    Ended = 3
}

/// <summary>
/// Effective-dated employment terms. A salary change never overwrites anything: it supersedes the
/// current contract and creates a new version, so a payroll run for any past period can always be
/// recalculated on the terms that actually applied then (ADR-004).
/// </summary>
public class EmployeeContract : AuditableEntity
{
    public Guid CompanyId { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>Version number within this employee's contract history, starting at 1.</summary>
    public int VersionNumber { get; set; } = 1;

    public string? ContractReference { get; set; }

    public Guid EmploymentTypeId { get; set; }
    public EmploymentType? EmploymentType { get; set; }

    public Guid? JobTitleId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ProjectSiteId { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>Required for fixed-term types; null for employment without limit of time.</summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// The currency this employee is paid in. Employees on different currencies can appear in the
    /// same payroll period, and their amounts are never merged.
    /// </summary>
    public string PayrollCurrency { get; set; } = "USD";

    public PaymentFrequency PaymentFrequency { get; set; } = PaymentFrequency.Monthly;

    public EarningsBasis EarningsBasis { get; set; } = EarningsBasis.MonthlySalary;

    public decimal? BasicSalary { get; set; }
    public decimal? HourlyRate { get; set; }
    public decimal? DailyRate { get; set; }
    public decimal? WeeklyRate { get; set; }
    public decimal? MonthlyRate { get; set; }
    public decimal? ProjectRate { get; set; }

    /// <summary>Commission terms, held as configuration rather than as code.</summary>
    public string? CommissionStructure { get; set; }

    public decimal StandardHoursPerDay { get; set; } = 8m;

    public decimal StandardDaysPerWeek { get; set; } = 5m;

    public ContractStatus Status { get; set; } = ContractStatus.Draft;

    /// <summary>Exactly one contract per employee may be current at a time.</summary>
    public bool IsCurrent { get; set; }

    public Guid? PreviousContractId { get; set; }

    public Guid? SupersededByContractId { get; set; }

    /// <summary>Why this version was created, e.g. "Annual increase". Required on supersession.</summary>
    public string? ChangeReason { get; set; }

    public string? Notes { get; set; }

    public DateRange EffectivePeriod => new(StartDate, EndDate);

    public bool AppliesOn(DateOnly date) => EffectivePeriod.Contains(date);

    /// <summary>The rate that drives basic pay, given the earnings basis.</summary>
    public decimal? PrimaryRate => EarningsBasis switch
    {
        EarningsBasis.MonthlySalary => MonthlyRate ?? BasicSalary,
        EarningsBasis.WeeklySalary => WeeklyRate,
        EarningsBasis.DailyRate => DailyRate,
        EarningsBasis.HourlyRate => HourlyRate,
        EarningsBasis.ProjectRate => ProjectRate,
        EarningsBasis.ContractAmount => ProjectRate ?? BasicSalary,
        EarningsBasis.Commission => BasicSalary,
        _ => BasicSalary
    };

    public Money? PrimaryRateMoney => PrimaryRate is null
        ? null
        : new Money(PrimaryRate.Value, new CurrencyCode(PayrollCurrency));
}
