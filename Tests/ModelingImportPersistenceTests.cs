using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Tests;

public sealed class ModelingImportPersistenceTests
{
    [Fact] public async Task AtomicImportCreatesDocumentDraftRevisionAndOperationTogether()
    {
        await using var f = await PersistenceFixture.CreateAsync();
        var publication = PersistenceFixture.Publication();
        var result = await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(publication);
        await using var db = f.CreateDbContext();
        Assert.Equal(publication.ModelId, result.Id); Assert.False(result.IsArchived); Assert.Null(result.CurrentApprovedResultId);
        Assert.Equal(1, await db.Documents.CountAsync()); Assert.Equal(1, await db.Drafts.CountAsync());
        Assert.Equal(1, await db.Revisions.CountAsync()); Assert.Equal(1, await db.Operations.CountAsync());
        Assert.Equal(0, await db.Jobs.CountAsync()); Assert.Equal(0, await db.Approvals.CountAsync());
    }

    [Fact] public async Task RetriedImportNeverCreatesAnotherDocumentOrRevision()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var repository = new ModelingImportRepository(f, TimeProvider.System);
        var input = PersistenceFixture.Publication(); var first = await repository.ImportAsync(input);
        var second = await repository.ImportAsync(input with { ModelId = Guid.NewGuid(), RevisionId = Guid.NewGuid() });
        Assert.Equal(first.Id, second.Id);
        await using var db = f.CreateDbContext(); Assert.Equal(1, await db.Documents.CountAsync()); Assert.Equal(1, await db.Revisions.CountAsync());
    }

    [Fact] public async Task SameOperationWithDifferentContentIsRejected()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var repository = new ModelingImportRepository(f, TimeProvider.System);
        var input = PersistenceFixture.Publication(); await repository.ImportAsync(input);
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => repository.ImportAsync(input with { Fingerprint = PersistenceFixture.Artifact("different").Sha256 }));
        Assert.Equal("modeling_operation_conflict", error.Code);
    }
}
