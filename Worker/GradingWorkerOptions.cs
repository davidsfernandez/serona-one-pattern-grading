using GestionProduccion.Application.ModelingGrading;

namespace GestionProduccion.Grading.Worker;

/// <summary>Configuration deliberately has no workload defaults because Phase 8 must establish those limits.</summary>
public sealed class GradingWorkerOptions
{
    public const string SectionName = "Grading:Worker";
    public int MaximumPending { get; set; }
    public long MaximumArtifactBytes { get; set; }
    public int MaximumPieces { get; set; }
    public int MaximumSizes { get; set; }
    public int MaximumNodes { get; set; }
    public long? MaximumDerivedNodes { get; set; }
    public long? MaximumRules { get; set; }
    public int ProofDepth { get; set; }
    public int ProofWork { get; set; }
    public TimeSpan LeaseDuration { get; set; }
    public TimeSpan PollInterval { get; set; }
    public TimeSpan HeartbeatInterval { get; set; }
    public TimeSpan ShutdownTimeout { get; set; }
    public TimeSpan MaximumCalculationDuration { get; set; }
    public int MaximumGlobalActive { get; set; } = 1;
    public int MaximumOwnerActive { get; set; } = 1;

    public void RequireValid()
    {
        ToPolicy().RequireValid();
        ToWorkloadLimits().RequireValid();
        if (PollInterval <= TimeSpan.Zero || HeartbeatInterval <= TimeSpan.Zero || ShutdownTimeout <= TimeSpan.Zero ||
            MaximumCalculationDuration <= TimeSpan.Zero || HeartbeatInterval >= LeaseDuration)
            throw new InvalidOperationException("grading_worker_not_configured");
    }

    public GradingExecutionPolicy ToPolicy() => new(MaximumPending, LeaseDuration, MaximumGlobalActive, MaximumOwnerActive);
    public GradingWorkloadLimits ToWorkloadLimits() => new(MaximumArtifactBytes, MaximumPieces, MaximumSizes, MaximumNodes, ProofDepth, ProofWork)
    {
        MaximumDerivedNodes = MaximumDerivedNodes ?? (long)MaximumNodes * MaximumSizes,
        MaximumRules = MaximumRules ?? (long)MaximumNodes * MaximumSizes
    };
}
