using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Core;
using GestionProduccion.Grading.Persistence;
using GestionProduccion.Shared.ModelingGrading;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Tests;

public sealed class ModelingApiExportTests
{
    [Fact]
    public async Task ApprovedExportPreservesCompleteConfigurationAndAuthoritativeBezierGeometry()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture();
        var doc = await Import(f, blobs); var result = await Approve(f, blobs, doc);
        var q = new ModelingQueries(f); var store = new ModelingArtifactStore(blobs, 1024 * 1024);
        var revision = (await q.RevisionAsync(doc.Scope, result.RevisionId))!;
        var configuration = await store.ReadAsync<ModelingConfiguration>(revision.Artifact);
        var stored = await store.ReadAsync<StoredGradingResult>(result.Artifact);
        var export = (await Api(f, blobs).ApprovedAsync(doc.Scope, doc.Id, null))!;

        Assert.Equal(revision.Id, export.RevisionId); Assert.Equal(result.Id, export.ResultId);
        Assert.Equal(ModelingJson.Encode(configuration), ModelingJson.Encode(export.Configuration));
        Assert.Equal(ModelingJson.Encode(stored.Calculation), ModelingJson.Encode(export.Calculation));
        Assert.Single(export.Configuration.UnassociatedSourceContent);
        Assert.NotEmpty(export.Configuration.Configuration.Rules);
        var contour = export.Calculation.Geometry.Single(g => g.SizeId == export.Configuration.Configuration.SizeSet.BaseSizeId).Contour;
        Assert.Contains(contour.Segments, s => s.Kind == MaterialSegmentKind.CubicBezier);
        Assert.Contains(contour.Nodes, n => n.IncomingHandle != default || n.OutgoingHandle != default);
        Assert.NotEmpty(contour.ControlSpans);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ExportRejectsStoredResultNotBoundToExactRevisionAndConfigurationHash(bool wrongRevision)
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture();
        var doc = await Import(f, blobs);
        await Approve(f, blobs, doc, wrongRevision ? "revision" : "hash");
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => Api(f, blobs).ApprovedAsync(doc.Scope, doc.Id, null));
        Assert.Equal("modeling_artifact_invalid", error.Code);
    }

    [Fact]
    public async Task ExplicitHistoricalApprovalRemainsExportableAfterCurrentApprovalChanges()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture();
        var doc = await Import(f, blobs); var first = await Approve(f, blobs, doc); var second = await Approve(f, blobs, doc);
        var api = Api(f, blobs);
        Assert.Equal(second.Id, (await api.ApprovedAsync(doc.Scope, doc.Id, null))!.ResultId);
        var historical = (await api.ApprovedAsync(doc.Scope, doc.Id, first.Id))!;
        Assert.Equal(first.Id, historical.ResultId); Assert.Equal(first.RevisionId, historical.RevisionId);
        Assert.Equal(first.Id, historical.Approval.ResultId);
        Assert.Equal(2, (await api.ApprovalHistoryAsync(doc.Scope, doc.Id)).Count);
        Assert.Null(await api.ApprovedAsync(doc.Scope, doc.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task ForeignScopeCannotLoadPrivateArtifactsThroughMetadataQueries()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture();
        var doc = await Import(f, blobs); var result = await Approve(f, blobs, doc);
        // A metadata leak would try opening a missing blob and fail instead of returning the scoped response.
        blobs.Bytes.Clear(); var api = Api(f, blobs);
        foreach (var scope in new[] { new ModelingScope("foreign", doc.Scope.OwnerId), new(doc.Scope.CompanyId, "foreign") })
        {
            Assert.Empty(await api.ListAsync(scope, false, 0, 10));
            Assert.Null(await api.DocumentAsync(scope, doc.Id)); Assert.Null(await api.DraftAsync(scope, doc.Id));
            Assert.Null(await api.RevisionAsync(scope, result.RevisionId)); Assert.Null(await api.ResultAsync(scope, result.Id));
            Assert.Null(await api.ResultForJobAsync(scope, result.JobId));
            var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => api.ApprovedAsync(scope, doc.Id, result.Id));
            Assert.Equal("modeling_not_found", error.Code);
        }
    }

    [Fact]
    public async Task ImportingApprovedExportCreatesLocalIdentitiesAndRequiresLocalApproval()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture();
        var source = await Import(f, blobs); await Approve(f, blobs, source);
        var export = (await Api(f, blobs).ApprovedAsync(source.Scope, source.Id, null))!;
        var scope = new ModelingScope("target-company", "target-owner");
        var imported = await PrivateModelingImportTests.Service(f, blobs).ImportAsync(scope, Guid.NewGuid(), ModelingJson.Encode(export));
        var q = new ModelingQueries(f); var draft = (await q.DraftAsync(scope, imported.Id))!;
        var config = await new ModelingArtifactStore(blobs, 1024 * 1024).ReadAsync<ModelingConfiguration>(draft.Artifact);
        Assert.NotEqual(source.Id, imported.Id); Assert.Equal(scope, imported.Scope); Assert.Null(imported.CurrentApprovedResultId);
        Assert.NotEqual(export.Configuration.Configuration.Pieces[0].Id, config.Configuration.Pieces[0].Id);
        Assert.Equal(export.ModelId.ToString("D"), config.SourceDocument.ModelId);
        Assert.Equal(export.RevisionId.ToString("D"), config.SourceDocument.RevisionId);
        Assert.Equal(export.ResultId.ToString("D"), config.SourceDocument.ResultId);
        Assert.Equal(export.Approval.ApprovedBy, config.SourceDocument.ApprovedBy);
        Assert.Equal(export.Approval.ApprovedAt, config.SourceDocument.ApprovedAt);
        Assert.Single(config.UnassociatedSourceContent); Assert.Empty(await q.ApprovalsAsync(scope, imported.Id));
        Assert.Null(await Api(f, blobs).ApprovedAsync(scope, imported.Id, null));
        Assert.All(await q.RevisionsAsync(scope, imported.Id), r => Assert.NotEqual(export.RevisionId, r.Id));
    }

    [Theory]
    [InlineData("model")] [InlineData("result")] [InlineData("invalid")]
    public async Task MalformedSourceApprovalCannotAuthorizeOrPublishAnImport(string mismatch)
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture();
        var doc = await Import(f, blobs); await Approve(f, blobs, doc);
        var export = (await Api(f, blobs).ApprovedAsync(doc.Scope, doc.Id, null))!;
        var malformed = mismatch switch
        {
            "model" => export with { Approval = export.Approval with { ModelId = Guid.NewGuid() } },
            "result" => export with { Approval = export.Approval with { ResultId = Guid.NewGuid() } },
            _ => export with { Calculation = export.Calculation with { Diagnostics = [new("grading_geometry_invalid")] } }
        };
        var writes = blobs.Writes; var target = new ModelingScope("target", "target");
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => PrivateModelingImportTests.Service(f, blobs)
            .ImportAsync(target, Guid.NewGuid(), ModelingJson.Encode(malformed)));
        Assert.Equal("modeling_import_invalid", error.Code); Assert.Equal(writes, blobs.Writes);
        Assert.Empty(await Api(f, blobs).ListAsync(target, false, 0, 10));
    }

    private static ModelingApiQueries Api(PersistenceFixture f, PrivateArtifactFixture blobs)
        => new(new ModelingQueries(f), new ModelingDocumentList(f), new(blobs, 1024 * 1024), new ModelingJobList(f));

    private static Task<ModelingDocumentSnapshot> Import(PersistenceFixture f, PrivateArtifactFixture blobs)
        => PrivateModelingImportTests.Service(f, blobs).ImportAsync(PersistenceFixture.Scope, Guid.NewGuid(),
            ModelingJson.Encode(new ModelingImportDocument(1, SyntheticGeometry.Curved(), new("synthetic-source"),
                [new("unassociated", "source_not_associated", SyntheticGeometry.Provenance)])));

    private static async Task<GradingResultSnapshot> Approve(PersistenceFixture f, PrivateArtifactFixture blobs,
        ModelingDocumentSnapshot doc, string? mismatch = null)
    {
        var q = new ModelingQueries(f); var store = new ModelingArtifactStore(blobs, 1024 * 1024);
        var job = await new ModelingDraftRepository(f, TimeProvider.System, new(10, TimeSpan.FromMinutes(1)))
            .RequestAsync(doc.Scope, doc.Id, 1, Guid.NewGuid());
        await using (var db = f.CreateDbContext())
        {
            var row = await db.Jobs.SingleAsync(j => j.Id == job.JobId); row.State = GradingJobState.Running;
            row.Version = 2; await db.SaveChangesAsync();
        }
        var revision = (await q.RevisionAsync(doc.Scope, job.RevisionId))!;
        var config = await store.ReadAsync<ModelingConfiguration>(revision.Artifact);
        var calculation = new GradingEngine().Calculate(config.Configuration, new(24, 20000));
        Assert.True(calculation.IsApprovable);
        var stored = new StoredGradingResult(mismatch == "revision" ? Guid.NewGuid() : revision.Id,
            mismatch == "hash" ? new string('0', 64) : revision.Artifact.Sha256, calculation);
        var repo = new GradingResultRepository(f, TimeProvider.System);
        // Persist adversarial reports through the repository to exercise the export's independent binding check.
        var result = await repo.CompleteAsync(doc.Scope, job.JobId, 2, true, await store.WriteDocumentAsync(stored));
        await repo.ApproveAsync(doc.Scope, result.Id, Guid.NewGuid()); return result;
    }
}
