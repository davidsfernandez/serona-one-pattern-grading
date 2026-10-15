using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Tests;

public sealed class ModelingLifecyclePersistenceTests
{
    [Theory] [InlineData(GradingJobState.Pending)] [InlineData(GradingJobState.Running)]
    public async Task NonterminalJobBlocksArchiveIncludingRequestedCancellation(GradingJobState state)
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p);
        var job = await new ModelingDraftRepository(f, TimeProvider.System, new(10, TimeSpan.FromMinutes(1))).RequestAsync(p.Scope, p.ModelId, 1, Guid.NewGuid());
        await using var db = f.CreateDbContext(); var row = await db.Jobs.SingleAsync(); row.State = state; row.CancellationRequestedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => new ModelingLifecycleRepository(f, TimeProvider.System).SetArchivedAsync(p.Scope, p.ModelId, true));
        Assert.Contains(job.JobId, error.BlockingJobIds); Assert.False((await new ModelingQueries(f).DocumentAsync(p.Scope, p.ModelId))!.IsArchived);
        Assert.Equal(state, (await new ModelingQueries(f).JobAsync(p.Scope, job.JobId))!.State);
    }

    [Fact] public async Task ArchivePreservesHistoryAndBlocksMutationsUntilExplicitReactivation()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p); var job = await GradingResultPersistenceTests.Running(f, p);
        var results = new GradingResultRepository(f, TimeProvider.System); var result = await results.CompleteAsync(p.Scope, job.JobId, 2, true, PersistenceFixture.Artifact("result"));
        await results.ApproveAsync(p.Scope, result.Id, Guid.NewGuid()); var lifecycle = new ModelingLifecycleRepository(f, TimeProvider.System);
        await lifecycle.SetArchivedAsync(p.Scope, p.ModelId, true); var drafts = new ModelingDraftRepository(f, TimeProvider.System, new(10, TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => drafts.SaveAsync(p.Scope, p.ModelId, 1, PersistenceFixture.Artifact("no")));
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => drafts.RequestAsync(p.Scope, p.ModelId, 1, Guid.NewGuid()));
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => results.ApproveAsync(p.Scope, result.Id, Guid.NewGuid()));
        var q = new ModelingQueries(f); Assert.NotNull(await q.ResultAsync(p.Scope, result.Id)); Assert.NotNull(await q.RevisionAsync(p.Scope, job.RevisionId));
        Assert.Equal(result.Id, (await q.DocumentAsync(p.Scope, p.ModelId))!.CurrentApprovedResultId);
        var before = (await q.RevisionsAsync(p.Scope, p.ModelId)).Count; await lifecycle.SetArchivedAsync(p.Scope, p.ModelId, false);
        Assert.Equal(before, (await q.RevisionsAsync(p.Scope, p.ModelId)).Count); Assert.Equal(result.Id, (await q.DocumentAsync(p.Scope, p.ModelId))!.CurrentApprovedResultId);
        Assert.Single(await q.ApprovalsAsync(p.Scope, p.ModelId));
    }
}
