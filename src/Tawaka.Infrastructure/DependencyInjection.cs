using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Employees;
using Tawaka.Application.Security;
using Tawaka.Application.Statutory;
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
        services.AddScoped<LivePayrollGate>();

        services.AddScoped<AuthenticationService>();
        services.AddScoped<RoleService>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<EmployeeContractService>();

        services.AddScoped<StatutoryRuleSeeder>();
        services.AddScoped<CompanySeeder>();
        services.AddScoped<SecuritySeeder>();
        services.AddScoped<ApplicationSeeder>();

        return services;
    }
}
