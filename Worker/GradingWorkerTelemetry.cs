using System.Diagnostics.Metrics;
using GestionProduccion.Shared.ModelingGrading;
using Microsoft.Extensions.Logging;

namespace GestionProduccion.Grading.Worker;

/// <summary>Best-effort operational signals; private work data never becomes a metric dimension.</summary>
internal sealed class GradingWorkerTelemetry(ILogger<GradingJobWorker> logger)
{
    internal const string MeterName = "Serona.Grading.Worker";
    private static readonly Meter? Meter;
    private static readonly Counter<long>? Events;
    private static readonly Histogram<double>? Duration;
    private static readonly Histogram<long>? ArtifactBytes;
    private static readonly Histogram<long>? DerivedGeometry;
    private static readonly Histogram<long>? DerivedNodes;
    private static readonly Histogram<long>? Diagnostics;

    static GradingWorkerTelemetry()
    {
        // Instrument publication can invoke a listener before the first measurement too.
        try
        {
            Meter = new(MeterName, "1.0");
            Events = Meter.CreateCounter<long>("grading.worker.events", "{event}");
            Duration = Meter.CreateHistogram<double>("grading.worker.duration", "s");
            ArtifactBytes = Meter.CreateHistogram<long>("grading.worker.artifact.bytes", "By");
            DerivedGeometry = Meter.CreateHistogram<long>("grading.worker.calculation.geometry", "{geometry}");
            DerivedNodes = Meter.CreateHistogram<long>("grading.worker.calculation.nodes", "{node}");
            Diagnostics = Meter.CreateHistogram<long>("grading.worker.calculation.diagnostics", "{diagnostic}");
        }
        catch (Exception) { }
    }

    public void Record(string outcome, Guid? jobId = null)
    {
        // Listener and logging providers are external callbacks and must never affect durable transitions.
        try { Events?.Add(1, new KeyValuePair<string, object?>("outcome", outcome)); }
        catch (Exception) { }
        try
        {
            var level = outcome is "claim_failed" or "heartbeat_failed" or "calculation_failed" or "failed" or "deadline"
                or "failure_persistence_failed" or "shutdown_release_failed" ? LogLevel.Error
                : outcome is "lease_lost" or "publication_fenced" or "cancellation_fenced" or "failure_fenced"
                    or "shutdown_release_fenced" ? LogLevel.Warning : LogLevel.Information;
            logger.Log(level, "Grading worker event {Outcome} for job {JobId}.", outcome, jobId);
        }
        catch (Exception) { }
    }

    public void Elapsed(double seconds)
    {
        try { Duration?.Record(seconds); }
        catch (Exception) { }
    }

    public void Workload(Guid jobId, long artifactBytes)
    {
        try { ArtifactBytes?.Record(artifactBytes); }
        catch (Exception) { }
        try { logger.LogInformation("Grading workload for job {JobId}: {ArtifactBytes} artifact bytes.", jobId, artifactBytes); }
        catch (Exception) { }
    }

    public void Calculated(Guid jobId, GradingCalculation calculation)
    {
        // These are calculation observations, independent of whether publication later wins its fence.
        try
        {
            var geometryCount = calculation.Geometry.Length;
            var nodeCount = calculation.Geometry.Sum(geometry => (long)geometry.Contour.Nodes.Length);
            var diagnosticCount = calculation.Diagnostics.Length;
            try { DerivedGeometry?.Record(geometryCount); } catch (Exception) { }
            try { DerivedNodes?.Record(nodeCount); } catch (Exception) { }
            try { Diagnostics?.Record(diagnosticCount); } catch (Exception) { }
            try
            {
                logger.LogInformation("Grading calculation for job {JobId}: {DerivedGeometryCount} derived geometries, {DerivedNodeCount} derived nodes, {DiagnosticCount} diagnostics.",
                    jobId, geometryCount, nodeCount, diagnosticCount);
            }
            catch (Exception) { }
        }
        catch (Exception) { }
    }
}
