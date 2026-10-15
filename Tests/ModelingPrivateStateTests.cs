using System.Collections.Immutable;
using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Core;
using GestionProduccion.Grading.Persistence;
using GestionProduccion.Shared.ModelingGrading;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Tests;

public sealed class ModelingPrivateStateTests
{
    [Fact]
    public async Task InvalidReportPreservesDiagnosticsAndNeverCreatesAnApproval()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture();
        var doc = await PrivateModelingImportTests.Service(f, blobs).ImportAsync(PersistenceFixture.Scope, Guid.NewGuid(), PrivateModelingImportTests.Input());
        var q = new ModelingQueries(f); var store = new ModelingArtifactStore(blobs, 1024 * 1024);
        var job = await new ModelingDraftRepository(f, TimeProvider.System, new(10, TimeSpan.FromMinutes(1))).RequestAsync(doc.Scope, doc.Id, 1, Guid.NewGuid());
        await using (var db = f.CreateDbContext()) { var row = await db.Jobs.SingleAsync(); row.State = GradingJobState.Running; row.Version = 2; await db.SaveChangesAsync(); }
        var revision = (await q.RevisionAsync(doc.Scope, job.RevisionId))!;
        var report = new StoredGradingResult(revision.Id, revision.Artifact.Sha256,
            new([], [new("grading_geometry_invalid", "synthetic-piece", "synthetic-size", "synthetic-contour")], []));
        var repo = new GradingResultRepository(f, TimeProvider.System);
        var result = await new GradingResultPublicationService(repo, q, store).PublishAsync(doc.Scope, job.JobId, 2, report);
        Assert.False(result.IsValid); Assert.Equal(GradingJobState.Failed, (await q.JobAsync(doc.Scope, job.JobId))!.State);
        var stored = await store.ReadAsync<StoredGradingResult>(result.Artifact);
        Assert.Equal("synthetic-contour", Assert.Single(stored.Calculation.Diagnostics).ContourId);
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => repo.ApproveAsync(doc.Scope, result.Id, Guid.NewGuid()));
        Assert.Empty(await q.ApprovalsAsync(doc.Scope, doc.Id));
    }

    [Fact]
    public async Task DraftRejectsBaseEditingButPersistsExplicitRulesAndPinsCalculation()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture();
        var doc = await PrivateModelingImportTests.Service(f, blobs).ImportAsync(PersistenceFixture.Scope, Guid.NewGuid(), PrivateModelingImportTests.Input());
        var q = new ModelingQueries(f); var store = new ModelingArtifactStore(blobs, 1024 * 1024);
        var source = await store.ReadAsync<ModelingConfiguration>((await q.DraftAsync(doc.Scope, doc.Id))!.Artifact);
        var service = new ModelingDraftService(new ModelingDraftRepository(f, TimeProvider.System, new(10, TimeSpan.FromMinutes(1))), q, store,
            new(1024 * 1024, 10, 10, 1000, 24, 20000));
        var piece = source.Configuration.Pieces[0]; var contour = piece.MaterialContours[0];
        var changed = source.Configuration with { Pieces = [piece with { MaterialContours = [contour with {
            Nodes = contour.Nodes.SetItem(0, contour.Nodes[0] with { Position = new(1, 1) }) }] }] };
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => service.SaveAsync(doc.Scope, doc.Id, 1, changed));
        var missing = source.Configuration with { Rules = [] };
        var draft = await service.SaveAsync(doc.Scope, doc.Id, 1, missing);
        var invalid = await Assert.ThrowsAsync<ModelingPersistenceException>(() => service.RequestAsync(doc.Scope, doc.Id, draft.Version, Guid.NewGuid()));
        Assert.NotEmpty(invalid.Diagnostics);
        var saved = await service.SaveAsync(doc.Scope, doc.Id, draft.Version, source.Configuration);
        var op = Guid.NewGuid(); var job = await service.RequestAsync(doc.Scope, doc.Id, saved.Version, op);
        await service.SaveAsync(doc.Scope, doc.Id, saved.Version, missing);
        Assert.Equal(job.JobId, (await service.RequestAsync(doc.Scope, doc.Id, saved.Version, op)).JobId);
        Assert.Equal(saved.Artifact.Sha256, (await q.RevisionAsync(doc.Scope, job.RevisionId))!.Artifact.Sha256);
    }

    [Fact]
    public async Task PublicationPinsSourceRejectsPartialResultsAndReplaysWithoutNewStorageWrites()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture();
        var doc = await PrivateModelingImportTests.Service(f, blobs).ImportAsync(PersistenceFixture.Scope, Guid.NewGuid(), PrivateModelingImportTests.Input());
        var q = new ModelingQueries(f); var store = new ModelingArtifactStore(blobs, 1024 * 1024);
        var job = await new ModelingDraftRepository(f, TimeProvider.System, new(10, TimeSpan.FromMinutes(1))).RequestAsync(doc.Scope, doc.Id, 1, Guid.NewGuid());
        await using (var db = f.CreateDbContext()) { var row = await db.Jobs.SingleAsync(); row.State = GradingJobState.Running; row.Version = 2; await db.SaveChangesAsync(); }
        var revision = (await q.RevisionAsync(doc.Scope, job.RevisionId))!;
        var config = await store.ReadAsync<ModelingConfiguration>(revision.Artifact);
        var calculated = new GradingEngine().Calculate(config.Configuration, new(24, 20000));
        Assert.True(calculated.IsApprovable);
        var report = new StoredGradingResult(revision.Id, revision.Artifact.Sha256, calculated);
        var service = new GradingResultPublicationService(new GradingResultRepository(f, TimeProvider.System), q, store);
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => service.PublishAsync(doc.Scope, job.JobId, 2, report with { RevisionId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => service.PublishAsync(doc.Scope, job.JobId, 2, report with {
            Calculation = calculated with { Geometry = calculated.Geometry.RemoveAt(0) } }));
        var result = await service.PublishAsync(doc.Scope, job.JobId, 2, report); var writes = blobs.Writes;
        Assert.Equal(result.Id, (await service.PublishAsync(doc.Scope, job.JobId, 2, report)).Id); Assert.Equal(writes, blobs.Writes);
        Assert.True(result.IsValid); Assert.Equal(revision.Id, result.RevisionId);
        var persisted = await store.ReadAsync<StoredGradingResult>(result.Artifact);
        Assert.Equal(calculated.Geometry.Length, persisted.Calculation.Geometry.Length);
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => service.PublishAsync(new(doc.Scope.CompanyId, "foreign"), job.JobId, 2, report));
        blobs.Bytes[result.Artifact.Key][0] ^= 1;
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => store.ReadAsync<StoredGradingResult>(result.Artifact));
    }
}
