using Microsoft.EntityFrameworkCore;
using Tawaka.Domain.Audit;
using Tawaka.Domain.Companies;
using Tawaka.Domain.Currencies;
using Tawaka.Domain.Earnings;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Organisation;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory;

namespace Tawaka.Application.Abstractions;

/// <summary>
/// The data surface the application layer works against.
/// <para>
/// This deliberately exposes EF Core <see cref="DbSet{TEntity}"/>s rather than a repository per
/// entity: the alternative is several thousand lines of pass-through code for no behavioural gain.
/// The application layer still knows nothing about SQLite, connection strings, migrations or
/// interceptors, and the calculation engine remains completely free of data access (ADR-005).
/// </para>
/// </summary>
public interface IPayrollDataContext
{
    DbSet<Company> Companies { get; }
    DbSet<CompanyCurrency> CompanyCurrencies { get; }
    DbSet<CompanyBankAccount> CompanyBankAccounts { get; }

    DbSet<Currency> Currencies { get; }
    DbSet<ExchangeRate> ExchangeRates { get; }

    DbSet<Department> Departments { get; }
    DbSet<JobTitle> JobTitles { get; }
    DbSet<Location> Locations { get; }
    DbSet<Client> Clients { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectSite> ProjectSites { get; }

    DbSet<EmploymentType> EmploymentTypes { get; }
    DbSet<Employee> Employees { get; }
    DbSet<EmployeeContract> EmployeeContracts { get; }
    DbSet<EmployeeStatutoryProfile> EmployeeStatutoryProfiles { get; }
    DbSet<EmployeePaymentAccount> EmployeePaymentAccounts { get; }
    DbSet<EmployeeProjectAssignment> EmployeeProjectAssignments { get; }
    DbSet<EmployeeStatusHistory> EmployeeStatusHistory { get; }
    DbSet<EmployeeNextOfKin> EmployeeNextOfKin { get; }
    DbSet<EmployeeDocument> EmployeeDocuments { get; }

    DbSet<EarningType> EarningTypes { get; }
    DbSet<DeductionType> DeductionTypes { get; }
    DbSet<EmployeeRecurringEarning> EmployeeRecurringEarnings { get; }
    DbSet<EmployeeRecurringDeduction> EmployeeRecurringDeductions { get; }

    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<LoginAttempt> LoginAttempts { get; }

    DbSet<StatutoryRule> StatutoryRules { get; }
    DbSet<PayrollPeriod> PayrollPeriods { get; }
    DbSet<PayrollRun> PayrollRuns { get; }
    DbSet<PayrollRunEmployee> PayrollRunEmployees { get; }
    DbSet<PayrollEarningLine> PayrollEarningLines { get; }
    DbSet<PayrollDeductionLine> PayrollDeductionLines { get; }
    DbSet<PayrollEmployerCostLine> PayrollEmployerCostLines { get; }
    DbSet<PayrollCalculationTraceEntry> PayrollCalculationTraces { get; }
    DbSet<PayrollUnresolvedItem> PayrollUnresolvedItems { get; }
    DbSet<PayrollCostAllocation> PayrollCostAllocations { get; }
    DbSet<AppSetting> AppSettings { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
