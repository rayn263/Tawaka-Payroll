using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Statutory;
using Tawaka.Domain.Common;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Statutory;
using Tawaka.Payroll.Engine.Inputs;
using Tawaka.Payroll.Engine.Results;

namespace Tawaka.Application.Payroll;

/// <summary>
/// Assembles the immutable <see cref="PayrollInputSnapshot"/> the engine calculates from.
/// <para>
/// This is the only place that reads the database for a calculation. It resolves the contract that
/// applied on the pay date — not the current one — and the statutory rules effective then, so a
/// past period recalculates to the same figures. Rules that fail to resolve are carried into the
/// snapshot as unresolved items rather than dropped, so the engine can refuse with a reason.
/// </para>
/// </summary>
public sealed class PayrollSnapshotBuilder
{
    private readonly IPayrollDataContext _context;
    private readonly IStatutoryRuleResolver _resolver;

    public PayrollSnapshotBuilder(IPayrollDataContext context, IStatutoryRuleResolver resolver)
    {
        _context = context;
        _resolver = resolver;
    }

    public async Task<PayrollInputSnapshot?> BuildAsync(
        Guid employeeId, PayrollPeriod period, PayrollMode mode,
        CancellationToken cancellationToken = default)
    {
        var employee = await _context.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null)
        {
            return null;
        }

        // The contract that applied on the pay date, which may be an earlier version.
        var contract = await _context.EmployeeContracts.AsNoTracking()
            .Where(c => c.EmployeeId == employeeId &&
                        c.StartDate <= period.PayDate &&
                        (c.EndDate == null || c.EndDate >= period.PayDate))
            .OrderByDescending(c => c.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (contract is null)
        {
            return null;
        }

        var employmentType = await _context.EmploymentTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == contract.EmploymentTypeId, cancellationToken)
            .ConfigureAwait(false);

