using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence;
using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingLifecycleRaceTests
{
    [Fact]
    public async Task ConcurrentClaimRaceProducesOneLeaseAndOneExplicitConcurrencyLoser()
    {
        var clock = new FrozenTimeProvider(); var barrier = new ClaimBarrier();
        await using var f = await PersistenceFixture.CreateAsync(barrier);
        var publication = PersistenceFixture.Publication(); await new ModelingImportRepository(f, clock).ImportAsync(publication);
        await new ModelingDraftRepository(f, clock, Policy).RequestAsync(publication.Scope, publication.ModelId, 1, Guid.NewGuid());
        var lifecycle = new GradingJobLifecycleRepository(f, clock); barrier.Enabled = true;
        var paused = lifecycle.ClaimAsync(Policy);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var competing = lifecycle.ClaimAsync(Policy);
        await barrier.CompetingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        GradingWorkItem? committed;
        try { committed = await competing; }
        finally { barrier.Release.TrySetResult(); }

        Assert.IsType<GradingWorkItem>(committed);
        var loser = Assert.IsType<ModelingPersistenceException>((await ObserveAsync(paused)).Error);
        Assert.Equal("modeling_write_conflict", loser.Code);
    }

    [Fact]
    public async Task ConcurrentCompletionAndCancellationLeaveCancellationEffectiveWithoutAResult()
    {
        var clock = new FrozenTimeProvider(); var barrier = new CompletionBarrier();
        await using var f = await PersistenceFixture.CreateAsync(barrier);
        var publication = PersistenceFixture.Publication(); await new ModelingImportRepository(f, clock).ImportAsync(publication);
        await new ModelingDraftRepository(f, clock, Policy).RequestAsync(publication.Scope, publication.ModelId, 1, Guid.NewGuid());
        var lifecycle = new GradingJobLifecycleRepository(f, clock);
        var work = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(Policy));
        barrier.Enabled = true;
        var completion = lifecycle.CompleteAsync(work, true, PersistenceFixture.Artifact("racing"));
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await lifecycle.CancelAsync(publication.Scope, work.JobId);
        barrier.Release.TrySetResult();

        var conflict = await Assert.ThrowsAsync<ModelingPersistenceException>(() => completion);
        Assert.Equal("modeling_write_conflict", conflict.Code);
        Assert.Null(await lifecycle.CompleteAsync(work, true, PersistenceFixture.Artifact("racing")));
        Assert.False(await lifecycle.AcknowledgeCancellationAsync(work));
        Assert.Equal(GradingJobState.Cancelled, (await new ModelingQueries(f).JobAsync(publication.Scope, work.JobId))!.State);
        Assert.Null(await new ModelingQueries(f).ResultForJobAsync(publication.Scope, work.JobId));
    }

    private static readonly GradingExecutionPolicy Policy = new(10, TimeSpan.FromMinutes(1));

    private static async Task<(GradingWorkItem? Work, ModelingPersistenceException? Error)> ObserveAsync(Task<GradingWorkItem?> task)
    {
        try { return (await task, null); }
        catch (ModelingPersistenceException error) { return (null, error); }
    }

    private sealed class FrozenTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ClaimBarrier : SaveChangesInterceptor
    {
        internal bool Enabled;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CompetingEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int consumed;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            var claiming = data.Context!.ChangeTracker.Entries<GradingJobRecord>()
                .Any(x => x.State == EntityState.Modified && x.Entity.State == GradingJobState.Running);
            if (Enabled && claiming && Interlocked.Exchange(ref consumed, 1) == 0)
            {
                // The competing writer wins before this claim reaches its optimistic admission commit.
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            else if (Enabled && claiming) CompetingEntered.TrySetResult();
            return result;
        }
    }

    private sealed class CompletionBarrier : SaveChangesInterceptor
    {
        internal bool Enabled;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int consumed;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            var publishing = data.Context!.ChangeTracker.Entries<GradingResultRecord>().Any(x => x.State == EntityState.Added);
            if (Enabled && publishing && Interlocked.Exchange(ref consumed, 1) == 0)
            {
                // Cancellation commits against the same admission fence before this publication can commit.
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            return result;
        }
    }
}
