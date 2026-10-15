using System.Diagnostics;
using GestionProduccion.Application.ModelingGrading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GestionProduccion.Grading.Worker;

/// <summary>Executes one durable grading lease and lets the lifecycle own every state transition and fence.</summary>
public sealed class GradingJobWorker(
    IGradingJobLifecycle lifecycle,
    IGradingCalculator calculator,
    IGradingResultPublisher publisher,
    GradingWorkerOptions options,
    GradingWorkerActivity activity,
    ILogger<GradingJobWorker> logger) : BackgroundService
{
    private readonly GradingWorkerTelemetry _telemetry = new(logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
                if (!await RunNextAsync(stoppingToken)) await Task.Delay(options.PollInterval, stoppingToken);
        }
        finally { activity.Stopped(); }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        activity.Stopping();
        return base.StopAsync(cancellationToken);
    }

    public async Task<bool> RunNextAsync(CancellationToken cancellationToken = default)
    {
        activity.Polling();
        GradingWorkItem? work;
        try { work = await lifecycle.ClaimAsync(options.ToPolicy(), cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { activity.Stopping(); throw; }
        catch (Exception)
        {
            activity.Failure(GradingWorkerFailure.Lifecycle);
            _telemetry.Record("claim_failed");
            return false;
        }
        if (work is null) { activity.Idle(); return false; }
        activity.Processing();
        _telemetry.Record("claimed", work.JobId);
        _telemetry.Workload(work.JobId, work.Configuration.Length);
        var started = Stopwatch.GetTimestamp();
        try { await ExecuteClaimedAsync(work, cancellationToken); }
        finally { _telemetry.Elapsed(Stopwatch.GetElapsedTime(started).TotalSeconds); }
        return true;
    }

    private async Task ExecuteClaimedAsync(GradingWorkItem work, CancellationToken stoppingToken)
    {
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(options.MaximumCalculationDuration);
        using var calculationToken = CancellationTokenSource.CreateLinkedTokenSource(execution.Token, deadline.Token);
        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, deadline.Token);
        var monitor = ObserveLeaseAsync(work, execution, monitorCancellation.Token);
        try
        {
            var report = await calculator.CalculateAsync(work, calculationToken.Token);
            _telemetry.Calculated(work.JobId, report.Calculation);
            calculationToken.Token.ThrowIfCancellationRequested();
            monitorCancellation.Cancel();
            var observed = await ObserveStoppedMonitorAsync(monitor);
            if (await HandleLeaseObservationAsync(work, observed)) return;
            stoppingToken.ThrowIfCancellationRequested();
            var final = await lifecycle.RenewAsync(work, options.ToPolicy(), CancellationToken.None);
            if (await HandleLeaseObservationAsync(work, final)) return;
            var published = await publisher.PublishAsync(work, report, stoppingToken);
            if (published is null)
            {
                _telemetry.Record("publication_fenced", work.JobId);
                activity.Failure(GradingWorkerFailure.Publication);
                return;
            }
            _telemetry.Record(published.IsValid ? "completed_valid" : "completed_invalid", work.JobId);
            activity.Completed();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            monitorCancellation.Cancel();
            await ObserveStoppedMonitorAsync(monitor);
            await ReleaseForShutdownAsync(work);
            activity.Stopping();
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            monitorCancellation.Cancel();
            var observed = await ObserveStoppedMonitorAsync(monitor);
            if (!await HandleLeaseObservationAsync(work, observed)) await FailAsync(work, "grading_execution_deadline", GradingWorkerFailure.Deadline);
        }
        catch (OperationCanceledException)
        {
            monitorCancellation.Cancel();
            var observed = await ObserveStoppedMonitorAsync(monitor);
            if (!await HandleLeaseObservationAsync(work, observed)) await FailAsync(work, "grading_calculation_cancelled", GradingWorkerFailure.Calculation);
        }
        catch (Exception)
        {
            monitorCancellation.Cancel();
            var observed = await ObserveStoppedMonitorAsync(monitor);
            if (!await HandleLeaseObservationAsync(work, observed)) await FailAsync(work, "grading_calculation_failed", GradingWorkerFailure.Calculation);
            _telemetry.Record("calculation_failed", work.JobId);
        }
    }

    private async Task<GradingLeaseObservation> ObserveLeaseAsync(GradingWorkItem work, CancellationTokenSource execution, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(options.HeartbeatInterval, ct);
                var observed = await lifecycle.RenewAsync(work, options.ToPolicy(), ct);
                activity.Heartbeat();
                if (observed == GradingLeaseObservation.Current) continue;
                execution.Cancel();
                return observed;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return GradingLeaseObservation.Current;
        }
        catch (Exception)
        {
            execution.Cancel();
            activity.Failure(GradingWorkerFailure.Lifecycle);
            _telemetry.Record("heartbeat_failed", work.JobId);
            return GradingLeaseObservation.Lost;
        }
    }

    private static async Task<GradingLeaseObservation> ObserveStoppedMonitorAsync(Task<GradingLeaseObservation> monitor)
    {
        try { return await monitor; }
        catch (Exception) { return GradingLeaseObservation.Lost; }
    }

    private async Task<bool> HandleLeaseObservationAsync(GradingWorkItem work, GradingLeaseObservation observation)
    {
        if (observation == GradingLeaseObservation.Current) return false;
        if (observation == GradingLeaseObservation.CancellationRequested)
        {
            if (await lifecycle.AcknowledgeCancellationAsync(work, CancellationToken.None))
            {
                _telemetry.Record("cancelled", work.JobId);
                activity.Completed();
            }
            else
            {
                _telemetry.Record("cancellation_fenced", work.JobId);
                activity.Failure(GradingWorkerFailure.Lifecycle);
            }
        }
        else
        {
            _telemetry.Record("lease_lost", work.JobId);
            activity.Failure(GradingWorkerFailure.Lifecycle);
        }
        return true;
    }

    private async Task ReleaseForShutdownAsync(GradingWorkItem work)
    {
        try
        {
            var released = await lifecycle.ReleaseAsync(work, CancellationToken.None);
            _telemetry.Record(released ? "shutdown_released" : "shutdown_release_fenced", work.JobId);
        }
        catch (Exception) { _telemetry.Record("shutdown_release_failed", work.JobId); }
    }

    private async Task FailAsync(GradingWorkItem work, string diagnostic, GradingWorkerFailure failure)
    {
        try
        {
            var failed = await lifecycle.FailAsync(work, diagnostic, CancellationToken.None);
            _telemetry.Record(!failed ? "failure_fenced" : failure == GradingWorkerFailure.Deadline ? "deadline" : "failed", work.JobId);
            activity.Failure(failure);
        }
        catch (Exception) { activity.Failure(GradingWorkerFailure.Lifecycle); _telemetry.Record("failure_persistence_failed", work.JobId); }
    }
}
