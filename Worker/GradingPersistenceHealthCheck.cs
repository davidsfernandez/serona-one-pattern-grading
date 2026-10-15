using GestionProduccion.Grading.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GestionProduccion.Grading.Worker;

/// <summary>Checks database availability and schema currency without executing migrations.</summary>
public sealed class GradingPersistenceHealthCheck(IDbContextFactory<GradingDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            if (!await db.Database.CanConnectAsync(cancellationToken) || (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                return HealthCheckResult.Unhealthy("Grading persistence is not ready.");
            return HealthCheckResult.Healthy("Grading persistence is ready.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Grading persistence is unavailable.");
        }
    }
}
