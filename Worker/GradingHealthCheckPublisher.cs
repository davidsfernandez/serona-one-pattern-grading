using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace GestionProduccion.Grading.Worker;

/// <summary>Publishes bounded readiness observations to logs without writing health state outside the worker process.</summary>
public sealed class GradingHealthCheckPublisher(GradingWorkerActivity activity, ILogger<GradingHealthCheckPublisher> logger) : IHealthCheckPublisher
{
    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = activity.Snapshot();
        logger.LogInformation("Grading worker readiness {Readiness}; activity {Activity}.", report.Status, state.Phase);
        return Task.CompletedTask;
    }
}
