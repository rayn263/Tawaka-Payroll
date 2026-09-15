using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Loans;
using Tawaka.Application.Security;
using Tawaka.Application.Statutory.Obligations;
using Tawaka.Application.Time;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Payroll.Engine;
using Tawaka.Payroll.Engine.Results;

namespace Tawaka.Application.Payroll;

/// <summary>
/// Creates payroll runs, calculates them through the engine, and moves them through the workflow.
/// <para>
/// The user interface never calculates anything: it calls this, which calls the engine. Every
/// figure on every screen comes from a <see cref="PayrollResult"/> produced by
/// <see cref="PayrollCalculator"/>.
/// </para>
/// </summary>
public sealed class PayrollRunService
{
    private readonly IPayrollDataContext _context;
    private readonly PayrollSnapshotBuilder _snapshots;
    private readonly PayrollSnapshotStore _snapshotStore;
    private readonly StatutoryObligationService _obligations;
    private readonly TimesheetService _timesheets;
    private readonly LoanService _loans;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly PayrollCalculator _calculator = new();

    public PayrollRunService(
        IPayrollDataContext context, PayrollSnapshotBuilder snapshots,
        PayrollSnapshotStore snapshotStore, StatutoryObligationService obligations,
        TimesheetService timesheets, LoanService loans, ICurrentUser currentUser, IClock clock)
    {
        _context = context;
        _snapshots = snapshots;
        _snapshotStore = snapshotStore;
        _obligations = obligations;
        _timesheets = timesheets;
        _loans = loans;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<OperationResult<PayrollRun>> CreateRunAsync(
        Guid payrollPeriodId, PayrollRunType runType = PayrollRunType.Normal,
        CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.PayrollCreate);

        var validation = ValidationResult.Success();
        var period = await _context.PayrollPeriods
            .FirstOrDefaultAsync(p => p.Id == payrollPeriodId, cancellationToken)
            .ConfigureAwait(false);

        if (period is null)
        {
            return OperationResult<PayrollRun>.Failed(validation.Add("Period", "Payroll period not found."));
        }

        validation.AddIf(period.IsLocked, "Period", "This payroll period is locked.");
        if (!validation.IsValid)
        {
            return OperationResult<PayrollRun>.Failed(validation);
        }

        var runNumber = await _context.PayrollRuns
            .Where(r => r.PayrollPeriodId == payrollPeriodId)
            .CountAsync(cancellationToken).ConfigureAwait(false) + 1;

        var run = new PayrollRun
        {
            CompanyId = period.CompanyId,
            PayrollPeriodId = payrollPeriodId,
            RunNumber = runNumber,
            RunType = runType,
            Status = PayrollRunStatus.Draft,
            Mode = period.Mode
        };

        _context.PayrollRuns.Add(run);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<PayrollRun>.Success(run);
    }

