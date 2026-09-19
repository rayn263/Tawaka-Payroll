using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Employees;
using Tawaka.Application.Accounting;
using Tawaka.Application.Administration;
using Tawaka.Application.Calendars;
using Tawaka.Application.Leave;
using Tawaka.Application.Loans;
using Tawaka.Application.Payroll;
using Tawaka.Application.Time;
using Tawaka.Application.Payslips;
using Tawaka.Application.Reports;
using Tawaka.Application.Statutory.Obligations;
using Tawaka.Application.Security;
using Tawaka.Application.Release;
using Tawaka.Application.Statutory;
using Tawaka.Infrastructure.Administration;
using Tawaka.Infrastructure.Interceptors;
using Tawaka.Infrastructure.Persistence;
using Tawaka.Infrastructure.Seeding;

namespace Tawaka.Infrastructure;

/// <summary>Composition root for the infrastructure and application services.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddTawakaInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<LockOverride>();
        services.AddSingleton<ILockOverride>(sp => sp.GetRequiredService<LockOverride>());

        // The signed-in user for this desktop session. Registered as a singleton so that the audit
        // interceptor, the UI and every service see the same identity, and as ICurrentUser so the
        // audit trail is attributed to a real person from sign-in onwards.
        services.AddSingleton<UserSession>();
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<UserSession>());

        services.AddSingleton<IPasswordHasher>(_ => new PasswordHasher());
        services.AddSingleton(new PasswordPolicy());

        services.AddScoped<AuditInterceptor>();
        services.AddScoped<PeriodLockInterceptor>();

        services.AddDbContext<PayrollDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            options.AddInterceptors(
                sp.GetRequiredService<PeriodLockInterceptor>(),
                sp.GetRequiredService<AuditInterceptor>());
        });

        services.AddScoped<IPayrollDataContext>(sp => sp.GetRequiredService<PayrollDbContext>());

        services.AddScoped<IStatutoryRuleSource, EfStatutoryRuleSource>();
        services.AddScoped<IStatutoryRuleResolver, StatutoryRuleResolver>();
        services.AddScoped<StatutoryRuleVerificationService>();
        services.AddScoped<LivePayrollGate>();

        services.AddScoped<AuthenticationService>();
        services.AddScoped<RoleService>();
        services.AddScoped<UserAdministrationService>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<EmployeeContractService>();
        services.AddScoped<TimesheetService>();
        services.AddScoped<LeaveService>();
        services.AddScoped<HolidayCalendarService>();
        services.AddScoped<LoanService>();
        services.AddScoped<PayrollSnapshotBuilder>();
        services.AddScoped<PayrollSnapshotStore>();
        services.AddScoped<PayrollRunService>();
        services.AddScoped<StatutoryObligationService>();
        services.AddScoped<PayslipBuilder>();
        services.AddScoped<PayrollReportService>();
        services.AddScoped<JournalExportService>();

        // Scoped, and cached for the life of the request: an audit page resolving the same few
        // actors once per row would be a few hundred queries.
        services.AddScoped<UserDirectory>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<ReleaseReadinessService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<BackupService>();

        services.AddScoped<StatutoryRuleSeeder>();
        services.AddScoped<CompanySeeder>();
        services.AddScoped<SecuritySeeder>();
        services.AddScoped<DemoDataSeeder>();
        services.AddScoped<ApplicationSeeder>();

        return services;
    }
}
