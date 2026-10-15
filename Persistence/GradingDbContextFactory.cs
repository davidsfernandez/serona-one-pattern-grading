using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GestionProduccion.Grading.Persistence;

public sealed class GradingDbContextFactory : IDesignTimeDbContextFactory<GradingDbContext>
{
    public GradingDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("NESTING_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Set NESTING_CONNECTION_STRING explicitly for EF design-time commands.");
        return new(new DbContextOptionsBuilder<GradingDbContext>()
            .UseMySql(connection, new MySqlServerVersion(new Version(8, 0, 36)),
                mysql => mysql.MigrationsHistoryTable("__GradingMigrationsHistory")).Options);
    }
}
