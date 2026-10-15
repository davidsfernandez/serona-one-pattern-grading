using System.Text.Json;
using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Tests;

public sealed class PrivateModelingImportTests
{
    internal static byte[] Input() => JsonSerializer.SerializeToUtf8Bytes(new ModelingImportDocument(1, SyntheticGeometry.Rectangle(),
        new("source-model", "source-revision", "source-result", "source-owner", "source-company", "source-approver", DateTimeOffset.UtcNow)));
    internal static ModelingImportService Service(PersistenceFixture f, PrivateArtifactFixture blobs) => new(
        new ModelingImportRepository(f, TimeProvider.System), new ModelingQueries(f), new(blobs, 1024 * 1024), new(24, 20000));

    [Fact] public async Task ImportPreservesOriginalBytesAndCreatesNewLocalIdentitiesWithoutApprovalTransfer()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture(); var bytes = Input();
        var doc = await Service(f, blobs).ImportAsync(PersistenceFixture.Scope, Guid.NewGuid(), bytes.AsMemory());
        Assert.Equal(PersistenceFixture.Scope, doc.Scope); Assert.Null(doc.CurrentApprovedResultId); Assert.Equal(bytes, blobs.Bytes[doc.Original.Key]);
        var draft = await new ModelingQueries(f).DraftAsync(doc.Scope, doc.Id);
        var config = await new ModelingArtifactStore(blobs, 1024 * 1024).ReadAsync<ModelingConfiguration>(draft!.Artifact);
        Assert.NotEqual("piece", config.Configuration.Pieces[0].Id);
        Assert.Contains(config.SourceIdentities, i => i.Kind == "piece" && i.SourceId == "piece" && i.LocalId == config.Configuration.Pieces[0].Id);
        Assert.Equal("source-approver", config.SourceDocument.ApprovedBy);
        await using var db = f.CreateDbContext(); Assert.Empty(await db.Approvals.ToArrayAsync());
    }

    [Fact] public async Task InvalidImportCannotPublishAnyPartialResource()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture();
        var invalid = JsonSerializer.SerializeToUtf8Bytes(new ModelingImportDocument(1, SyntheticGeometry.Rectangle() with { Units = "unknown" }, new()));
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => Service(f, blobs).ImportAsync(PersistenceFixture.Scope, Guid.NewGuid(), invalid.AsMemory()));
        await using var db = f.CreateDbContext(); Assert.Equal(0, await db.Documents.CountAsync()); Assert.Equal(0, await db.Revisions.CountAsync()); Assert.Equal(0, blobs.Writes);
    }

    [Fact] public async Task StorageFailureDoesNotExposeMetadataAndRetryHasOneDocument()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture { FailAtWrite = 2 };
        var op = Guid.NewGuid(); var input = Input();
        await Assert.ThrowsAsync<IOException>(() => Service(f, blobs).ImportAsync(PersistenceFixture.Scope, op, input.AsMemory()));
        await using var db = f.CreateDbContext(); Assert.Equal(0, await db.Documents.CountAsync()); Assert.Equal(0, await db.Revisions.CountAsync());
        blobs.FailAtWrite = null; var doc = await Service(f, blobs).ImportAsync(PersistenceFixture.Scope, op, input.AsMemory());
        var replay = await Service(f, blobs).ImportAsync(PersistenceFixture.Scope, op, input.AsMemory()); Assert.Equal(doc.Id, replay.Id);
        Assert.Equal(1, await db.Documents.CountAsync()); Assert.Equal(1, await db.Revisions.CountAsync());
    }

    [Fact] public async Task RetryAfterLostPublicationResponseRecoversTheCommittedIdentity()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var blobs = new PrivateArtifactFixture();
        var q = new ModelingQueries(f); var op = Guid.NewGuid(); var input = Input();
        var repository = new LostResponse(new ModelingImportRepository(f, TimeProvider.System));
        var service = new ModelingImportService(repository, q, new(blobs, 1024 * 1024), new(24, 20000));
        await Assert.ThrowsAsync<IOException>(() => service.ImportAsync(PersistenceFixture.Scope, op, input));
        var writes = blobs.Writes; var recovered = await service.ImportAsync(PersistenceFixture.Scope, op, input);
        Assert.Equal(repository.CommittedId, recovered.Id); Assert.Equal(writes, blobs.Writes);
        await using var db = f.CreateDbContext(); Assert.Single(await db.Documents.ToArrayAsync()); Assert.Single(await db.Revisions.ToArrayAsync());
    }

    private sealed class LostResponse(IModelingImportRepository inner) : IModelingImportRepository
    {
        internal Guid CommittedId;
        public async Task<ModelingDocumentSnapshot> ImportAsync(ModelingImportPublication publication, CancellationToken ct = default)
        {
            var result = await inner.ImportAsync(publication, ct); CommittedId = result.Id;
            throw new IOException("synthetic lost response");
        }
    }
}
