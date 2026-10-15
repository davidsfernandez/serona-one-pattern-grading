using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingHostedWorkerTests
{
    [Fact]
    public async Task CancellationRequestCancelsCalculationAndAcknowledgesWithoutPublishing()
    {
        var lifecycle = new FakeLifecycle(GradingLeaseObservation.CancellationRequested);
        var calculator = new BlockingCalculator();
        var publisher = new RecordingPublisher();
        var worker = CreateWorker(lifecycle, calculator, publisher);

        Assert.True(await worker.RunNextAsync());

        Assert.True(calculator.Cancelled);
        Assert.Equal(1, lifecycle.Acknowledgements);
        Assert.Equal(0, publisher.Publications);
    }

    [Fact]
    public async Task LostLeaseCancelsCalculationAndNeverPublishes()
    {
        var lifecycle = new FakeLifecycle(GradingLeaseObservation.Lost);
        var calculator = new BlockingCalculator();
        var publisher = new RecordingPublisher();
        var worker = CreateWorker(lifecycle, calculator, publisher);

        Assert.True(await worker.RunNextAsync());

        Assert.True(calculator.Cancelled);
        Assert.Equal(0, lifecycle.Acknowledgements);
        Assert.Equal(0, publisher.Publications);
    }

    [Fact]
    public async Task ShutdownReleasesTheCurrentLeaseWithAnUncancelledToken()
    {
        var lifecycle = new FakeLifecycle(GradingLeaseObservation.Current);
        var calculator = new BlockingCalculator();
        var worker = CreateWorker(lifecycle, calculator, new RecordingPublisher());
        using var stop = new CancellationTokenSource();
        var attempt = worker.RunNextAsync(stop.Token);

        await calculator.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        stop.Cancel();
        await attempt;

        Assert.Equal(1, lifecycle.Releases);
        Assert.False(lifecycle.ReleaseTokenWasCancelled);
    }

    [Fact]
    public async Task HeartbeatFailureCancelsCalculationAndNeverPublishes()
    {
        var lifecycle = new FakeLifecycle(GradingLeaseObservation.Current, throwOnRenew: true);
        var calculator = new BlockingCalculator();
        var publisher = new RecordingPublisher();
        var worker = CreateWorker(lifecycle, calculator, publisher);

        Assert.True(await worker.RunNextAsync());

        Assert.True(calculator.Cancelled);
        Assert.Equal(0, publisher.Publications);
    }

    [Fact]
    public async Task DeadlinePreventsPublicationWhenCalculatorReturnsAfterCancellation()
    {
        var lifecycle = new FakeLifecycle(GradingLeaseObservation.Current);
        var publisher = new RecordingPublisher();
        var options = ValidOptions();
        options.MaximumCalculationDuration = TimeSpan.FromMilliseconds(50);
        var worker = new GradingJobWorker(lifecycle, new CancellationIgnoringCalculator(), publisher, options,
            new GradingWorkerActivity(TimeProvider.System), NullLogger<GradingJobWorker>.Instance);

        Assert.True(await worker.RunNextAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(0, publisher.Publications);
        Assert.Equal("grading_execution_deadline", lifecycle.FailCode);
    }

    private static GradingJobWorker CreateWorker(FakeLifecycle lifecycle, BlockingCalculator calculator, RecordingPublisher publisher) => new(
        lifecycle, calculator, publisher, ValidOptions(), new GradingWorkerActivity(TimeProvider.System), NullLogger<GradingJobWorker>.Instance);

    private static GradingWorkerOptions ValidOptions() => new()
    {
        MaximumPending = 1, MaximumArtifactBytes = 1, MaximumPieces = 1, MaximumSizes = 1, MaximumNodes = 1, ProofDepth = 1, ProofWork = 1,
        LeaseDuration = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromMilliseconds(10), HeartbeatInterval = TimeSpan.FromMilliseconds(10),
        ShutdownTimeout = TimeSpan.FromSeconds(1), MaximumCalculationDuration = TimeSpan.FromSeconds(1)
    };

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

    private sealed class CancellationIgnoringCalculator : IGradingCalculator
    {
        public async Task<StoredGradingResult> CalculateAsync(GradingWorkItem work, CancellationToken ct = default)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Simulate a calculator that returns a report despite the expired execution deadline.
                return new StoredGradingResult(work.RevisionId, work.Configuration.Sha256, new([], [], []));
            }
            throw new InvalidOperationException();
        }
    }

    private sealed class BlockingCalculator : IGradingCalculator
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled { get; private set; }
        public async Task<StoredGradingResult> CalculateAsync(GradingWorkItem work, CancellationToken ct = default)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { Cancelled = true; throw; }
            throw new InvalidOperationException();
        }
    }

    private sealed class RecordingPublisher : IGradingResultPublisher
    {
        public int Publications { get; private set; }
        public Task<GradingResultSnapshot?> PublishAsync(GradingWorkItem work, StoredGradingResult report, CancellationToken ct = default)
        { Publications++; return Task.FromResult<GradingResultSnapshot?>(null); }
    }

    private static readonly GradingWorkItem Work = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new ModelingScope("company", "owner"),
        Guid.NewGuid(), 1, new PrivateModelingArtifact(Guid.NewGuid().ToString("N"), "hash", 1));
}
