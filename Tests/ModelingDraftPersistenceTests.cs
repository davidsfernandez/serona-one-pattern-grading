using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Tests;

public sealed class ModelingDraftPersistenceTests
{
    [Fact] public async Task CompanyAndOwnerAreBothRequiredBeforeIdentifierLookup()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p); var q = new ModelingQueries(f);
        Assert.NotNull(await q.DocumentAsync(p.Scope, p.ModelId));
        foreach (var wrong in new[] { new ModelingScope("other", p.Scope.OwnerId), new(p.Scope.CompanyId, "other"), new("COMPANY-A", p.Scope.OwnerId) })
        { Assert.Null(await q.DocumentAsync(wrong, p.ModelId)); Assert.Null(await q.DraftAsync(wrong, p.ModelId)); Assert.Null(await q.RevisionAsync(wrong, p.RevisionId)); Assert.Null(await q.OperationAsync(wrong, p.OperationId)); }
    }

    [Fact] public async Task StaleDraftSaveCannotOverwriteOrChangeTheOriginalRevision()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p); var repository = new ModelingDraftRepository(f, TimeProvider.System, new(10, TimeSpan.FromMinutes(1)));
        var replacement = PersistenceFixture.Artifact("new draft");
        Assert.Equal(2, (await repository.SaveAsync(p.Scope, p.ModelId, 1, replacement)).Version);
        var conflict = await Assert.ThrowsAsync<ModelingPersistenceException>(() => repository.SaveAsync(p.Scope, p.ModelId, 1, PersistenceFixture.Artifact("stale")));
        Assert.Equal("modeling_draft_conflict", conflict.Code); Assert.Equal(2, conflict.CurrentVersion);
        var q = new ModelingQueries(f); Assert.Equal(replacement, (await q.DraftAsync(p.Scope, p.ModelId))!.Artifact);
        Assert.Equal(p.Configuration, (await q.RevisionAsync(p.Scope, p.RevisionId))!.Artifact);
    }

    [Fact] public async Task CalculationRequestIsAtomicIdempotentAndPinnedToSavedDraft()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p); var repository = new ModelingDraftRepository(f, TimeProvider.System, new(10, TimeSpan.FromMinutes(1)));
        var op = Guid.NewGuid(); var first = await repository.RequestAsync(p.Scope, p.ModelId, 1, op);
        await repository.SaveAsync(p.Scope, p.ModelId, 1, PersistenceFixture.Artifact("later draft"));
        var replay = await repository.RequestAsync(p.Scope, p.ModelId, 1, op); Assert.Equal(first.JobId, replay.JobId);
        var q = new ModelingQueries(f); Assert.Equal(p.Configuration, (await q.RevisionAsync(p.Scope, first.RevisionId))!.Artifact);
        await using var db = f.CreateDbContext(); Assert.Equal(2, await db.Revisions.CountAsync()); Assert.Equal(1, await db.Jobs.CountAsync());
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => repository.RequestAsync(p.Scope, p.ModelId, 1, Guid.NewGuid()));
    }

    [Fact] public async Task ImmutableRevisionCannotBeUpdatedOrDeletedThroughTheContext()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p);
        await using var db = f.CreateDbContext(); var revision = await db.Revisions.SingleAsync(); revision.ArtifactHash = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.Entry(revision).State = EntityState.Unchanged; db.Remove(revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
