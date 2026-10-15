using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence;
using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingNestingHandoffPersistenceTests
{
    private static async Task<GradingNestingHandoffPublication> Approved(PersistenceFixture f)
    {
        var p = PersistenceFixture.Publication();
        await new ModelingImportRepository(f, TimeProvider.System).ImportAsync(p);
        var job = await GradingResultPersistenceTests.Running(f, p);
        var results = new GradingResultRepository(f, TimeProvider.System);
        var result = await results.CompleteAsync(p.Scope, job.JobId, 2, true, PersistenceFixture.Artifact("result"));
        var approval = await results.ApproveAsync(p.Scope, result.Id, Guid.NewGuid());
        var doc = (await new ModelingQueries(f).DocumentAsync(p.Scope, p.ModelId))!;
        return new(new(new(Guid.NewGuid(), p.ModelId, result.RevisionId, result.Id, approval.Id,
            p.Scope.OwnerId, p.Scope.CompanyId, result.Artifact.Sha256), Guid.NewGuid(), "fingerprint",
            PersistenceFixture.Artifact("handoff")), doc.Version);
    }

    [Fact] public async Task RetryReturnsOriginalAndRejectsChangedPayload()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = await Approved(f);
        var repo = new GradingNestingHandoffRepository(f, TimeProvider.System);
        Assert.Equal(p.Snapshot, await repo.PublishAsync(PersistenceFixture.Scope, p));
        Assert.Equal(p.Snapshot, await repo.PublishAsync(PersistenceFixture.Scope, p));
        var relocated = p with { Snapshot = p.Snapshot with { Artifact = p.Snapshot.Artifact with { Key = "retry-location" },
            Source = p.Snapshot.Source with { HandoffId = Guid.NewGuid() } } };
        Assert.Equal(p.Snapshot, await repo.PublishAsync(PersistenceFixture.Scope, relocated));
        var changed = p with { Snapshot = p.Snapshot with { Artifact = PersistenceFixture.Artifact("changed") } };
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => repo.PublishAsync(PersistenceFixture.Scope, changed));
        Assert.Equal("modeling_operation_conflict", error.Code);
        await using var db = f.CreateDbContext(); Assert.Equal(1, await db.Handoffs.CountAsync());
        Assert.Equal(1, await db.Audit.CountAsync(a => a.Action == "NestingHandoffPublished"));
    }

    [Fact] public async Task OwnerAndCompanyScopeProtectReadsAndPublication()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = await Approved(f);
        var repo = new GradingNestingHandoffRepository(f, TimeProvider.System);
        await repo.PublishAsync(PersistenceFixture.Scope, p);
        foreach (var scope in new[] { new ModelingScope("foreign", "owner-a"), new("company-a", "foreign") })
        {
            Assert.Null(await repo.GetAsync(scope, p.Snapshot.Source.HandoffId));
            Assert.Null(await repo.OperationAsync(scope, p.Snapshot.OperationId));
            await Assert.ThrowsAsync<ModelingPersistenceException>(() => repo.PublishAsync(scope, p));
        }
    }

    [Fact] public async Task PinnedApprovalSurvivesCurrentSelectionChangeAndArchive()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = await Approved(f);
        var repo = new GradingNestingHandoffRepository(f, TimeProvider.System);
        await repo.PublishAsync(PersistenceFixture.Scope, p);
        var results = new GradingResultRepository(f, TimeProvider.System);
        var model = p.Snapshot.Source.ModelId;
        var job = await GradingResultPersistenceTests.Running(f,
            PersistenceFixture.Publication() with { ModelId = model });
        var next = await results.CompleteAsync(PersistenceFixture.Scope, job.JobId, 2, true, PersistenceFixture.Artifact("next-result"));
        await results.ApproveAsync(PersistenceFixture.Scope, next.Id, Guid.NewGuid());
        Assert.Equal(next.Id, (await new ModelingQueries(f).DocumentAsync(PersistenceFixture.Scope, model))!.CurrentApprovedResultId);
        await new ModelingLifecycleRepository(f, TimeProvider.System).SetArchivedAsync(PersistenceFixture.Scope, p.Snapshot.Source.ModelId, true);
        Assert.Equal(p.Snapshot, await repo.GetAsync(PersistenceFixture.Scope, p.Snapshot.Source.HandoffId));
        Assert.Equal(p.Snapshot, await repo.PublishAsync(PersistenceFixture.Scope, p));
        var fresh = p with { Snapshot = p.Snapshot with { OperationId = Guid.NewGuid(), Source = p.Snapshot.Source with { HandoffId = Guid.NewGuid() } } };
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => repo.PublishAsync(PersistenceFixture.Scope, fresh));
        Assert.Equal("modeling_archived", error.Code);
    }

    [Fact] public async Task StaleVersionAndWrongApprovalNeverPublishPartialRows()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = await Approved(f);
        var repo = new GradingNestingHandoffRepository(f, TimeProvider.System);
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => repo.PublishAsync(PersistenceFixture.Scope, p with { ExpectedDocumentVersion = p.ExpectedDocumentVersion - 1 }));
        Assert.Equal("modeling_write_conflict", error.Code);
        await Assert.ThrowsAsync<ModelingPersistenceException>(() => repo.PublishAsync(PersistenceFixture.Scope,
            p with { Snapshot = p.Snapshot with { Source = p.Snapshot.Source with { ApprovalId = Guid.NewGuid() } } }));
        await using var db = f.CreateDbContext(); Assert.Empty(await db.Handoffs.ToArrayAsync());
        Assert.Equal(p.ExpectedDocumentVersion, (await db.Documents.SingleAsync()).Version);
        Assert.Equal(0, await db.Audit.CountAsync(a => a.Action == "NestingHandoffPublished"));
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task ArchiveAndHandoffShareAnAtomicDocumentFence(bool pauseHandoff)
    {
        var barrier = new WriteBarrier(pauseHandoff);
        await using var f = await PersistenceFixture.CreateAsync(barrier); var p = await Approved(f);
        var repo = new GradingNestingHandoffRepository(f, TimeProvider.System);
        var lifecycle = new ModelingLifecycleRepository(f, TimeProvider.System);
        barrier.Enabled = true;
        Task paused = pauseHandoff ? repo.PublishAsync(PersistenceFixture.Scope, p)
            : lifecycle.SetArchivedAsync(PersistenceFixture.Scope, p.Snapshot.Source.ModelId, true);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            if (pauseHandoff) await lifecycle.SetArchivedAsync(PersistenceFixture.Scope, p.Snapshot.Source.ModelId, true);
            else await repo.PublishAsync(PersistenceFixture.Scope, p);
        }
        finally { barrier.Release.TrySetResult(); }
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => paused);
        Assert.Equal("modeling_write_conflict", error.Code);
        await using var db = f.CreateDbContext();
        Assert.Equal(pauseHandoff ? 0 : 1, await db.Handoffs.CountAsync());
        Assert.Equal(pauseHandoff ? 0 : 1, await db.Audit.CountAsync(a => a.Action == "NestingHandoffPublished"));
        Assert.Equal(pauseHandoff, (await db.Documents.SingleAsync()).IsArchived);
    }

    [Fact] public async Task HandoffCannotBeRewrittenOrDeleted()
    {
        await using var f = await PersistenceFixture.CreateAsync(); var p = await Approved(f);
        await new GradingNestingHandoffRepository(f, TimeProvider.System).PublishAsync(PersistenceFixture.Scope, p);
        await using var db = f.CreateDbContext(); var row = await db.Handoffs.SingleAsync(); row.GeometryHash = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(true));
        db.Entry(row).State = EntityState.Unchanged; db.Handoffs.Remove(row);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges(true));
    }

    private sealed class WriteBarrier(bool handoff) : SaveChangesInterceptor
    {
        internal bool Enabled;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int consumed;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            var target = handoff ? data.Context!.ChangeTracker.Entries<GradingNestingHandoffRecord>().Any(e => e.State == EntityState.Added)
                : data.Context!.ChangeTracker.Entries<ModelingDocumentRecord>().Any(e => e.State == EntityState.Modified && e.Entity.IsArchived);
            if (Enabled && target && Interlocked.Exchange(ref consumed, 1) == 0)
            {
                Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            return result;
        }
    }
}
