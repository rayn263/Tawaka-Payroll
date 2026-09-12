using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tawaka.Domain.Security;

namespace Tawaka.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Username).HasMaxLength(100).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(200);
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(u => u.CreatedBy).HasMaxLength(100);
        builder.Property(u => u.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(u => u.Username).IsUnique();
        builder.HasMany(u => u.UserRoles).WithOne(r => r.User!)
            .HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.CreatedBy).HasMaxLength(100);
        builder.Property(r => r.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(r => r.Name).IsUnique();
        builder.HasMany(r => r.RolePermissions).WithOne(rp => rp.Role!)
            .HasForeignKey(rp => rp.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(80).IsRequired();
        builder.Property(p => p.Category).HasMaxLength(60).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(300).IsRequired();
        builder.Property(p => p.ConflictsWith).HasMaxLength(80);
        builder.HasIndex(p => p.Code).IsUnique();
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(rp => rp.Id);
        builder.HasOne(rp => rp.Permission).WithMany()
            .HasForeignKey(rp => rp.PermissionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique();
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(ur => ur.Id);
        builder.HasOne(ur => ur.Role).WithMany()
            .HasForeignKey(ur => ur.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();
    }
}

public sealed class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        builder.ToTable("LoginAttempts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Username).HasMaxLength(100).IsRequired();
        builder.Property(a => a.FailureReason).HasMaxLength(200);
        builder.Property(a => a.Machine).HasMaxLength(200);
        builder.HasIndex(a => new { a.Username, a.AttemptedAt });
    }
}