        var profile = await _context.EmployeeStatutoryProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId, cancellationToken)
            .ConfigureAwait(false);

        var currency = new CurrencyCode(contract.PayrollCurrency);
        var earnings = await BuildEarningsAsync(employee, contract, period, currency, cancellationToken)
            .ConfigureAwait(false);
        var deductions = await BuildDeductionsAsync(employeeId, period, cancellationToken)
            .ConfigureAwait(false);
        var allocations = await BuildAllocationsAsync(employeeId, contract, period, cancellationToken)
            .ConfigureAwait(false);

        var rules = ResolveRules(contract, employmentType, period, currency, mode);

        return new PayrollInputSnapshot
        {
            CompanyId = employee.CompanyId,
            EmployeeId = employee.Id,
            EmployeeNumber = employee.EmployeeNumber,
            EmployeeName = employee.FullName,
            PayrollPeriodId = period.Id,
            PeriodStart = period.StartDate,
            PeriodEnd = period.EndDate,
            PayDate = period.PayDate,
            PeriodBasis = period.Frequency,
            Mode = mode,
            ContractId = contract.Id,
            ContractVersion = contract.VersionNumber,
            PayrollCurrency = currency,
            EarningsBasis = contract.EarningsBasis,
            PaymentFrequency = contract.PaymentFrequency,
            EmploymentTypeCode = employmentType?.Code ?? string.Empty,
            ContractRate = contract.PrimaryRateMoney,
            StandardHoursPerDay = contract.StandardHoursPerDay,
            StandardDaysPerWeek = contract.StandardDaysPerWeek,
            Earnings = earnings,
            Deductions = deductions,
            StatutoryProfile = new StatutoryProfileInput
            {
                TaxNumber = profile?.TaxNumber,
                NssaNumber = profile?.NssaNumber,
                IsPayeExempt = profile?.IsPayeExempt ?? false,
                NssaEligibilityOverride = profile?.NssaEligibilityOverride,
                Age = employee.AgeAt(period.PayDate),
                IsElderlyCreditEligible = profile?.IsElderlyCreditEligible ?? false,
                IsDisabledCreditEligible = profile?.IsDisabledCreditEligible ?? false,
                IsBlindCreditEligible = profile?.IsBlindCreditEligible ?? false,
                MedicalAidCreditApplies = profile?.MedicalAidCreditApplies ?? false,
                IsNecMember = profile?.IsNecMember ?? false
            },
            CostAllocations = allocations,
            Rules = rules
        };
    }

    /// <summary>
    /// Basic pay from the contract, plus the recurring earnings effective in the period. Each
    /// carries its earning type's statutory treatment and verification grade.
    /// </summary>
    private async Task<List<EarningInput>> BuildEarningsAsync(
        Employee employee, EmployeeContract contract, PayrollPeriod period, CurrencyCode currency,
        CancellationToken cancellationToken)
    {
        var earnings = new List<EarningInput>();

        var basicType = await _context.EarningTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CompanyId == employee.CompanyId && t.Code == "BASIC",
                cancellationToken)
            .ConfigureAwait(false);

        if (contract.PrimaryRate is { } rate)
        {
            earnings.Add(new EarningInput
            {
                Code = "BASIC",
                Name = basicType?.Name ?? "Basic Salary",
                Amount = new Money(rate, currency),
                IsBasic = true,
                IsTaxable = basicType?.IsTaxable ?? true,
                IsNssaApplicable = basicType?.IsNssaApplicable ?? true,
                IsIncludedInGross = basicType?.IsIncludedInGross ?? true,
                IsEmployerLevyBase = basicType?.IsEmployerLevyBase ?? true,
                TreatmentVerification = basicType?.TreatmentVerificationStatus
                                        ?? VerificationStatus.Unverified,
                Source = basicType?.TreatmentSource
            });
        }

        var recurring = await _context.EmployeeRecurringEarnings.AsNoTracking()
            .Include(e => e.EarningType)
            .Where(e => e.EmployeeId == employee.Id && e.IsActive &&
                        e.EffectiveFrom <= period.PayDate &&
                        (e.EffectiveTo == null || e.EffectiveTo >= period.PayDate))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var item in recurring)
        {
            var type = item.EarningType;
            earnings.Add(new EarningInput
            {
                Code = type?.Code ?? "OTHER",
                Name = type?.Name ?? "Other earning",
                Amount = item.AsMoney(),
                IsTaxable = type?.IsTaxable ?? true,
                IsNssaApplicable = type?.IsNssaApplicable ?? false,
                IsIncludedInGross = type?.IsIncludedInGross ?? true,
                IsEmployerLevyBase = type?.IsEmployerLevyBase ?? true,
                IsExemptUpToLimit = type?.IsExemptUpToLimit ?? false,
                IsReimbursive = type?.IsReimbursive ?? false,
                IsBonus = type?.Category == Domain.Earnings.EarningCategory.Bonus,
                IsOvertime = type?.Category == Domain.Earnings.EarningCategory.Overtime,
                TreatmentVerification = type?.TreatmentVerificationStatus
                                        ?? VerificationStatus.Unverified,
                Source = type?.TreatmentSource
            });
        }

        return earnings;
    }

    private async Task<List<DeductionInput>> BuildDeductionsAsync(
        Guid employeeId, PayrollPeriod period, CancellationToken cancellationToken)
    {
        var recurring = await _context.EmployeeRecurringDeductions.AsNoTracking()
            .Include(d => d.DeductionType)
            .Where(d => d.EmployeeId == employeeId && d.IsActive &&
                        d.EffectiveFrom <= period.PayDate &&
                        (d.EffectiveTo == null || d.EffectiveTo >= period.PayDate))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return recurring.Select(d => new DeductionInput
        {
            Code = d.DeductionType?.Code ?? "OTHER",
            Name = d.DeductionType?.Name ?? "Other deduction",
            Amount = d.AsMoney(),
            ReducesTaxableIncome = d.DeductionType?.ReducesTaxableIncome ?? false,
            AppliesBeforeTax = d.DeductionType?.AppliesBeforeTax ?? false,
            Priority = d.Priority,
            TreatmentVerification = d.DeductionType?.TreatmentVerificationStatus
                                    ?? VerificationStatus.Unverified
        }).ToList();
    }

    private async Task<List<CostAllocationInput>> BuildAllocationsAsync(
        Guid employeeId, EmployeeContract contract, PayrollPeriod period,
        CancellationToken cancellationToken)
    {
        var assignments = await _context.EmployeeProjectAssignments.AsNoTracking()
            .Include(a => a.Project)
            .Where(a => a.EmployeeId == employeeId && a.IsActive &&
                        a.StartDate <= period.PayDate &&
                        (a.EndDate == null || a.EndDate >= period.PayDate))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (assignments.Count > 0)
        {
            return assignments.Select(a => new CostAllocationInput(
                a.ProjectId, a.Project?.Name, a.ProjectSiteId, contract.DepartmentId, null,
                a.AllocationPercent)).ToList();
        }

        // With no explicit assignment, cost follows the contract's project or department in full.
        if (contract.ProjectId is not null || contract.DepartmentId is not null)
        {
            var project = contract.ProjectId is null
                ? null
                : await _context.Projects.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == contract.ProjectId, cancellationToken)
                    .ConfigureAwait(false);

            return new List<CostAllocationInput>
            {
                new(contract.ProjectId, project?.Name, contract.ProjectSiteId,
                    contract.DepartmentId, null, 100m)
            };
        }

        return new List<CostAllocationInput>();
    }

    /// <summary>
    /// Resolves every rule the calculation needs. A failure is recorded, never defaulted: the
    /// engine then refuses that figure and names the rule.
    /// </summary>
    private ResolvedRules ResolveRules(
        EmployeeContract contract, EmploymentType? employmentType, PayrollPeriod period,
        CurrencyCode currency, PayrollMode mode)
    {
        var unresolved = new List<UnresolvedItem>();

        TRule? Resolve<TRule>(StatutoryRuleType type, PeriodBasis? basis = null,
            string? discriminator = null) where TRule : StatutoryRule
        {
            var resolution = _resolver.Resolve<TRule>(new StatutoryRuleQuery
            {
                RuleType = type,
                EffectiveDate = period.PayDate,
                Currency = RequiresCurrency(type) ? currency : null,
                PeriodBasis = basis,
                Mode = mode,
                Discriminator = discriminator,
                Employee = new EmployeeRuleContext
                {
                    EmploymentTypeCode = employmentType?.Code,
                    IndustryCode = null
                }
            });

            if (resolution.Succeeded)
            {
                return resolution.Rule;
            }

            var failure = resolution.Failure!;
            unresolved.Add(new UnresolvedItem(
                CodeFor(type), type.ToString(), failure.Message, type,
                failure.RuleId, failure.VerificationStatus, QuestionFor(type), failure.Remedy));
            return null;
        }

        var credits = new List<TaxCreditRule>();
        foreach (var creditType in new[]
                 {
                     TaxCreditType.Elderly, TaxCreditType.Disabled, TaxCreditType.Blind
                 })
        {
            var rule = Resolve<TaxCreditRule>(StatutoryRuleType.TaxCredit,
                discriminator: creditType.ToString());
            if (rule is not null)
            {
                credits.Add(rule);
            }
        }

        var levies = new List<EmployerLevyRule>();
        foreach (var levyType in new[]
                 {
                     Domain.Statutory.EmployerLevyType.Zimdef,
                     Domain.Statutory.EmployerLevyType.StandardsDevelopmentFund
                 })
        {
            var rule = Resolve<EmployerLevyRule>(StatutoryRuleType.EmployerLevy,
                discriminator: levyType.ToString());
            if (rule is { IsActive: true })
            {
                levies.Add(rule);
            }
        }

        return new ResolvedRules
        {
            PayeTable = Resolve<TaxRule>(StatutoryRuleType.PayeTable, period.Frequency),
            AidsLevy = Resolve<AidsLevyRule>(StatutoryRuleType.AidsLevy),
            Nssa = Resolve<NssaRule>(StatutoryRuleType.NssaPobs),
            NssaEligibility = Resolve<NssaEligibilityRule>(StatutoryRuleType.NssaEligibility,
                discriminator: employmentType?.Code),
            TaxCredits = credits,
            BonusExemption = Resolve<TaxExemptionRule>(StatutoryRuleType.TaxExemption,
                discriminator: TaxExemptionType.AnnualBonus.ToString()),
            Apwcs = Resolve<ApwcsRule>(StatutoryRuleType.Apwcs),
            EmployerLevies = levies,
            CurrencyStrategy = Resolve<CurrencyTaxStrategyRule>(
                StatutoryRuleType.CurrencyTaxStrategy),
            Unresolved = unresolved
        };
    }

    /// <summary>
    /// The engine's code for a failure to resolve this rule type, so a resolution failure and an
    /// in-engine refusal are reported identically.
    /// </summary>
    private static string CodeFor(StatutoryRuleType type) => type switch
    {
        StatutoryRuleType.PayeTable => UnresolvedCodes.PayeTableUnresolved,
        StatutoryRuleType.AidsLevy => UnresolvedCodes.AidsLevyRuleUnresolved,
        StatutoryRuleType.NssaPobs => UnresolvedCodes.NssaRuleUnresolved,
        StatutoryRuleType.NssaEligibility => UnresolvedCodes.NssaEligibilityUnresolved,
        StatutoryRuleType.Apwcs => UnresolvedCodes.ApwcsRuleUnresolved,
        StatutoryRuleType.EmployerLevy => UnresolvedCodes.EmployerLevyUnresolved,
        StatutoryRuleType.TaxCredit => UnresolvedCodes.TaxCreditRuleUnresolved,
        StatutoryRuleType.TaxExemption => UnresolvedCodes.ExemptionRuleUnresolved,
        StatutoryRuleType.CurrencyTaxStrategy => UnresolvedCodes.CurrencyStrategyUnresolved,
        _ => "RULE_UNRESOLVED"
    };

    /// <summary>
    /// The compliance question that must be answered to clear this rule, from the specification.
    /// Carrying it here means the payroll screen can point at the exact open question.
    /// </summary>
    private static string? QuestionFor(StatutoryRuleType type) => type switch
    {
        StatutoryRuleType.PayeTable => "Q26",
        StatutoryRuleType.AidsLevy => "Q2",
        StatutoryRuleType.NssaPobs => "Q22",
        StatutoryRuleType.NssaEligibility => "Q5",
        StatutoryRuleType.Apwcs => "Q6",
        StatutoryRuleType.TaxCredit => "Q24",
        StatutoryRuleType.TaxExemption => "Q28",
        StatutoryRuleType.CurrencyTaxStrategy => "Q1",
        _ => null
    };

    private static bool RequiresCurrency(StatutoryRuleType type) => type switch
    {
        StatutoryRuleType.PayeTable => true,
        StatutoryRuleType.NssaPobs => true,
        StatutoryRuleType.Apwcs => true,
        StatutoryRuleType.TaxCredit => true,
        StatutoryRuleType.TaxExemption => true,
        _ => false
    };
}
