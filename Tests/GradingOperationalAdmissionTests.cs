using System.Text.Json;
using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Core;
using GestionProduccion.Grading.Persistence;
using GestionProduccion.Shared.ModelingGrading;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingOperationalAdmissionTests
{
    [Fact]
    public void WorkloadAtEveryConfiguredBoundaryIsAccepted()
    {
        var geometry = SyntheticGeometry.Rectangle();
        var bytes = 4096L;

        var error = Record.Exception(() => GradingWorkloadValidation.RequireWithin(geometry, bytes, LimitsFor(geometry, bytes)));

        Assert.Null(error);
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("pieces")]
    [InlineData("sizes")]
    [InlineData("nodes")]
    [InlineData("rules")]
    [InlineData("derived-nodes")]
    public void WorkloadJustOverEachConfiguredBoundaryIsRejected(string dimension)
    {
        var geometry = dimension == "pieces" ? GradingOperationalFixtures.Create("rectangle", 2, 4) : SyntheticGeometry.Rectangle();
        const long bytes = 4096;
        var limits = LimitsFor(geometry, bytes);
        limits = dimension switch
        {
            "bytes" => limits with { MaximumArtifactBytes = bytes - 1 },
            "pieces" => limits with { MaximumPieces = geometry.Pieces.Length - 1 },
            "sizes" => limits with { MaximumSizes = geometry.SizeSet.Sizes.Length - 1 },
            "nodes" => limits with { MaximumNodes = geometry.Pieces.Sum(p => p.MaterialContours.Sum(c => c.Nodes.Length)) - 1 },
            "rules" => limits with { MaximumRules = geometry.Rules.Length - 1 },
            "derived-nodes" => limits with { MaximumDerivedNodes = DerivedNodes(geometry) - 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(dimension))
        };

        var error = Assert.Throws<ModelingPersistenceException>(() =>
            GradingWorkloadValidation.RequireWithin(geometry, bytes, limits));

        Assert.Equal("grading_workload_capacity", error.Code);
    }

    [Theory]
    [InlineData("rules", 0)]
    [InlineData("rules", -1)]
    [InlineData("derived-nodes", 0)]
    [InlineData("derived-nodes", -1)]
    public void InvalidNewLimitsAreReportedAsMisconfiguration(string dimension, long value)
    {
        var geometry = SyntheticGeometry.Rectangle();
        const long bytes = 4096;
        var limits = LimitsFor(geometry, bytes);
        limits = dimension == "rules"
            ? limits with { MaximumRules = value }
            : limits with { MaximumDerivedNodes = value };

        var error = Assert.Throws<ModelingPersistenceException>(() =>
            GradingWorkloadValidation.RequireWithin(geometry, bytes, limits));

        Assert.Equal("grading_workload_not_configured", error.Code);
    }

    [Fact]
    public async Task ImportOverWorkloadLimitPublishesNoArtifactsOrPersistenceRows()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var blobs = new PrivateArtifactFixture();
        var geometry = GradingOperationalFixtures.Create("rectangle", 2, 1);
        var input = JsonSerializer.SerializeToUtf8Bytes(new ModelingImportDocument(1, geometry,
            new("source-model", "source-revision", "source-result", "source-owner", "source-company", "source-approver", DateTimeOffset.UnixEpoch)));
        var limits = new GradingWorkloadLimits(1024 * 1024, 1, 1, 100, 24, 20_000)
        {
            MaximumDerivedNodes = 100,
            MaximumRules = 100
        };
        var service = new ModelingImportService(new ModelingImportRepository(fixture, TimeProvider.System),
            new ModelingQueries(fixture), new ModelingArtifactStore(blobs, 1024 * 1024), new GeometryProofBudget(24, 20_000), limits);

        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() =>
            service.ImportAsync(PersistenceFixture.Scope, Guid.NewGuid(), input));

        Assert.Equal("grading_workload_capacity", error.Code);
        Assert.Equal(0, blobs.Writes);
        Assert.Empty(blobs.Bytes);
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.Documents.ToArrayAsync());
        Assert.Empty(await db.Revisions.ToArrayAsync());
        Assert.Empty(await db.Jobs.ToArrayAsync());
        Assert.Empty(await db.Operations.ToArrayAsync());
    }

    private static GradingWorkloadLimits LimitsFor(InternalGeometryDocument geometry, long bytes)
        => new(bytes, geometry.Pieces.Length, geometry.SizeSet.Sizes.Length,
            geometry.Pieces.Sum(p => p.MaterialContours.Sum(c => c.Nodes.Length)), 24, 20_000)
        {
            MaximumDerivedNodes = DerivedNodes(geometry),
            MaximumRules = geometry.Rules.Length
        };

    private static long DerivedNodes(InternalGeometryDocument geometry)
        => geometry.Pieces.Sum(p => p.MaterialContours.Sum(c => (long)c.Nodes.Length) * p.AvailableSizeIds.Length);
}
