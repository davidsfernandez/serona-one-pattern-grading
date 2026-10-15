using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GestionProduccion.Grading.Worker;

public sealed class GradingWorkerHealthCheck(GradingWorkerActivity activity, GradingWorkerOptions options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = activity.Snapshot();
        var status = state.Phase is GradingWorkerPhase.Stopped or GradingWorkerPhase.Faulted ? HealthStatus.Unhealthy
            : state.Phase == GradingWorkerPhase.Stopping ? HealthStatus.Degraded
            : state.ElapsedSinceActivity > (state.Phase == GradingWorkerPhase.Processing ? options.MaximumCalculationDuration : options.PollInterval + options.HeartbeatInterval)
                ? HealthStatus.Unhealthy : state.Phase == GradingWorkerPhase.Starting ? HealthStatus.Degraded : HealthStatus.Healthy;
        return Task.FromResult(new HealthCheckResult(status, "Grading worker activity: " + state.Phase + ".", data: new Dictionary<string, object>
        {
            ["phase"] = state.Phase.ToString(), ["completedAttempts"] = state.CompletedAttempts, ["lastFailure"] = state.LastFailure.ToString()
        }));
    }
}
