using Microsoft.EntityFrameworkCore;
using Tawaka.Application.Abstractions;
using Tawaka.Domain.Audit;
using Tawaka.Domain.Companies;
using Tawaka.Domain.Currencies;
using Tawaka.Domain.Earnings;
using Tawaka.Domain.Employees;
using Tawaka.Domain.Organisation;
using Tawaka.Domain.Payroll;
using Tawaka.Domain.Security;
using Tawaka.Domain.Statutory;
using Tawaka.Domain.Statutory.Obligations;

namespace Tawaka.Infrastructure.Persistence;

/// <summary>
/// The payroll database. Statutory rules use table-per-type mapping: shared identity and
/// verification metadata live in <c>StatutoryRules</c>, with each rule kind in its own table, so
/// the documented table names hold while common behaviour stays in one place.
/// </summary>
public class PayrollDbContext : DbContext, IPayrollDataContext
{
    public PayrollDbContext(DbContextOptions<PayrollDbContext> options) : base(options)
    {
    }

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    public DbSet<StatutoryRule> StatutoryRules => Set<StatutoryRule>();

    public DbSet<TaxRule> TaxRules => Set<TaxRule>();

    public DbSet<TaxBracket> TaxBrackets => Set<TaxBracket>();

    public DbSet<AidsLevyRule> AidsLevyRules => Set<AidsLevyRule>();

    public DbSet<NssaRule> NssaRules => Set<NssaRule>();

    public DbSet<NssaEligibilityRule> NssaEligibilityRules => Set<NssaEligibilityRule>();

    public DbSet<ApwcsRule> ApwcsRules => Set<ApwcsRule>();

    public DbSet<EmployerLevyRule> EmployerLevyRules => Set<EmployerLevyRule>();

    public DbSet<TaxCreditRule> TaxCreditRules => Set<TaxCreditRule>();

    public DbSet<TaxExemptionRule> TaxExemptionRules => Set<TaxExemptionRule>();

    public DbSet<CurrencyTaxStrategyRule> CurrencyTaxStrategyRules => Set<CurrencyTaxStrategyRule>();

    public DbSet<PayrollPeriod> PayrollPeriods => Set<PayrollPeriod>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    // ---- Payroll runs ----------------------------------------------------------------------

    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();

    public DbSet<PayrollRunEmployee> PayrollRunEmployees => Set<PayrollRunEmployee>();

    public DbSet<PayrollEarningLine> PayrollEarningLines => Set<PayrollEarningLine>();

    public DbSet<PayrollDeductionLine> PayrollDeductionLines => Set<PayrollDeductionLine>();

    public DbSet<PayrollEmployerCostLine> PayrollEmployerCostLines => Set<PayrollEmployerCostLine>();

    public DbSet<PayrollCalculationTraceEntry> PayrollCalculationTraces =>
        Set<PayrollCalculationTraceEntry>();

    public DbSet<PayrollUnresolvedItem> PayrollUnresolvedItems => Set<PayrollUnresolvedItem>();

    public DbSet<PayrollCostAllocation> PayrollCostAllocations => Set<PayrollCostAllocation>();

    // ---- Statutory obligations and payslips -------------------------------------------------

    public DbSet<StatutoryObligation> StatutoryObligations => Set<StatutoryObligation>();

    public DbSet<StatutoryObligationLine> StatutoryObligationLines =>
        Set<StatutoryObligationLine>();

    public DbSet<StatutoryPayment> StatutoryPayments => Set<StatutoryPayment>();

    public DbSet<Payslip> Payslips => Set<Payslip>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // ---- Company ---------------------------------------------------------------------------

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<CompanyCurrency> CompanyCurrencies => Set<CompanyCurrency>();

    public DbSet<CompanyBankAccount> CompanyBankAccounts => Set<CompanyBankAccount>();

    // ---- Organisation ----------------------------------------------------------------------

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<JobTitle> JobTitles => Set<JobTitle>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectSite> ProjectSites => Set<ProjectSite>();

    // ---- Employees -------------------------------------------------------------------------

    public DbSet<EmploymentType> EmploymentTypes => Set<EmploymentType>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<EmployeeContract> EmployeeContracts => Set<EmployeeContract>();

    public DbSet<EmployeeStatutoryProfile> EmployeeStatutoryProfiles =>
        Set<EmployeeStatutoryProfile>();

    public DbSet<EmployeePaymentAccount> EmployeePaymentAccounts => Set<EmployeePaymentAccount>();

    public DbSet<EmployeeProjectAssignment> EmployeeProjectAssignments =>
        Set<EmployeeProjectAssignment>();

    public DbSet<EmployeeStatusHistory> EmployeeStatusHistory => Set<EmployeeStatusHistory>();

    public DbSet<EmployeeNextOfKin> EmployeeNextOfKin => Set<EmployeeNextOfKin>();

    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();

    // ---- Earnings and deductions -----------------------------------------------------------

    public DbSet<EarningType> EarningTypes => Set<EarningType>();

    public DbSet<DeductionType> DeductionTypes => Set<DeductionType>();

    public DbSet<EmployeeRecurringEarning> EmployeeRecurringEarnings =>
        Set<EmployeeRecurringEarning>();

    public DbSet<EmployeeRecurringDeduction> EmployeeRecurringDeductions =>
        Set<EmployeeRecurringDeduction>();

    // ---- Security --------------------------------------------------------------------------

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PayrollDbContext).Assembly);
    }
}
