using System.Collections.Immutable;
using System.Text.Json;
using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Core;
using GestionProduccion.Grading.Persistence;
using GestionProduccion.Shared.ModelingGrading;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingNestingPreparationTests
{
    private const long ArtifactLimit = 1024 * 1024;

    [Fact]
    public async Task ApprovedRichCurveProducesOnlyRequestedGradedInstancesAndPreservesSource()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var blobs = new PrivateArtifactFixture();
        var imported = await Import(fixture, blobs, SyntheticGeometry.Curved());
        var approved = await Approve(fixture, blobs, imported.Document);
        var originalBytes = blobs.Bytes[imported.Document.Original.Key].ToArray();
        var configurationBefore = await new ModelingArtifactStore(blobs, ArtifactLimit)
            .ReadAsync<ModelingConfiguration>((await new ModelingQueries(fixture).DraftAsync(imported.Document.Scope, imported.Document.Id))!.Artifact);
        var piece = configurationBefore.Configuration.Pieces.Single();
        var baseSize = configurationBefore.Configuration.SizeSet.BaseSizeId;
        var largeSize = LocalId(configurationBefore, "size", "large");
        var request = Request([new(piece.Id, baseSize, 2), new(piece.Id, largeSize, 3)]);

        var prepared = await Preparation(fixture, blobs).PrepareAsync(imported.Document.Scope, imported.Document.Id, request);

        Assert.Equal(approved.Id, prepared.Source.ResultId);
        Assert.Equal(imported.Document.Scope.OwnerId, prepared.Source.OwnerId);
        Assert.Equal(imported.Document.Scope.CompanyId, prepared.Source.CompanyId);
        Assert.Equal(new[] { (piece.Id, baseSize, 2), (piece.Id, largeSize, 3) },
            prepared.Pieces.Select(p => (p.PieceId, p.SizeId, p.Quantity)).ToArray());
        Assert.Equal(5, prepared.Request.Problem.Instances.Count);
        Assert.Equal(2, prepared.Request.Problem.Definitions.Count);
        Assert.Equal(2, prepared.Labels.Count);
        Assert.All(prepared.Pieces, p => Assert.Equal(piece.Provenance, p.Provenance));
        foreach (var trace in prepared.Pieces)
        {
            var expectedIds = Enumerable.Range(1, trace.Quantity)
                .Select(copy => $"{trace.DefinitionId}-instance-{copy:D4}");
            Assert.Equal(expectedIds, prepared.Request.Problem.Instances
                .Where(instance => instance.DefinitionId == trace.DefinitionId).Select(instance => instance.Id));
        }
        Assert.All(prepared.Request.Problem.Instances, instance =>
            Assert.Contains(prepared.Request.Problem.Definitions, definition => definition.Id == instance.DefinitionId));
        Assert.All(prepared.Conversion, conversion => Assert.NotEmpty(conversion.Segments));
        Assert.Contains(configurationBefore.Configuration.Pieces.Single().MaterialContours.Single().Segments,
            segment => segment.Kind == MaterialSegmentKind.CubicBezier);
        Assert.NotEmpty(configurationBefore.Configuration.Pieces.Single().MaterialContours.Single().ControlSpans);
        Assert.Equal(imported.InputBytes, blobs.Bytes[imported.Document.Original.Key]);
        Assert.Equal(originalBytes, blobs.Bytes[imported.Document.Original.Key]);
        var configurationAfter = await new ModelingArtifactStore(blobs, ArtifactLimit)
            .ReadAsync<ModelingConfiguration>((await new ModelingQueries(fixture).DraftAsync(imported.Document.Scope, imported.Document.Id))!.Artifact);
        Assert.Equal(ModelingJson.Encode(configurationBefore), ModelingJson.Encode(configurationAfter));
        Assert.Single(configurationAfter.UnassociatedSourceContent);
        Assert.DoesNotContain(prepared.Pieces, p => p.PieceId == configurationAfter.UnassociatedSourceContent[0].Id);
    }

    [Fact]
    public async Task ExactAvailableSizeIsUsedAndUnavailableBaseNeverSubstitutes()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var blobs = new PrivateArtifactFixture();
        var document = SyntheticGeometry.Rectangle() with
        {
            Pieces = [SyntheticGeometry.Rectangle().Pieces[0] with { AvailableSizeIds = ["xl"] }]
        };
        var imported = await Import(fixture, blobs, document);
        await Approve(fixture, blobs, imported.Document);
        var configuration = await Configuration(fixture, blobs, imported.Document);
        var localPiece = configuration.Configuration.Pieces.Single();
        var xlSize = LocalId(configuration, "size", "xl");
        var service = Preparation(fixture, blobs);

        var prepared = await service.PrepareAsync(imported.Document.Scope, imported.Document.Id,
            Request([new(localPiece.Id, xlSize, 2)]));
        Assert.Equal(xlSize, Assert.Single(prepared.Pieces).SizeId);
        Assert.Equal(2, prepared.Request.Problem.Instances.Count);

        var handoffsBefore = await HandoffCount(fixture);
        var writesBefore = blobs.Writes;
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => service.PrepareAsync(
            imported.Document.Scope, imported.Document.Id, Request([new(localPiece.Id, configuration.Configuration.SizeSet.BaseSizeId, 1)])));
        Assert.Equal("grading_nesting_size_unavailable", error.Code);
        Assert.Equal(handoffsBefore, await HandoffCount(fixture));
        Assert.Equal(writesBefore, blobs.Writes);
    }

    [Fact]
    public async Task SelectionRequiresApprovalAndNewPreparationRejectsArchivedModel()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var blobs = new PrivateArtifactFixture();
        var imported = await Import(fixture, blobs, SyntheticGeometry.Rectangle());
        var configuration = await Configuration(fixture, blobs, imported.Document);
        var localPiece = configuration.Configuration.Pieces.Single();
        var service = Preparation(fixture, blobs);
        var beforeApproval = await Assert.ThrowsAsync<ModelingPersistenceException>(() => service.PrepareAsync(
            imported.Document.Scope, imported.Document.Id, Request([new(localPiece.Id, configuration.Configuration.SizeSet.BaseSizeId, 1)])));
        Assert.Equal("modeling_result_not_approvable", beforeApproval.Code);

        await Approve(fixture, blobs, imported.Document);
        await new ModelingLifecycleRepository(fixture, TimeProvider.System)
            .SetArchivedAsync(imported.Document.Scope, imported.Document.Id, true);
        var archived = await Assert.ThrowsAsync<ModelingPersistenceException>(() => service.PrepareAsync(
            imported.Document.Scope, imported.Document.Id, Request([new(localPiece.Id, configuration.Configuration.SizeSet.BaseSizeId, 1)])));
        Assert.Equal("modeling_archived", archived.Code);
        Assert.Equal(0, await HandoffCount(fixture));
    }

    [Fact]
    public async Task HistoricalApprovalAndOperationReplayStayPinnedAfterCurrentApprovalChanges()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var blobs = new PrivateArtifactFixture();
        var imported = await Import(fixture, blobs, SyntheticGeometry.Rectangle());
        var first = await Approve(fixture, blobs, imported.Document);
        var configuration = await Configuration(fixture, blobs, imported.Document);
        var localPiece = configuration.Configuration.Pieces.Single();
        var service = Preparation(fixture, blobs);
        var request = Request([new(localPiece.Id, configuration.Configuration.SizeSet.BaseSizeId, 1)], first.Id);
        var prepared = await service.PrepareAsync(imported.Document.Scope, imported.Document.Id, request);

        var current = await Approve(fixture, blobs, imported.Document);
        Assert.NotEqual(first.Id, current.Id);
        Assert.Equal(current.Id, (await new ModelingQueries(fixture).DocumentAsync(imported.Document.Scope, imported.Document.Id))!.CurrentApprovedResultId);
        var replay = await service.PrepareAsync(imported.Document.Scope, imported.Document.Id, request);

        Assert.Equal(first.Id, prepared.Source.ResultId);
        Assert.Equal(first.Id, replay.Source.ResultId);
        Assert.Equal(prepared.Source, replay.Source);
        Assert.Equal(ModelingJson.Encode(prepared.Request), ModelingJson.Encode(replay.Request));
        Assert.Equal(1, await HandoffCount(fixture));
    }

    [Theory]
    [InlineData("foreign-company", "owner-a")]
    [InlineData("company-a", "foreign-owner")]
    public async Task ForeignCompanyOrOwnerGetsScopedNotFound(string companyId, string ownerId)
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var blobs = new PrivateArtifactFixture();
        var imported = await Import(fixture, blobs, SyntheticGeometry.Rectangle());
        await Approve(fixture, blobs, imported.Document);
        var configuration = await Configuration(fixture, blobs, imported.Document);
        var localPiece = configuration.Configuration.Pieces.Single();
        var foreign = new ModelingScope(companyId, ownerId);

        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => Preparation(fixture, blobs)
            .PrepareAsync(foreign, imported.Document.Id, Request([new(localPiece.Id, configuration.Configuration.SizeSet.BaseSizeId, 1)])));

        Assert.Equal("modeling_not_found", error.Code);
        Assert.Equal(0, await HandoffCount(fixture));
    }

    [Theory]
    [InlineData("multiple")]
    [InlineData("unsupported")]
    public async Task BuilderRejectsUnsupportedApprovedContourBeforeReturningAnyPreparedProblem(string shape)
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var blobs = new PrivateArtifactFixture();
        var imported = await Import(fixture, blobs, SyntheticGeometry.Rectangle());
        var result = await Approve(fixture, blobs, imported.Document);
        var approved = (await Api(fixture, blobs).ApprovedAsync(imported.Document.Scope, imported.Document.Id, result.Id))!;
        var configuration = approved.Configuration.Configuration;
        var piece = configuration.Pieces.Single();
        var contour = piece.MaterialContours.Single();
        var contours = shape == "multiple"
            ? ImmutableArray.Create(contour, contour with { Id = "second-contour" })
            : ImmutableArray.Create(contour with { Kind = MaterialContourKind.Unsupported });
        approved = approved with
        {
            Configuration = approved.Configuration with
            {
                Configuration = configuration with { Pieces = [piece with { MaterialContours = contours }] }
            }
        };
        var request = Request([new(piece.Id, approved.Configuration.Configuration.SizeSet.BaseSizeId, 1)]);
        var beforeHandoffs = await HandoffCount(fixture);
        var beforeWrites = blobs.Writes;

        var error = Assert.Throws<ModelingPersistenceException>(() => new GradingNestingProblemBuilder(
            new GradingPolygonizer(new(24, 20000))).Build(imported.Document.Scope, Guid.NewGuid(), approved, request));

        Assert.Equal("grading_nesting_contour_unsupported", error.Code);
        Assert.Equal(beforeHandoffs, await HandoffCount(fixture));
        Assert.Equal(beforeWrites, blobs.Writes);
    }

    [Fact]
    public async Task LaterInvalidDemandCannotPublishPartialHandoffOrArtifact()
    {
        await using var fixture = await PersistenceFixture.CreateAsync();
        var blobs = new PrivateArtifactFixture();
        var imported = await Import(fixture, blobs, SyntheticGeometry.Rectangle());
        await Approve(fixture, blobs, imported.Document);
        var configuration = await Configuration(fixture, blobs, imported.Document);
        var localPiece = configuration.Configuration.Pieces.Single();
        var handoffsBefore = await HandoffCount(fixture);
        var writesBefore = blobs.Writes;

        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => Preparation(fixture, blobs)
            .PrepareAsync(imported.Document.Scope, imported.Document.Id,
                Request([new(localPiece.Id, configuration.Configuration.SizeSet.BaseSizeId, 1),
                    new("zzz-missing-piece", configuration.Configuration.SizeSet.BaseSizeId, 1)])));

        Assert.Equal("grading_nesting_size_unavailable", error.Code);
        Assert.Equal(handoffsBefore, await HandoffCount(fixture));
        Assert.Equal(writesBefore, blobs.Writes);
    }

    private static GradingNestingPrepareRequest Request(ImmutableArray<GradingNestingDemand> demands, Guid? resultId = null)
        => new(Guid.NewGuid(), resultId, demands, 120, 30, [0, 90], false);

    private static async Task<(ModelingDocumentSnapshot Document, byte[] InputBytes)> Import(
        PersistenceFixture fixture, PrivateArtifactFixture blobs, InternalGeometryDocument geometry)
    {
        var input = JsonSerializer.SerializeToUtf8Bytes(new ModelingImportDocument(1, geometry,
            new("source-model", "source-revision", "source-result", "source-owner", "source-company", "source-approver", DateTimeOffset.UtcNow),
            [new("unassociated-note", "source_note", SyntheticGeometry.Provenance)]));
        var document = await new ModelingImportService(new ModelingImportRepository(fixture, TimeProvider.System),
            new ModelingQueries(fixture), new ModelingArtifactStore(blobs, ArtifactLimit), new(24, 20000))
            .ImportAsync(PersistenceFixture.Scope, Guid.NewGuid(), input.AsMemory());
        return (document, input);
    }

    private static async Task<GradingResultSnapshot> Approve(PersistenceFixture fixture, PrivateArtifactFixture blobs,
        ModelingDocumentSnapshot document)
    {
        var queries = new ModelingQueries(fixture);
        var drafts = new ModelingDraftRepository(fixture, TimeProvider.System, new(10, TimeSpan.FromMinutes(1)));
        var current = (await queries.DraftAsync(document.Scope, document.Id))!;
        var job = await drafts.RequestAsync(document.Scope, document.Id, current.Version, Guid.NewGuid());
        await using (var db = fixture.CreateDbContext())
        {
            var row = await db.Jobs.SingleAsync(j => j.Id == job.JobId);
            row.State = GradingJobState.Running;
            row.Version = 2;
            await db.SaveChangesAsync();
        }
        var revision = (await queries.RevisionAsync(document.Scope, job.RevisionId))!;
        var store = new ModelingArtifactStore(blobs, ArtifactLimit);
        var configuration = await store.ReadAsync<ModelingConfiguration>(revision.Artifact);
        var calculation = new GradingEngine().Calculate(configuration.Configuration, new(24, 20000));
        Assert.True(calculation.IsApprovable);
        var result = await new GradingResultRepository(fixture, TimeProvider.System).CompleteAsync(document.Scope,
            job.JobId, 2, true, await store.WriteDocumentAsync(new StoredGradingResult(revision.Id, revision.Artifact.Sha256, calculation)));
        await new GradingResultRepository(fixture, TimeProvider.System).ApproveAsync(document.Scope, result.Id, Guid.NewGuid());
        return result;
    }

    private static async Task<ModelingConfiguration> Configuration(PersistenceFixture fixture, PrivateArtifactFixture blobs,
        ModelingDocumentSnapshot document)
    {
        var draft = (await new ModelingQueries(fixture).DraftAsync(document.Scope, document.Id))!;
        return await new ModelingArtifactStore(blobs, ArtifactLimit).ReadAsync<ModelingConfiguration>(draft.Artifact);
    }

    private static string LocalId(ModelingConfiguration configuration, string kind, string sourceId)
        => configuration.SourceIdentities.Single(identity => identity.Kind == kind && identity.SourceId == sourceId).LocalId;

    private static GradingNestingPreparationService Preparation(PersistenceFixture fixture, PrivateArtifactFixture blobs)
    {
        var queries = new ModelingQueries(fixture);
        var store = new ModelingArtifactStore(blobs, ArtifactLimit);
        return new(queries, Api(fixture, blobs), new(new GradingPolygonizer(new(24, 20000))),
            new GradingNestingHandoffRepository(fixture, TimeProvider.System), store);
    }

    private static ModelingApiQueries Api(PersistenceFixture fixture, PrivateArtifactFixture blobs)
    {
        var queries = new ModelingQueries(fixture);
        return new(queries, new ModelingDocumentList(fixture), new ModelingArtifactStore(blobs, ArtifactLimit),
            new ModelingJobList(fixture));
    }

    private static async Task<int> HandoffCount(PersistenceFixture fixture)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Handoffs.CountAsync();
    }
}
