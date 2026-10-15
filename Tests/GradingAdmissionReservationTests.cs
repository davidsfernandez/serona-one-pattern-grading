using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingAdmissionReservationTests
{
    [Fact]
    public async Task RunningWorkReservesAdmissionCapacitySoReleaseCannotOverflowTheQueue()
    {
        var clock = TimeProvider.System;
        var policy = new GradingExecutionPolicy(1, TimeSpan.FromMinutes(1));
        await using var fixture = await PersistenceFixture.CreateAsync();
        var imports = new ModelingImportRepository(fixture, clock);
        var drafts = new ModelingDraftRepository(fixture, clock, policy);
        var lifecycle = new GradingJobLifecycleRepository(fixture, clock);
        var first = PersistenceFixture.Publication();
        await imports.ImportAsync(first);
        var accepted = await drafts.RequestAsync(first.Scope, first.ModelId, 1, Guid.NewGuid());
        var work = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(policy));
        Assert.Equal(accepted.JobId, work.JobId);
        Assert.Equal(GradingJobState.Running, (await new ModelingQueries(fixture).JobAsync(first.Scope, accepted.JobId))!.State);

        var second = PersistenceFixture.Publication();
        await imports.ImportAsync(second);
        var rejectedOperation = Guid.NewGuid();
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() =>
            drafts.RequestAsync(second.Scope, second.ModelId, 1, rejectedOperation));

        Assert.Equal("grading_queue_capacity", error.Code);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(1, await db.Jobs.CountAsync());
            Assert.Equal(3, await db.Revisions.CountAsync());
            Assert.Equal(1, await db.Revisions.CountAsync(r => r.ModelId == second.ModelId));
            Assert.False(await db.Operations.AnyAsync(o => o.OperationId == rejectedOperation));
        }

        Assert.True(await lifecycle.ReleaseAsync(work));
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(1, await db.Jobs.CountAsync(j => j.State == GradingJobState.Pending));
            Assert.True(await db.Jobs.CountAsync(j => j.State == GradingJobState.Pending) <= policy.MaximumPending);
        }
        var recovered = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(policy));
        Assert.Equal(accepted.JobId, recovered.JobId);
        Assert.Equal(accepted.RevisionId, recovered.RevisionId);
        Assert.True(recovered.ClaimVersion > work.ClaimVersion);
        Assert.NotEqual(work.LeaseToken, recovered.LeaseToken);
    }
}