    /// <summary>
    /// Calculates every eligible employee through the engine and stores the results, lines and
    /// traces. Recalculating replaces the previous results; an approved run is frozen and must be
    /// reopened first.
    /// </summary>
    public async Task<OperationResult<PayrollRun>> CalculateAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.PayrollCalculate);

        var validation = ValidationResult.Success();
        var run = await _context.PayrollRuns
            .Include(r => r.PayrollPeriod)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            .ConfigureAwait(false);

        if (run is null)
        {
            return OperationResult<PayrollRun>.Failed(validation.Add("Run", "Payroll run not found."));
        }

        if (!run.CanCalculate)
        {
            return OperationResult<PayrollRun>.Failed(validation.Add("Run",
                $"A run at status {run.Status} cannot be recalculated. Approved figures are frozen; " +
                "reopen the run first."));
        }

        var period = run.PayrollPeriod!;
        run.Status = PayrollRunStatus.Calculating;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await ClearPreviousResultsAsync(runId, cancellationToken).ConfigureAwait(false);

        var employees = await _context.Employees.AsNoTracking()
            .Where(e => e.CompanyId == run.CompanyId && e.Status == EmployeeStatus.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ruleSnapshot = new Dictionary<string, string>();

        foreach (var employee in employees)
        {
            var snapshot = await _snapshots
                .BuildAsync(employee.Id, period, run.Mode, cancellationToken)
                .ConfigureAwait(false);

            if (snapshot is null)
            {
                // No contract applied on the pay date: recorded as excluded, not silently skipped.
                _context.PayrollRunEmployees.Add(new PayrollRunEmployee
                {
                    PayrollRunId = runId,
                    EmployeeId = employee.Id,
                    EmployeeNumber = employee.EmployeeNumber,
                    EmployeeName = employee.FullName,
                    CurrencyCode = "USD",
                    IsExcluded = true,
                    IsCalculated = false,
                    ExclusionReason = "No contract was in force on the pay date."
                });
                continue;
            }

            var result = _calculator.Calculate(snapshot);
            var runEmployee = Persist(runId, result);

            // The snapshot is sealed the moment it is calculated. Approved time, leave and loan
            // balances all move on; without this a recalculation years later would silently use
            // today's inputs and produce a different, equally defensible figure (ADR-035).
            _snapshotStore.Capture(runId, runEmployee.Id, snapshot, PayrollCalculator.Version);

            foreach (var (key, value) in result.RuleSnapshot)
            {
                ruleSnapshot[key] = value;
            }
        }

        run.Status = PayrollRunStatus.Review;
        run.CalculatedBy = _currentUser.UserId;
        run.CalculatedAt = _clock.Now;
        run.EngineVersion = PayrollCalculator.Version;
        run.RuleSnapshot = JsonSerializer.Serialize(ruleSnapshot);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<PayrollRun>.Success(run);
    }

    /// <summary>
    /// Approves a run. Refused while any employee's figures are unresolved: approving a payroll
    /// that the engine could not fully calculate would approve figures nobody has.
    /// </summary>
    public async Task<ValidationResult> ApproveAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.PayrollApprove);

        var validation = ValidationResult.Success();
        var run = await _context.PayrollRuns
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return validation.Add("Run", "Payroll run not found.");
        }

        validation.AddIf(run.Status != PayrollRunStatus.Review, "Run",
            $"Only a run awaiting review can be approved. This run is {run.Status}.");

        // Segregation of duties: whoever calculated it cannot also approve it.
        validation.AddIf(
            run.CalculatedBy is not null && run.CalculatedBy == _currentUser.UserId, "Run",
            "Segregation of duties: the user who calculated a payroll run cannot approve it.");

        var unresolved = await _context.PayrollUnresolvedItems
            .Where(u => _context.PayrollRunEmployees
                .Any(e => e.Id == u.PayrollRunEmployeeId && e.PayrollRunId == runId))
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        validation.AddIf(unresolved > 0, "Run",
            $"{unresolved} figures could not be calculated because statutory rules are missing or " +
            "unverified. Resolve them before approving.");

        validation.AddIf(run.Mode == PayrollMode.Development, "Run",
            "This is a development calculation and cannot be approved. Live payroll requires every " +
            "required statutory rule to be verified.");

        if (!validation.IsValid)
        {
            return validation;
        }

        run.Status = PayrollRunStatus.Approved;
        run.ApprovedBy = _currentUser.UserId;
        run.ApprovedAt = _clock.Now;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// Finalises an approved run. This is the point at which the money is treated as withheld, so
    /// it creates the statutory obligations with Calculated and Deducted set — and nothing more.
    /// Approval of a payroll never marks an obligation paid.
    /// </summary>
    public async Task<ValidationResult> FinaliseAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.PayrollFinalise);

        var validation = ValidationResult.Success();
        var run = await _context.PayrollRuns
            .Include(r => r.PayrollPeriod)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return validation.Add("Run", "Payroll run not found.");
        }

        validation.AddIf(run.Status != PayrollRunStatus.Approved, "Run",
            $"Only an approved run can be finalised. This run is {run.Status}.");

        if (!validation.IsValid)
        {
            return validation;
        }

        run.Status = PayrollRunStatus.Finalised;
        run.FinalisedBy = _currentUser.UserId;
        run.FinalisedAt = _clock.Now;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _obligations.CreateForRunAsync(runId, cancellationToken).ConfigureAwait(false);
        await LockConsumedInputsAsync(run, cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// Freezes the inputs this run consumed and posts the loan repayments it actually deducted.
    /// <para>
    /// Both happen at finalisation rather than at calculation, because a calculated run can still
    /// be recalculated or abandoned. Moving a loan balance for a run that is later thrown away
    /// would leave the employee's loan wrong with nothing to point at.
    /// </para>
    /// </summary>
    private async Task LockConsumedInputsAsync(PayrollRun run, CancellationToken cancellationToken)
    {
        var sources = await _context.PayrollRunInputSources.AsNoTracking()
            .Where(s => s.PayrollRunId == run.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var timesheetIds = sources
            .Where(s => s.InputType == "Timesheet")
            .Select(s => s.InputId)
            .Distinct()
            .ToList();

        await _timesheets.LockForRunAsync(timesheetIds, run.Id, cancellationToken)
            .ConfigureAwait(false);

        var loanLines = await _context.PayrollDeductionLines.AsNoTracking()
            .Where(l => _context.PayrollRunEmployees
                .Any(e => e.Id == l.PayrollRunEmployeeId && e.PayrollRunId == run.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var snapshots = await _context.PayrollInputSnapshots.AsNoTracking()
            .Where(s => s.PayrollRunId == run.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var repayments =
            new List<(Guid LoanId, Guid? InstalmentId, decimal Amount, string CurrencyCode)>();

        foreach (var record in snapshots)
        {
            var snapshot = await _snapshotStore
                .ReadAsync(record.PayrollRunEmployeeId, cancellationToken).ConfigureAwait(false);

            if (snapshot is null)
            {
                continue;
            }

            // Taken from the sealed snapshot, so what is posted to the loan is exactly what the
            // calculation deducted — not what the schedule would say today.
            repayments.AddRange(snapshot.LoanDeductions.Select(loan =>
                (loan.LoanId, loan.InstalmentId, loan.Amount.Amount, loan.Amount.Currency.Value)));
        }

        var payDate = run.PayrollPeriod?.PayDate ?? DateOnly.FromDateTime(_clock.Now.Date);
        await _loans.RecordPayrollRepaymentsAsync(
            run.Id, run.PayrollPeriodId, repayments, payDate, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Records that employees were paid their net wages. This says nothing about the statutory
    /// authorities: those are settled separately, with their own evidence.
    /// </summary>
    public async Task<ValidationResult> MarkNetWagesPaidAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.PayrollRecordPayment);

        var validation = ValidationResult.Success();
        var run = await _context.PayrollRuns
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return validation.Add("Run", "Payroll run not found.");
        }

        validation.AddIf(run.Status != PayrollRunStatus.Finalised, "Run",
            $"Only a finalised run can be marked paid. This run is {run.Status}.");

        if (!validation.IsValid)
        {
            return validation;
        }

        run.Status = PayrollRunStatus.Paid;
        run.PaidBy = _currentUser.UserId;
        run.PaidAt = _clock.Now;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>Locks a run. From here the figures are immutable at the data layer.</summary>
    public async Task<ValidationResult> LockAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.PayrollLock);

        var validation = ValidationResult.Success();
        var run = await _context.PayrollRuns
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken).ConfigureAwait(false);

        if (run is null)
        {
            return validation.Add("Run", "Payroll run not found.");
        }

        validation.AddIf(run.Status is not (PayrollRunStatus.Finalised or PayrollRunStatus.Paid),
            "Run", $"Only a finalised or paid run can be locked. This run is {run.Status}.");

        if (!validation.IsValid)
        {
            return validation;
        }

        run.Status = PayrollRunStatus.Locked;
        run.LockedBy = _currentUser.UserId;
        run.LockedAt = _clock.Now;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    public Task<List<PayrollRun>> GetRunsAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.PayrollView);

        return _context.PayrollRuns.AsNoTracking()
            .Include(r => r.PayrollPeriod)
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task ClearPreviousResultsAsync(Guid runId, CancellationToken cancellationToken)
    {
        var existing = await _context.PayrollRunEmployees
            .Where(e => e.PayrollRunId == runId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing.Count == 0)
        {
            return;
        }

        _context.PayrollRunEmployees.RemoveRange(existing);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes an engine result to the run, including every trace entry.</summary>
    private PayrollRunEmployee Persist(Guid runId, PayrollResult result)
    {
        var currency = result.Currency.Value;

        var runEmployee = new PayrollRunEmployee
        {
            PayrollRunId = runId,
            EmployeeId = result.EmployeeId,
            EmployeeContractId = result.ContractId,
            ContractVersion = result.ContractVersion,
            EmployeeNumber = result.EmployeeNumber,
            EmployeeName = result.EmployeeName,
            CurrencyCode = currency,
            IsCalculated = result.Status == PayrollResultStatus.Calculated,
            GrossEarningsAmount = result.GrossEarnings?.Amount,
            TaxableIncomeAmount = result.TaxableIncome?.Amount,
            NssaInsurableEarningsAmount = result.NssaInsurableEarnings?.Amount,
            NssaEmployeeAmount = result.NssaEmployee?.Amount,
            PayeBeforeCreditsAmount = result.PayeBeforeCredits?.Amount,
            TaxCreditsAmount = result.TaxCredits?.Amount,
            PayeAfterCreditsAmount = result.PayeAfterCredits?.Amount,
            AidsLevyAmount = result.AidsLevy?.Amount,
            TotalStatutoryDeductionsAmount = result.TotalStatutoryDeductions?.Amount,
            TotalOtherDeductionsAmount = result.TotalOtherDeductions?.Amount,
            TotalDeductionsAmount = result.TotalDeductions?.Amount,
            NetPayAmount = result.NetPay?.Amount,
            NssaEmployerAmount = result.NssaEmployer?.Amount,
            ApwcsAmount = result.Apwcs?.Amount,
            TotalEmployerCostAmount = result.TotalEmployerCost?.Amount
        };

        var order = 0;
        foreach (var line in result.Earnings)
        {
            runEmployee.EarningLines.Add(new PayrollEarningLine
            {
                Code = line.Code,
                Name = line.Name,
                Amount = line.Amount.Amount,
                CurrencyCode = line.Amount.Currency.Value,
                Quantity = line.Quantity,
                RateAmount = line.Rate?.Amount,
                TaxableAmount = line.TaxableAmount.Amount,
                ExemptAmount = line.ExemptAmount.Amount,
                NssaApplicableAmount = line.NssaApplicableAmount.Amount,
                IsIncludedInGross = line.IsIncludedInGross,
                DisplayOrder = order++,
                OriginalAmount = line.Conversion?.OriginalAmount,
                OriginalCurrencyCode = line.Conversion?.OriginalCurrency,
                ExchangeRateUsed = line.Conversion?.Rate,
                ExchangeRateDate = line.Conversion?.RateDate,
                ExchangeRateSource = line.Conversion?.RateSource
            });
        }

        order = 0;
        foreach (var line in result.Deductions)
        {
            runEmployee.DeductionLines.Add(new PayrollDeductionLine
            {
                Code = line.Code,
                Name = line.Name,
                Amount = line.Amount.Amount,
                CurrencyCode = line.Amount.Currency.Value,
                IsStatutory = line.IsStatutory,
                ReducesTaxableIncome = line.ReducesTaxableIncome,
                DisplayOrder = order++
            });
        }

        order = 0;
        foreach (var cost in result.EmployerCosts)
        {
            runEmployee.EmployerCostLines.Add(new PayrollEmployerCostLine
            {
                Code = cost.Code,
                Name = cost.Name,
                Amount = cost.Amount.Amount,
                CurrencyCode = cost.Amount.Currency.Value,
                BaseAmount = cost.BaseAmount.Amount,
                RateApplied = cost.RateApplied,
                RuleId = cost.RuleId,
                DisplayOrder = order++
            });
        }

        foreach (var entry in result.Trace.Entries)
        {
            runEmployee.TraceEntries.Add(new PayrollCalculationTraceEntry
            {
                Sequence = entry.Sequence,
                Stage = entry.Stage,
                ItemKey = entry.ItemKey,
                RuleId = entry.RuleId,
                RuleType = entry.RuleType?.ToString(),
                VerificationStatus = entry.VerificationStatus?.ToString(),
                RuleSource = entry.RuleSource,
                RuleEffectiveFrom = entry.RuleEffectiveFrom,
                Inputs = entry.Inputs.Count == 0 ? null : JsonSerializer.Serialize(entry.Inputs),
                Steps = entry.Steps.Count == 0
                    ? null
                    : JsonSerializer.Serialize(entry.Steps.Select(s => s.ToString())),
                RawValue = entry.RawValue,
                OutputAmount = entry.Output?.Amount,
                OutputCurrency = entry.Output?.Currency.Value,
                RoundingApplied = entry.RoundingApplied,
                Explanation = entry.Explanation,
                Conversion = entry.Conversion is null
                    ? null
                    : JsonSerializer.Serialize(entry.Conversion)
            });
        }

        foreach (var unresolved in result.Unresolved)
        {
            runEmployee.UnresolvedItems.Add(new PayrollUnresolvedItem
            {
                Code = unresolved.Code,
                ItemKey = unresolved.ItemKey,
                Message = unresolved.Message,
                RuleType = unresolved.RuleType?.ToString(),
                RuleId = unresolved.RuleId,
                VerificationStatus = unresolved.VerificationStatus?.ToString(),
                ComplianceQuestion = unresolved.ComplianceQuestion,
                Remedy = unresolved.Remedy
            });
        }

        foreach (var allocation in result.CostAllocations)
        {
            runEmployee.CostAllocations.Add(new PayrollCostAllocation
            {
                ProjectId = allocation.ProjectId,
                ProjectName = allocation.ProjectName,
                DepartmentId = allocation.DepartmentId,
                DepartmentName = allocation.DepartmentName,
                Percent = allocation.Percent,
                AllocatedCostAmount = allocation.AllocatedCost.Amount,
                CurrencyCode = allocation.AllocatedCost.Currency.Value
            });
        }

        _context.PayrollRunEmployees.Add(runEmployee);
        return runEmployee;
    }
}
