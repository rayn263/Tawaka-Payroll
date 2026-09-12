using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tawaka.Application.Abstractions;
using Tawaka.Application.Statutory;
using Tawaka.Infrastructure.Interceptors;
using Tawaka.Infrastructure.Persistence;
using Tawaka.Infrastructure.Seeding;

namespace Tawaka.Infrastructure;

/// <summary>Composition root for the infrastructure layer.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddTawakaInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<LockOverride>();
        services.AddSingleton<ILockOverride>(sp => sp.GetRequiredService<LockOverride>());

        // Replaced by the authenticated user once login exists (Milestone 2).
        services.AddScoped<ICurrentUser, SystemUser>();

        services.AddScoped<AuditInterceptor>();
        services.AddScoped<PeriodLockInterceptor>();

        services.AddDbContext<PayrollDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            options.AddInterceptors(
                sp.GetRequiredService<PeriodLockInterceptor>(),
                sp.GetRequiredService<AuditInterceptor>());
        });

        services.AddScoped<IStatutoryRuleSource, EfStatutoryRuleSource>();
        services.AddScoped<IStatutoryRuleResolver, StatutoryRuleResolver>();
        services.AddScoped<LivePayrollGate>();
        services.AddScoped<StatutoryRuleSeeder>();

        return services;
    }
}
