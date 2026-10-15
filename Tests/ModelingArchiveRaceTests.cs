using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence;
using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GestionProduccion.Grading.Tests;

public sealed class ModelingArchiveRaceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SharedDocumentFenceSerializesArchiveAndCalculation(bool pauseCalculation)
    {
        var barrier = new WriteBarrier(pauseCalculation);
        await using var f = await PersistenceFixture.CreateAsync(barrier);
        var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p);
        var drafts = new ModelingDraftRepository(f, TimeProvider.System, new(10, TimeSpan.FromMinutes(1)));
        var lifecycle = new ModelingLifecycleRepository(f, TimeProvider.System);
        barrier.Enabled = true;
        Task paused = pauseCalculation
            ? drafts.RequestAsync(p.Scope, p.ModelId, 1, Guid.NewGuid())
            : lifecycle.SetArchivedAsync(p.Scope, p.ModelId, true);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            if (pauseCalculation) await lifecycle.SetArchivedAsync(p.Scope, p.ModelId, true);
            else await drafts.RequestAsync(p.Scope, p.ModelId, 1, Guid.NewGuid());
        }
        finally { barrier.Release.TrySetResult(); }
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => paused);
        Assert.Equal("modeling_write_conflict", error.Code);
        var q = new ModelingQueries(f);
        Assert.Equal(pauseCalculation, (await q.DocumentAsync(p.Scope, p.ModelId))!.IsArchived);
        await using var db = f.CreateDbContext();
        Assert.Equal(pauseCalculation ? 0 : 1, await db.Jobs.CountAsync());
        Assert.Equal(pauseCalculation ? 1 : 2, await db.Revisions.CountAsync());
    }

    private sealed class WriteBarrier(bool calculation) : SaveChangesInterceptor
    {
        internal bool Enabled;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int consumed;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            var db = data.Context!;
            var target = calculation
                ? db.ChangeTracker.Entries<GradingJobRecord>().Any(e => e.State == EntityState.Added)
                : db.ChangeTracker.Entries<ModelingDocumentRecord>().Any(e => e.State == EntityState.Modified && e.Entity.IsArchived);
            if (Enabled && target && Interlocked.Exchange(ref consumed, 1) == 0)
            {
                // Pause before the transaction, so the competing write deterministically wins the fence.
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            return result;
        }
    }
}
