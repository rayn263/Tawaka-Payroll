using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tawaka.Domain.Organisation;

namespace Tawaka.Infrastructure.Persistence.Configurations;

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Code).HasMaxLength(40).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(150).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(500);
        builder.Property(d => d.CostCentreCode).HasMaxLength(40);
        builder.Property(d => d.CreatedBy).HasMaxLength(100);
        builder.Property(d => d.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(d => new { d.CompanyId, d.Code }).IsUnique();
    }
}

public sealed class JobTitleConfiguration : IEntityTypeConfiguration<JobTitle>
{
    public void Configure(EntityTypeBuilder<JobTitle> builder)
    {
        builder.ToTable("JobTitles");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Code).HasMaxLength(40).IsRequired();
        builder.Property(j => j.Name).HasMaxLength(150).IsRequired();
        builder.Property(j => j.Grade).HasMaxLength(40);
        builder.Property(j => j.Description).HasMaxLength(500);
        builder.Property(j => j.CreatedBy).HasMaxLength(100);
        builder.Property(j => j.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(j => new { j.CompanyId, j.Code }).IsUnique();
    }
}

public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Code).HasMaxLength(40).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(150).IsRequired();
        builder.Property(l => l.AddressLine1).HasMaxLength(200);
        builder.Property(l => l.AddressLine2).HasMaxLength(200);
        builder.Property(l => l.City).HasMaxLength(100);
        builder.Property(l => l.CreatedBy).HasMaxLength(100);
        builder.Property(l => l.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(l => new { l.CompanyId, l.Code }).IsUnique();
    }
}

public sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("Clients");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(40).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.ContactName).HasMaxLength(150);
        builder.Property(c => c.Phone).HasMaxLength(60);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.AddressLine1).HasMaxLength(200);
        builder.Property(c => c.CreatedBy).HasMaxLength(100);
        builder.Property(c => c.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique();
    }
}

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.ContractValueAmount)
            .HasConversion(new NullableScaledDecimalConverter(MoneyScales.Money));
        builder.Property(p => p.ContractValueCurrency).HasMaxLength(3);
        builder.Property(p => p.CreatedBy).HasMaxLength(100);
        builder.Property(p => p.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(p => new { p.CompanyId, p.Code }).IsUnique();

        builder.HasOne(p => p.Client).WithMany()
            .HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Location).WithMany()
            .HasForeignKey(p => p.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(p => p.Sites).WithOne(s => s.Project!)
            .HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ProjectSiteConfiguration : IEntityTypeConfiguration<ProjectSite>
{
    public void Configure(EntityTypeBuilder<ProjectSite> builder)
    {
        builder.ToTable("ProjectSites");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Code).HasMaxLength(40).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.AddressLine1).HasMaxLength(200);
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.CreatedBy).HasMaxLength(100);
        builder.Property(s => s.ModifiedBy).HasMaxLength(100);
        builder.HasIndex(s => new { s.CompanyId, s.Code }).IsUnique();
    }
}
