using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingResultPersistenceTests
{
    internal static async Task<GradingRequestSnapshot> Running(PersistenceFixture f, ModelingImportPublication p)
    {
        var job = await new ModelingDraftRepository(f, TimeProvider.System, new(10, TimeSpan.FromMinutes(1))).RequestAsync(p.Scope, p.ModelId, 1, Guid.NewGuid());
        // Isolated producer fixture, not a claim implementation or a mutation of a runtime database.
        await using var db = f.CreateDbContext(); var row = await db.Jobs.SingleAsync(j => j.Id == job.JobId);
        row.State = GradingJobState.Running; row.Version = 2; await db.SaveChangesAsync(); return job with { State = row.State, Version = row.Version };
    }

    [Fact] public async Task ResultRetryIsImmutableAndCannotReplaceGeometry()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p); var job = await Running(f, p);
        var repo = new GradingResultRepository(f, TimeProvider.System); var artifact = PersistenceFixture.Artifact("result");
        var first = await repo.CompleteAsync(p.Scope, job.JobId, 2, true, artifact);
        Assert.Equal(first.Id, (await repo.CompleteAsync(p.Scope, job.JobId, 2, true, artifact)).Id);
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => repo.CompleteAsync(p.Scope, job.JobId, 2, true, PersistenceFixture.Artifact("changed")));
        await using var db = f.CreateDbContext(); Assert.Equal(1, await db.Results.CountAsync());
    }

    [Fact] public async Task ApprovalsPreserveHistoryAndCurrentTransitionIsAudited()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p);
        var repository = new GradingResultRepository(f, TimeProvider.System); var firstJob = await Running(f, p);
        var first = await repository.CompleteAsync(p.Scope, firstJob.JobId, 2, true, PersistenceFixture.Artifact("first"));
        var operation = Guid.NewGuid(); var a = await repository.ApproveAsync(p.Scope, first.Id, operation);
        Assert.Equal(p.Scope.OwnerId, a.ApprovedBy); Assert.Equal(first.Id, a.ResultId);
        Assert.Equal(a.Id, (await repository.ApproveAsync(p.Scope, first.Id, operation)).Id);
        var secondJob = await Running(f, p); var second = await repository.CompleteAsync(p.Scope, secondJob.JobId, 2, true, PersistenceFixture.Artifact("second"));
        await repository.ApproveAsync(p.Scope, second.Id, Guid.NewGuid()); var q = new ModelingQueries(f);
        Assert.Equal(second.Id, (await q.DocumentAsync(p.Scope, p.ModelId))!.CurrentApprovedResultId);
        Assert.Equal(2, (await q.ApprovalsAsync(p.Scope, p.ModelId)).Count);
        Assert.NotNull(await q.ResultAsync(p.Scope, first.Id));
        Assert.Equal(2, (await q.AuditAsync(p.Scope, p.ModelId)).Count(e => e.Action == "ApprovedCurrent"));
        await repository.ApproveAsync(p.Scope, first.Id, Guid.NewGuid());
        Assert.Equal(first.Id, (await q.DocumentAsync(p.Scope, p.ModelId))!.CurrentApprovedResultId);
    }

    [Fact] public async Task InvalidCalculatedVersionNeverBecomesApproved()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p); var job = await Running(f, p);
        var repo = new GradingResultRepository(f, TimeProvider.System); var result = await repo.CompleteAsync(p.Scope, job.JobId, 2, false, PersistenceFixture.Artifact("diagnostics"));
        var e = await Assert.ThrowsAsync<ModelingPersistenceException>(() => repo.ApproveAsync(p.Scope, result.Id, Guid.NewGuid()));
        Assert.Equal("modeling_result_not_approvable", e.Code);
        Assert.Null((await new ModelingQueries(f).DocumentAsync(p.Scope, p.ModelId))!.CurrentApprovedResultId);
    }

    [Fact] public async Task ForeignResultAndJobCannotBeReadOrApproved()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p); var job = await Running(f, p);
        var repo = new GradingResultRepository(f, TimeProvider.System); var result = await repo.CompleteAsync(p.Scope, job.JobId, 2, true, PersistenceFixture.Artifact("result"));
        foreach (var wrong in new[] { new ModelingScope("other", p.Scope.OwnerId), new(p.Scope.CompanyId, "other") })
        {
            var q = new ModelingQueries(f); Assert.Null(await q.JobAsync(wrong, job.JobId)); Assert.Null(await q.ResultAsync(wrong, result.Id));
            var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => repo.ApproveAsync(wrong, result.Id, Guid.NewGuid())); Assert.Equal("modeling_not_found", error.Code);
        }
    }

    [Fact] public async Task BooleanSaveChangesOverloadCannotRewriteAnImmutableResult()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p); var job = await Running(f, p);
        await new GradingResultRepository(f, TimeProvider.System).CompleteAsync(p.Scope, job.JobId, 2, true, PersistenceFixture.Artifact("result"));
        await using var db = f.CreateDbContext(); var result = await db.Results.SingleAsync(); result.ArtifactHash = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(acceptAllChangesOnSuccess: true));
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges(acceptAllChangesOnSuccess: true));
    }
}
