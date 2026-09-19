using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Security;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Security;

namespace Tawaka.Application.Employees;

/// <summary>
/// Effective-dated employment contracts.
/// <para>
/// The rule this service exists to enforce: <b>a change in terms never overwrites the previous
/// contract</b>. Superseding creates a new version, marks the old one Superseded, and links the
/// two, so payroll for any past period can be recalculated on the terms that actually applied
/// then. Without this, a mid-year increase would silently rewrite January's payroll.
/// </para>
/// </summary>
public sealed class EmployeeContractService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;

    public EmployeeContractService(IPayrollDataContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    /// <summary>Creates the first contract for an employee.</summary>
    /// <summary>
    /// Validates a first contract without writing anything.
    /// <para>
    /// A new employee and their first contract are entered on one screen but written by two
    /// services. Without this, an employee whose contract fails validation is already saved, and
    /// the officer correcting the mistake is told the employee number is already in use — leaving
    /// a person on the payroll with no terms of employment and no way to finish the record.
    /// Validating the contract first means nothing is written unless both halves are sound.
    /// </para>
    /// </summary>
    public async Task<ValidationResult> ValidateInitialAsync(
        EmployeeContract contract, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.EmployeesEditSalary);

        var validation = EmployeeValidation.ValidateContract(contract);

        var existing = await _context.EmployeeContracts
            .AnyAsync(c => c.EmployeeId == contract.EmployeeId, cancellationToken)
            .ConfigureAwait(false);
        validation.AddIf(existing, nameof(contract.EmployeeId),
            "This employee already has a contract. Use a supersede to change the terms.");

        await ValidateReferencesAsync(contract, validation, cancellationToken).ConfigureAwait(false);

        return validation;
    }

    public async Task<OperationResult<EmployeeContract>> CreateInitialAsync(
        EmployeeContract contract, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.EmployeesEditSalary);

        var validation = await ValidateInitialAsync(contract, cancellationToken).ConfigureAwait(false);

        if (!validation.IsValid)
        {
            return OperationResult<EmployeeContract>.Failed(validation);
        }

        contract.VersionNumber = 1;
        contract.Status = ContractStatus.Active;
        contract.IsCurrent = true;

        _context.EmployeeContracts.Add(contract);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return OperationResult<EmployeeContract>.Success(contract);
    }

    /// <summary>
    /// Supersedes the current contract with a new version. The caller supplies the new terms and a
    /// reason; the previous version is retained untouched apart from being closed off.
    /// </summary>
    public async Task<OperationResult<EmployeeContract>> SupersedeAsync(
        Guid employeeId,
        EmployeeContract newTerms,
        DateOnly effectiveFrom,
        string changeReason,
        CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.EmployeesEditSalary);

        var validation = ValidationResult.Success();
        validation.Require(changeReason, nameof(changeReason),
            "A reason is required when contract terms change.");

        var current = await _context.EmployeeContracts
            .FirstOrDefaultAsync(c => c.EmployeeId == employeeId && c.IsCurrent, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            return OperationResult<EmployeeContract>.Failed(
                validation.Add("Contract", "This employee has no current contract to supersede."));
        }

        validation.AddIf(effectiveFrom <= current.StartDate, nameof(effectiveFrom),
            "The new contract must start after the contract it supersedes " +
            $"(which started on {current.StartDate:dd MMM yyyy}).");

        var successor = new EmployeeContract
        {
            CompanyId = current.CompanyId,
            EmployeeId = employeeId,
            VersionNumber = current.VersionNumber + 1,
            ContractReference = newTerms.ContractReference ?? current.ContractReference,
            EmploymentTypeId = newTerms.EmploymentTypeId == Guid.Empty
                ? current.EmploymentTypeId
                : newTerms.EmploymentTypeId,
            JobTitleId = newTerms.JobTitleId ?? current.JobTitleId,
            DepartmentId = newTerms.DepartmentId ?? current.DepartmentId,
            LocationId = newTerms.LocationId ?? current.LocationId,
            ProjectId = newTerms.ProjectId ?? current.ProjectId,
            ProjectSiteId = newTerms.ProjectSiteId ?? current.ProjectSiteId,
            StartDate = effectiveFrom,
            EndDate = newTerms.EndDate,
            PayrollCurrency = string.IsNullOrWhiteSpace(newTerms.PayrollCurrency)
                ? current.PayrollCurrency
                : newTerms.PayrollCurrency,
            PaymentFrequency = newTerms.PaymentFrequency,
            EarningsBasis = newTerms.EarningsBasis,
            BasicSalary = newTerms.BasicSalary,
            HourlyRate = newTerms.HourlyRate,
            DailyRate = newTerms.DailyRate,
            WeeklyRate = newTerms.WeeklyRate,
            MonthlyRate = newTerms.MonthlyRate,
            ProjectRate = newTerms.ProjectRate,
            CommissionStructure = newTerms.CommissionStructure ?? current.CommissionStructure,
            StandardHoursPerDay = newTerms.StandardHoursPerDay,
            StandardDaysPerWeek = newTerms.StandardDaysPerWeek,
            Status = ContractStatus.Active,
            IsCurrent = true,
            PreviousContractId = current.Id,
            ChangeReason = changeReason,
            Notes = newTerms.Notes
        };

        foreach (var error in EmployeeValidation.ValidateContract(successor).Errors)
        {
            validation.Add(error.Field, error.Message);
        }

        await ValidateReferencesAsync(successor, validation, cancellationToken).ConfigureAwait(false);

        if (!validation.IsValid)
        {
            return OperationResult<EmployeeContract>.Failed(validation);
        }

        // Close the outgoing version the day before the new one starts. It is never edited beyond
        // this, and never deleted.
        current.IsCurrent = false;
        current.Status = ContractStatus.Superseded;
        current.EndDate ??= effectiveFrom.AddDays(-1);

        _context.EmployeeContracts.Add(successor);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        current.SupersededByContractId = successor.Id;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return OperationResult<EmployeeContract>.Success(successor);
    }

    /// <summary>
    /// The contract that applied on a given date. This is what payroll must use — not the current
    /// contract — so that reprocessing a past period reproduces the original figures.
    /// </summary>
    public Task<EmployeeContract?> GetContractOnAsync(
        Guid employeeId, DateOnly date, CancellationToken cancellationToken = default) =>
        _context.EmployeeContracts
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId &&
                        c.StartDate <= date &&
                        (c.EndDate == null || c.EndDate >= date))
            .OrderByDescending(c => c.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<EmployeeContract>> GetHistoryAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.EmployeesView);

        return _context.EmployeeContracts
            .AsNoTracking()
            .Where(c => c.EmployeeId == employeeId)
            .OrderByDescending(c => c.VersionNumber)
            .ToListAsync(cancellationToken);
    }

    private async Task ValidateReferencesAsync(
        EmployeeContract contract, ValidationResult validation, CancellationToken cancellationToken)
    {
        var employmentType = await _context.EmploymentTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == contract.EmploymentTypeId, cancellationToken)
            .ConfigureAwait(false);

        if (employmentType is null)
        {
            validation.Add(nameof(contract.EmploymentTypeId), "Employment type not found.");
            return;
        }

        validation.AddIf(
            employmentType.RequiresContractEndDate && contract.EndDate is null,
            nameof(contract.EndDate),
            $"A contract end date is required for {employmentType.Name} employment.");

        var currencyExists = await _context.Currencies
            .AsNoTracking()
            .AnyAsync(c => c.Code == contract.PayrollCurrency && c.IsActive, cancellationToken)
            .ConfigureAwait(false);
        validation.AddIf(!currencyExists, nameof(contract.PayrollCurrency),
            $"'{contract.PayrollCurrency}' is not an active currency.");

        if (contract.ProjectId.HasValue)
        {
            var projectExists = await _context.Projects
                .AsNoTracking()
                .AnyAsync(p => p.Id == contract.ProjectId.Value, cancellationToken)
                .ConfigureAwait(false);
            validation.AddIf(!projectExists, nameof(contract.ProjectId), "Project not found.");
        }
    }
}
