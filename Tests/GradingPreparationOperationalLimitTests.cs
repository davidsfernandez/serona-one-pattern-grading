using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Core;
using GestionProduccion.Shared.ModelingGrading;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingPreparationOperationalLimitTests
{
    [Fact]
    public void ExactAggregateVertexBudgetAcceptsBothDefinitionsAndPreservesQuantities()
    {
        var (approved, request) = Fixture();
        var limits = new GradingPolygonizationLimits(24, 20000) { MaximumTotalVertices = 8 };

        var prepared = new GradingNestingProblemBuilder(new(limits), limits)
            .Build(new("synthetic-company", "synthetic-owner"), Guid.NewGuid(), approved, request);

        Assert.Equal(2, prepared.Request.Problem.Definitions.Count);
        Assert.Equal(8, prepared.Conversion.Sum(c => c.Segments.Length));
        Assert.Equal(new[] { 2, 3 }, prepared.Pieces.Select(p => p.Quantity));
        Assert.Equal(5, prepared.Request.Problem.Instances.Count);
        foreach (var piece in prepared.Pieces)
            Assert.Equal(piece.Quantity, prepared.Request.Problem.Instances.Count(i => i.DefinitionId == piece.DefinitionId));
        Assert.InRange(prepared.MaximumDeviationMm, 0m, .025m);
        Assert.All(prepared.Conversion.SelectMany(c => c.Segments),
            s => Assert.InRange(s.MaximumDeviationMm, 0m, .025m));
    }

    [Theory]
    [InlineData(7, "grading_workload_capacity")]
    [InlineData(0, "grading_workload_not_configured")]
    public void InsufficientOrMisconfiguredAggregateBudgetRejectsPreparation(long maximum, string expectedCode)
    {
        var (approved, request) = Fixture();
        var limits = new GradingPolygonizationLimits(24, 20000) { MaximumTotalVertices = maximum };

        var error = Assert.Throws<ModelingPersistenceException>(() => new GradingNestingProblemBuilder(new(limits), limits)
            .Build(new("synthetic-company", "synthetic-owner"), Guid.NewGuid(), approved, request));

        Assert.Equal(expectedCode, error.Code);
    }

    private static (ApprovedGradingDocument Approved, GradingNestingPrepareRequest Request) Fixture()
    {
        var document = SyntheticGeometry.Rectangle();
        var calculation = new GradingEngine().Calculate(document, new(24, 20000));
        Assert.True(calculation.IsApprovable);
        var modelId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var configuration = new ModelingConfiguration(document, [], new());
        var approval = new GradingApprovalSnapshot(Guid.NewGuid(), resultId, modelId,
            "synthetic-approver", DateTimeOffset.UnixEpoch);
        var approved = new ApprovedGradingDocument(1, modelId, revisionId, resultId, configuration, calculation, approval);
        var request = new GradingNestingPrepareRequest(Guid.NewGuid(), resultId,
            [new("piece", "base", 2), new("piece", "large", 3)], 120, 30, [0, 90], false);
        return (approved, request);
    }
}
