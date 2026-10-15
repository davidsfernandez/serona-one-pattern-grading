using System.Diagnostics.Metrics;
using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Worker;
using Microsoft.Extensions.Logging;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingWorkerObservabilityTests
{
    [Fact]
    public async Task WorkloadMeasurementsContainCountsWithoutMetricDimensionsOrPrivateLogFields()
    {
        var measurements = new System.Collections.Concurrent.ConcurrentQueue<(string Name, long Value, int Tags)>();
        using var listener = new MeterListener();
        // .NET 8 requires this hook for Dispose to detach enabled instrument subscriptions.
        listener.MeasurementsCompleted = (_, _) => { };
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == "Serona.Grading.Worker") current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            measurements.Enqueue((instrument.Name, value, tags.Length)));
        listener.Start();
        var log = new RecordingLogger();
        var worker = Create(new FakeLifecycle(GradingLeaseObservation.Current), new Publisher(null), log,
            new GradingWorkerActivity(TimeProvider.System));

        await worker.RunNextAsync();

        Assert.Contains(measurements, measurement => measurement == ("grading.worker.artifact.bytes", 1L, 0));
        foreach (var name in new[] { "geometry", "nodes", "diagnostics" })
            Assert.Contains(measurements, measurement => measurement == ($"grading.worker.calculation.{name}", 0L, 0));
        Assert.Contains(log.Fields, fields => fields.Any(field => field.Key == "ArtifactBytes" && Equals(field.Value, 1L)));
        Assert.Contains(log.Fields, fields => fields.Any(field => field.Key == "DerivedGeometryCount" && Equals(field.Value, 0)));
        var allowed = new[] { "JobId", "Outcome", "ArtifactBytes", "DerivedGeometryCount", "DerivedNodeCount", "DiagnosticCount", "{OriginalFormat}" };
        Assert.All(log.Fields.SelectMany(fields => fields), field => Assert.Contains(field.Key, allowed));
        Assert.DoesNotContain(log.Outcomes, outcome => outcome.StartsWith("completed_", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, "completed_valid")]
    [InlineData(false, "completed_invalid")]
    [InlineData(null, "publication_fenced")]
    public async Task PublicationLogsOnlyItsActualOutcome(bool? validity, string outcome)
    {
        var log = new RecordingLogger();
        var activity = new GradingWorkerActivity(TimeProvider.System);
        var publisher = new Publisher(validity);
        var worker = Create(new FakeLifecycle(GradingLeaseObservation.Current), publisher, log, activity);

        Assert.True(await worker.RunNextAsync());

        Assert.Contains(outcome, log.Outcomes);
        Assert.Equal(validity.HasValue ? 1 : 0, activity.Snapshot().CompletedAttempts);
    }

    [Theory]
    [InlineData(GradingLeaseObservation.CancellationRequested, "cancelled")]
    [InlineData(GradingLeaseObservation.Lost, "lease_lost")]
    public async Task FinalFenceReportsCancellationOrLeaseLossWithoutPublishing(GradingLeaseObservation observation, string outcome)
    {
        var log = new RecordingLogger();
        var lifecycle = new FakeLifecycle(observation);
        var publisher = new Publisher(true);
        var worker = Create(lifecycle, publisher, log, new GradingWorkerActivity(TimeProvider.System));

        await worker.RunNextAsync();

        Assert.Contains(outcome, log.Outcomes);
        Assert.Equal(0, publisher.Publications);
        Assert.Equal(observation == GradingLeaseObservation.CancellationRequested ? 1 : 0, lifecycle.Acknowledgements);
    }

    [Fact]
    public async Task ThrowingLoggerAndMeasurementCallbacksCannotPreventPublication()
    {
        using var listener = new MeterListener();
        var callbackCount = 0;
        var completionCount = 0;
        listener.MeasurementsCompleted = (_, _) => Interlocked.Increment(ref completionCount);
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == "Serona.Grading.Worker") current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            Interlocked.Increment(ref callbackCount);
            throw new InvalidOperationException("listener");
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
        {
            Interlocked.Increment(ref callbackCount);
            throw new InvalidOperationException("listener");
        });
        listener.Start();
        var publisher = new Publisher(true);
        var activity = new GradingWorkerActivity(TimeProvider.System);
        var worker = Create(new FakeLifecycle(GradingLeaseObservation.Current), publisher, new RecordingLogger(true), activity);

        Assert.True(await worker.RunNextAsync());

        Assert.Equal(1, publisher.Publications);
        Assert.Equal(1, activity.Snapshot().CompletedAttempts);
        Assert.True(Volatile.Read(ref callbackCount) > 0);
        listener.Dispose();
        Assert.True(Volatile.Read(ref completionCount) > 0);
    }

    private static GradingJobWorker Create(FakeLifecycle lifecycle, Publisher publisher, ILogger<GradingJobWorker> logger,
        GradingWorkerActivity activity) => new(lifecycle, new Calculator(), publisher, new GradingWorkerOptions
        {
            MaximumPending = 1, MaximumArtifactBytes = 1, MaximumPieces = 1, MaximumSizes = 1, MaximumNodes = 1, ProofDepth = 1, ProofWork = 1,
            LeaseDuration = TimeSpan.FromSeconds(2), PollInterval = TimeSpan.FromMilliseconds(10), HeartbeatInterval = TimeSpan.FromSeconds(1),
            ShutdownTimeout = TimeSpan.FromSeconds(1), MaximumCalculationDuration = TimeSpan.FromSeconds(2)
        }, activity, logger);

    private sealed class Calculator : IGradingCalculator
    {
        public Task<StoredGradingResult> CalculateAsync(GradingWorkItem work, CancellationToken ct = default) =>
            Task.FromResult(new StoredGradingResult(work.RevisionId, work.Configuration.Sha256, new([], [], [])));
    }

    private sealed class Publisher(bool? validity) : IGradingResultPublisher
    {
        public int Publications { get; private set; }
        public Task<GradingResultSnapshot?> PublishAsync(GradingWorkItem work, StoredGradingResult report, CancellationToken ct = default)
        {
            Publications++;
            return Task.FromResult(validity.HasValue ? new GradingResultSnapshot(Guid.NewGuid(), work.JobId, work.RevisionId,
                Guid.NewGuid(), validity.Value, work.Configuration, work.ClaimVersion) : null);
        }
    }

    private sealed class RecordingLogger(bool throws = false) : ILogger<GradingJobWorker>
    {
        public List<string> Outcomes { get; } = [];
        public List<KeyValuePair<string, object?>[]> Fields { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (throws) throw new InvalidOperationException("logger");
            var fields = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state).ToArray();
            Fields.Add(fields);
            if (fields.SingleOrDefault(field => field.Key == "Outcome").Value is string outcome) Outcomes.Add(outcome);
            Assert.DoesNotContain(fields, field => field.Key is "Owner" or "Company" or "Path" or "Model");
        }
    }

    private sealed class FakeLifecycle(GradingLeaseObservation renewal, bool throwOnRenew = false) : IGradingJobLifecycle
    {
        private int _claimed;
        public int Acknowledgements { get; private set; }
        public int Releases { get; private set; }
        public bool ReleaseTokenWasCancelled { get; private set; }
        public string? FailCode { get; private set; }
        public Task<GradingWorkItem?> ClaimAsync(GradingExecutionPolicy policy, CancellationToken ct = default) =>
            Task.FromResult<GradingWorkItem?>(Interlocked.Exchange(ref _claimed, 1) == 0 ? Work : null);
        public Task<GradingLeaseObservation> RenewAsync(GradingWorkItem work, GradingExecutionPolicy policy, CancellationToken ct = default)
            => throwOnRenew ? throw new InvalidOperationException("synthetic heartbeat failure") : Task.FromResult(renewal);
        public Task<bool> ReleaseAsync(GradingWorkItem work, CancellationToken ct = default) { Releases++; ReleaseTokenWasCancelled = ct.IsCancellationRequested; return Task.FromResult(true); }
        public Task<bool> AcknowledgeCancellationAsync(GradingWorkItem work, CancellationToken ct = default) { Acknowledgements++; return Task.FromResult(true); }
        public Task<GradingCancellationSnapshot> CancelAsync(ModelingScope scope, Guid jobId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GradingResultSnapshot?> CompleteAsync(GradingWorkItem work, bool valid, PrivateModelingArtifact artifact, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> FailAsync(GradingWorkItem work, string diagnosticCode, CancellationToken ct = default)
        { FailCode = diagnosticCode; return Task.FromResult(true); }
    }

    private static readonly GradingWorkItem Work = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new ModelingScope("company", "owner"),
        Guid.NewGuid(), 1, new PrivateModelingArtifact(Guid.NewGuid().ToString("N"), "hash", 1));
}
