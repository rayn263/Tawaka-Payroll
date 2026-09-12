using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Tawaka.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c> at design time to create migrations.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PayrollDbContext>
{
    public PayrollDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseSqlite("Data Source=tawaka-design.db")
            .Options;

        return new PayrollDbContext(options);
    }
}
