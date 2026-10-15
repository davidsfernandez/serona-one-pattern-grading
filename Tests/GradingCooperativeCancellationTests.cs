using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Core;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingCooperativeCancellationTests
{
    [Fact]
    public void EngineHonorsPreCancelledTokensBeforeStartingGeometryWork()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => new GradingEngine().Calculate(SyntheticGeometry.Rectangle(), new(24, 20_000), cancellation.Token));
    }

    [Fact]
    public async Task CalculationServiceCarriesCancellationIntoTheBoundedEngineCall()
    {
        var artifacts = new ModelingArtifactStore(new PrivateArtifactFixture(), 100_000);
        var configuration = await artifacts.WriteDocumentAsync(Configuration());
        var work = Work(configuration); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var service = new GradingCalculationService(artifacts, Limits());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CalculateAsync(work, cancellation.Token));
    }

    [Fact]
    public async Task ExplicitWorkloadLimitsRejectOversizedInputBeforeCalculation()
    {
        var artifacts = new ModelingArtifactStore(new PrivateArtifactFixture(), 100_000);
        var configuration = await artifacts.WriteDocumentAsync(Configuration());
        var service = new GradingCalculationService(artifacts, Limits(maximumSizes: 3));

        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => service.CalculateAsync(Work(configuration)));

        Assert.Equal("grading_workload_capacity", error.Code);
    }

    [Fact]
    public async Task CalculationReportIsPinnedToTheClaimedRevisionAndConfigurationHash()
    {
        var artifacts = new ModelingArtifactStore(new PrivateArtifactFixture(), 100_000);
        var configuration = await artifacts.WriteDocumentAsync(Configuration());
        var work = Work(configuration);

        var report = await new GradingCalculationService(artifacts, Limits()).CalculateAsync(work);

        Assert.Equal(work.RevisionId, report.RevisionId);
        Assert.Equal(configuration.Sha256, report.SourceConfigurationHash);
        Assert.True(report.Calculation.IsApprovable);
    }

    private static ModelingConfiguration Configuration() => new(SyntheticGeometry.Rectangle(), [], new SourceDocumentProvenance());

    private static GradingWorkItem Work(PrivateModelingArtifact configuration) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        PersistenceFixture.Scope, Guid.NewGuid(), 1, configuration);

    private static GradingWorkloadLimits Limits(int maximumSizes = 4) => new(100_000, 1, maximumSizes, 16, 24, 20_000);
}
