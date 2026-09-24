using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lensee.Modules.Finance.Data;

/// <summary>Enables migration discovery without starting the application host.</summary>
public sealed class FinanceDbContextFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=lensee_design_time;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connectionString).Options;
        return new FinanceDbContext(options);
    }
}
