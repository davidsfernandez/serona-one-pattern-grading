namespace GestionProduccion.Grading.Worker;

public enum GradingWorkerPhase { Starting, Polling, Idle, Processing, Faulted, Stopping, Stopped }
public enum GradingWorkerFailure { None, Polling, Lifecycle, Calculation, Publication, Deadline }
public sealed record GradingWorkerActivitySnapshot(GradingWorkerPhase Phase, DateTimeOffset LastActivityUtc,
    DateTimeOffset? ProcessingStartedUtc, long CompletedAttempts, GradingWorkerFailure LastFailure, TimeSpan ElapsedSinceActivity);

/// <summary>Exposes process liveness without retaining private job, model, or owner data.</summary>
public sealed class GradingWorkerActivity(TimeProvider time)
{
    private readonly object _sync = new();
    private GradingWorkerPhase _phase = GradingWorkerPhase.Starting;
    private DateTimeOffset _lastActivityUtc = time.GetUtcNow();
    private long _lastActivityTimestamp = time.GetTimestamp();
    private DateTimeOffset? _processingStartedUtc;
    private long _completedAttempts;
    private GradingWorkerFailure _lastFailure;

    public GradingWorkerActivitySnapshot Snapshot()
    {
        lock (_sync) return new(_phase, _lastActivityUtc, _processingStartedUtc, _completedAttempts, _lastFailure,
            time.GetElapsedTime(_lastActivityTimestamp));
    }
    public void Polling() => Transition(GradingWorkerPhase.Polling, GradingWorkerFailure.None, false);
    public void Idle() => Transition(GradingWorkerPhase.Idle, GradingWorkerFailure.None, false);
    public void Processing() => Transition(GradingWorkerPhase.Processing, GradingWorkerFailure.None, true);
    public void Heartbeat()
    {
        lock (_sync)
        {
            _lastActivityUtc = time.GetUtcNow();
            _lastActivityTimestamp = time.GetTimestamp();
        }
    }
    public void Completed() { lock (_sync) { _completedAttempts++; TransitionUnsafe(GradingWorkerPhase.Idle, GradingWorkerFailure.None, false); } }
    public void Failure(GradingWorkerFailure failure) => Transition(GradingWorkerPhase.Faulted, failure, false);
    public void Stopping() => Transition(GradingWorkerPhase.Stopping, _lastFailure, false);
    public void Stopped() => Transition(GradingWorkerPhase.Stopped, _lastFailure, false);

    private void Transition(GradingWorkerPhase phase, GradingWorkerFailure failure, bool processing)
    { lock (_sync) TransitionUnsafe(phase, failure, processing); }
    private void TransitionUnsafe(GradingWorkerPhase phase, GradingWorkerFailure failure, bool processing)
    {
        _phase = phase; _lastFailure = failure; _lastActivityUtc = time.GetUtcNow(); _lastActivityTimestamp = time.GetTimestamp();
        _processingStartedUtc = processing ? _lastActivityUtc : null;
    }
}
