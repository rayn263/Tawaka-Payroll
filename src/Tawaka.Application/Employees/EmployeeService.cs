using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Common;
using Tawaka.Application.Security;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Security;

namespace Tawaka.Application.Employees;

/// <summary>Filters for the employee list screen.</summary>
public sealed record EmployeeFilter
{
    public string? SearchTerm { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? EmploymentTypeId { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? ProjectId { get; init; }
    public EmployeeStatus? Status { get; init; }
    public string? PayrollCurrency { get; init; }
    public PaymentFrequency? PaymentFrequency { get; init; }
}

/// <summary>A row on the employee list, flattened from the employee and their current contract.</summary>
public sealed record EmployeeListItem(
    Guid Id,
    string EmployeeNumber,
    string FullName,
    string? JobTitle,
    string? Department,
    string? EmploymentType,
    string? PayrollCurrency,
    EmployeeStatus Status,
    DateOnly HireDate,
    DateOnly? ContractEndDate);

/// <summary>
/// Employee master data. Employees are never deleted: leaving is a status change, so payroll
/// history, payslips and statutory records stay intact.
/// </summary>
public sealed class EmployeeService
{
    private readonly IPayrollDataContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public EmployeeService(IPayrollDataContext context, ICurrentUser currentUser, IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<IReadOnlyList<EmployeeListItem>> SearchAsync(
        Guid companyId, EmployeeFilter filter, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.EmployeesView);

        var query =
            from e in _context.Employees.AsNoTracking()
            where e.CompanyId == companyId
            join c in _context.EmployeeContracts.AsNoTracking().Where(c => c.IsCurrent)
                on e.Id equals c.EmployeeId into contracts
            from contract in contracts.DefaultIfEmpty()
            select new { Employee = e, Contract = contract };

        if (filter.Status.HasValue)
        {
            query = query.Where(x => x.Employee.Status == filter.Status.Value);
        }

        if (filter.DepartmentId.HasValue)
        {
            query = query.Where(x => x.Contract != null &&
                                     x.Contract.DepartmentId == filter.DepartmentId.Value);
        }

        if (filter.EmploymentTypeId.HasValue)
        {
            query = query.Where(x => x.Contract != null &&
                                     x.Contract.EmploymentTypeId == filter.EmploymentTypeId.Value);
        }

        if (filter.LocationId.HasValue)
        {
            query = query.Where(x => x.Contract != null &&
                                     x.Contract.LocationId == filter.LocationId.Value);
        }

        if (filter.ProjectId.HasValue)
        {
            query = query.Where(x => x.Contract != null &&
                                     x.Contract.ProjectId == filter.ProjectId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.PayrollCurrency))
        {
            query = query.Where(x => x.Contract != null &&
                                     x.Contract.PayrollCurrency == filter.PayrollCurrency);
        }

        if (filter.PaymentFrequency.HasValue)
        {
            query = query.Where(x => x.Contract != null &&
                                     x.Contract.PaymentFrequency == filter.PaymentFrequency.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(x =>
                x.Employee.EmployeeNumber.Contains(term) ||
                x.Employee.FirstName.Contains(term) ||
                x.Employee.LastName.Contains(term) ||
                (x.Employee.NationalId != null && x.Employee.NationalId.Contains(term)));
        }

        var rows = await query
            .OrderBy(x => x.Employee.LastName).ThenBy(x => x.Employee.FirstName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var jobTitles = await _context.JobTitles.AsNoTracking()
            .Where(j => j.CompanyId == companyId)
            .ToDictionaryAsync(j => j.Id, j => j.Name, cancellationToken).ConfigureAwait(false);
        var departments = await _context.Departments.AsNoTracking()
            .Where(d => d.CompanyId == companyId)
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken).ConfigureAwait(false);
        var employmentTypes = await _context.EmploymentTypes.AsNoTracking()
            .Where(t => t.CompanyId == companyId)
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken).ConfigureAwait(false);

        return rows.Select(x => new EmployeeListItem(
                x.Employee.Id,
                x.Employee.EmployeeNumber,
                x.Employee.FullName,
                x.Contract?.JobTitleId is { } jt && jobTitles.TryGetValue(jt, out var jobTitle) ? jobTitle : null,
                x.Contract?.DepartmentId is { } dp && departments.TryGetValue(dp, out var department) ? department : null,
                x.Contract is not null && employmentTypes.TryGetValue(x.Contract.EmploymentTypeId, out var type) ? type : null,
                x.Contract?.PayrollCurrency,
                x.Employee.Status,
                x.Employee.HireDate,
                x.Contract?.EndDate))
            .ToList();
    }

    public async Task<OperationResult<Employee>> CreateAsync(
        Employee employee, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.EmployeesEdit);

        var validation = EmployeeValidation.ValidateEmployee(employee);

        var duplicateNumber = await _context.Employees
            .AnyAsync(e => e.CompanyId == employee.CompanyId &&
                           e.EmployeeNumber == employee.EmployeeNumber, cancellationToken)
            .ConfigureAwait(false);
        validation.AddIf(duplicateNumber, nameof(employee.EmployeeNumber),
            $"Employee number '{employee.EmployeeNumber}' is already in use.");

        if (!string.IsNullOrWhiteSpace(employee.NationalId))
        {
            var duplicateId = await _context.Employees
                .AnyAsync(e => e.CompanyId == employee.CompanyId &&
                               e.NationalId == employee.NationalId, cancellationToken)
                .ConfigureAwait(false);
            validation.AddIf(duplicateId, nameof(employee.NationalId),
                "Another employee already has this national ID.");
        }

        if (!validation.IsValid)
        {
            return OperationResult<Employee>.Failed(validation);
        }

        _context.Employees.Add(employee);
        _context.EmployeeStatusHistory.Add(new EmployeeStatusHistory
        {
            EmployeeId = employee.Id,
            FromStatus = employee.Status,
            ToStatus = employee.Status,
            EffectiveDate = employee.HireDate,
            Reason = "Engaged",
            ChangedBy = _currentUser.UserId,
            ChangedAt = _clock.Now
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return OperationResult<Employee>.Success(employee);
    }

    public async Task<ValidationResult> UpdateAsync(
        Employee employee, CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.EmployeesEdit);

        var validation = EmployeeValidation.ValidateEmployee(employee);

        var duplicateNumber = await _context.Employees
            .AnyAsync(e => e.CompanyId == employee.CompanyId &&
                           e.EmployeeNumber == employee.EmployeeNumber &&
                           e.Id != employee.Id, cancellationToken)
            .ConfigureAwait(false);
        validation.AddIf(duplicateNumber, nameof(employee.EmployeeNumber),
            $"Employee number '{employee.EmployeeNumber}' is already in use.");

        if (!validation.IsValid)
        {
            return validation;
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

    /// <summary>
    /// Changes an employee's status, recording the transition. This is how an employee leaves;
    /// there is no delete.
    /// </summary>
    public async Task<ValidationResult> ChangeStatusAsync(
        Guid employeeId,
        EmployeeStatus newStatus,
        DateOnly effectiveDate,
        string reason,
        CancellationToken cancellationToken = default)
    {
        _currentUser.Require(Permissions.EmployeesDeactivate);

        var validation = ValidationResult.Success();
        validation.Require(reason, nameof(reason), "A reason is required for a status change.");

        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
            .ConfigureAwait(false);

        if (employee is null)
        {
            return validation.Add("Employee", "Employee not found.");
        }

        validation.AddIf(employee.Status == newStatus, "Status",
            $"Employee is already {newStatus}.");
        validation.AddIf(effectiveDate < employee.HireDate, nameof(effectiveDate),
            "Effective date cannot precede the engagement date.");

        if (!validation.IsValid)
        {
            return validation;
        }

        var previous = employee.Status;
        employee.Status = newStatus;

        if (newStatus == EmployeeStatus.Terminated)
        {
            employee.TerminationDate = effectiveDate;
            employee.TerminationReason = reason;

            // End the current contract, but keep every version for payroll history.
            var current = await _context.EmployeeContracts
                .FirstOrDefaultAsync(c => c.EmployeeId == employeeId && c.IsCurrent, cancellationToken)
                .ConfigureAwait(false);
            if (current is not null)
            {
                current.Status = ContractStatus.Ended;
                current.EndDate ??= effectiveDate;
                current.IsCurrent = false;
            }
        }

        _context.EmployeeStatusHistory.Add(new EmployeeStatusHistory
        {
            EmployeeId = employeeId,
            FromStatus = previous,
            ToStatus = newStatus,
            EffectiveDate = effectiveDate,
            Reason = reason,
            ChangedBy = _currentUser.UserId,
            ChangedAt = _clock.Now
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return validation;
    }

}
