using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tawaka.Domain.Audit;
using Tawaka.Domain.Payroll;

namespace Tawaka.Infrastructure.Persistence.Configurations;

public sealed class PayrollPeriodConfiguration : IEntityTypeConfiguration<PayrollPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollPeriod> builder)
    {
        builder.ToTable("PayrollPeriods");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.LockedBy).HasMaxLength(100);
        builder.Property(p => p.CreatedBy).HasMaxLength(100);
        builder.Property(p => p.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(p => new { p.CompanyId, p.Code }).IsUnique();
    }
}

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Key).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(2000);
        builder.Property(s => s.DataType).HasMaxLength(40).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500);
        builder.Property(s => s.CreatedBy).HasMaxLength(100);
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(s => s.Key).IsUnique();
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.UserName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(120).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(80).IsRequired();
        builder.Property(a => a.FieldName).HasMaxLength(120);
        builder.Property(a => a.OldValue).HasMaxLength(4000);
        builder.Property(a => a.NewValue).HasMaxLength(4000);
        builder.Property(a => a.Reason).HasMaxLength(1000);
        builder.Property(a => a.Machine).HasMaxLength(200);

        builder.HasIndex(a => new { a.EntityName, a.EntityId, a.OccurredAt });
        builder.HasIndex(a => a.OccurredAt);
    }
}
