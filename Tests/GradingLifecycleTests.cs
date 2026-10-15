using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingLifecycleTests
{
    private static readonly GradingExecutionPolicy DefaultPolicy = new(10, TimeSpan.FromMinutes(1));

    [Fact]
    public async Task ConfiguredGlobalAndOwnerLimitsAllowDistinctOwnersButLimitEachOwner()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        var first = await RequestAsync(f, clock, PersistenceFixture.Publication());
        var second = await RequestAsync(f, clock, PersistenceFixture.Publication());
        var otherScope = new ModelingScope(PersistenceFixture.Scope.CompanyId, "owner-b");
        var third = await RequestAsync(f, clock, PersistenceFixture.Publication(otherScope));
        var lifecycle = Lifecycle(f, clock);
        var policy = new GradingExecutionPolicy(10, TimeSpan.FromMinutes(1), MaximumGlobalActive: 2, MaximumOwnerActive: 1);

        var claimed = new[] { await lifecycle.ClaimAsync(policy), await lifecycle.ClaimAsync(policy), await lifecycle.ClaimAsync(policy) }
            .Where(x => x is not null).Cast<GradingWorkItem>().ToArray();

        Assert.Equal(2, claimed.Length);
        Assert.Contains(claimed, x => x.JobId == first.JobId || x.JobId == second.JobId);
        Assert.Contains(claimed, x => x.JobId == third.JobId);
        Assert.Single(claimed.Where(x => x.Scope == PersistenceFixture.Scope));
    }

    [Fact]
    public async Task ExpiredClaimIsRecoveredAndStaleWorkerCannotPublish()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        await RequestAsync(f, clock, PersistenceFixture.Publication());
        var lifecycle = Lifecycle(f, clock); var first = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));
        clock.Advance(TimeSpan.FromMinutes(2));
        var recovered = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));

        Assert.NotEqual(first.LeaseToken, recovered.LeaseToken);
        Assert.True(recovered.ClaimVersion > first.ClaimVersion);
        Assert.Equal(GradingLeaseObservation.Lost, await lifecycle.RenewAsync(first, DefaultPolicy));
        Assert.False(await lifecycle.ReleaseAsync(first));
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => lifecycle.CompleteAsync(first, true, PersistenceFixture.Artifact("stale")));
        Assert.Equal("grading_fence_lost", error.Code);
    }

    [Fact]
    public async Task RenewAndReleaseReturnWorkToTheQueueWithoutChangingItsRevision()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        var job = await RequestAsync(f, clock, PersistenceFixture.Publication());
        var lifecycle = Lifecycle(f, clock); var work = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(GradingLeaseObservation.Current, await lifecycle.RenewAsync(work, DefaultPolicy));
        Assert.True(await lifecycle.ReleaseAsync(work));
        var retry = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));

        Assert.Equal(job.RevisionId, retry.RevisionId);
        Assert.True(retry.ClaimVersion > work.ClaimVersion);
    }

    [Fact]
    public async Task PendingCancellationIsEffectiveImmediatelyAndAudited()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        var publication = PersistenceFixture.Publication(); var job = await RequestAsync(f, clock, publication);
        var cancelled = await Lifecycle(f, clock).CancelAsync(publication.Scope, job.JobId);

        Assert.Equal(GradingJobState.Cancelled, cancelled.State);
        Assert.Equal(publication.Scope.OwnerId, cancelled.RequestedBy);
        Assert.Equal(clock.GetUtcNow(), cancelled.RequestedAt);
        Assert.Equal(clock.GetUtcNow(), cancelled.EffectiveAt);
        Assert.Equal(GradingJobState.Cancelled, (await new ModelingQueries(f).JobAsync(publication.Scope, job.JobId))!.State);
        Assert.Contains((await new ModelingQueries(f).AuditAsync(publication.Scope, publication.ModelId)), x => x.Action == "CancellationEffective" && x.ResourceId == job.JobId);
    }

    [Fact]
    public async Task RunningCancellationRecordsActorAndAcknowledgementWinsAgainstCompletion()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        var publication = PersistenceFixture.Publication(); await RequestAsync(f, clock, publication);
        var lifecycle = Lifecycle(f, clock); var work = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));

        clock.Advance(TimeSpan.FromSeconds(5));
        var requested = await lifecycle.CancelAsync(publication.Scope, work.JobId);
        Assert.Equal(GradingJobState.Running, requested.State);
        Assert.Equal(publication.Scope.OwnerId, requested.RequestedBy);
        Assert.Equal(clock.GetUtcNow(), requested.RequestedAt);
        Assert.Null(requested.EffectiveAt);
        Assert.Equal(GradingLeaseObservation.CancellationRequested, await lifecycle.RenewAsync(work, DefaultPolicy));
        Assert.True(await lifecycle.AcknowledgeCancellationAsync(work));
        Assert.Equal(GradingJobState.Cancelled, (await new ModelingQueries(f).JobAsync(publication.Scope, work.JobId))!.State);
        var audit = await new ModelingQueries(f).AuditAsync(publication.Scope, publication.ModelId);
        Assert.Contains(audit, x => x.Action == "CancellationRequested" && x.ActorId == publication.Scope.OwnerId);
        Assert.Contains(audit, x => x.Action == "CancellationEffective" && x.ResourceId == work.JobId);
    }

    [Fact]
    public async Task CancellationRequestTakesPrecedenceOverACompletionThatHasNotCommitted()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        var publication = PersistenceFixture.Publication(); await RequestAsync(f, clock, publication);
        var lifecycle = Lifecycle(f, clock); var work = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));

        await lifecycle.CancelAsync(publication.Scope, work.JobId);

        Assert.Null(await lifecycle.CompleteAsync(work, true, PersistenceFixture.Artifact("suppressed")));
        Assert.Equal(GradingJobState.Cancelled, (await new ModelingQueries(f).JobAsync(publication.Scope, work.JobId))!.State);
        Assert.Null(await new ModelingQueries(f).ResultForJobAsync(publication.Scope, work.JobId));
    }

    [Fact]
    public async Task CompletionCommittedFirstRemainsSucceededWhenCancellationArrivesLater()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        var publication = PersistenceFixture.Publication(); await RequestAsync(f, clock, publication);
        var lifecycle = Lifecycle(f, clock); var work = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));
        var completed = Assert.IsType<GradingResultSnapshot>(await lifecycle.CompleteAsync(work, true, PersistenceFixture.Artifact("final")));

        var cancellation = await lifecycle.CancelAsync(publication.Scope, work.JobId);

        Assert.Equal(GradingJobState.Succeeded, cancellation.State);
        Assert.Equal(completed.Id, (await new ModelingQueries(f).ResultForJobAsync(publication.Scope, work.JobId))!.Id);
        Assert.Equal(GradingJobState.Succeeded, (await new ModelingQueries(f).JobAsync(publication.Scope, work.JobId))!.State);
    }

    [Fact]
    public async Task ExactResultRetryFromTheOriginalClaimReturnsTheCommittedResult()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        await RequestAsync(f, clock, PersistenceFixture.Publication());
        var lifecycle = Lifecycle(f, clock); var work = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));
        var artifact = PersistenceFixture.Artifact("stable-result");

        var first = Assert.IsType<GradingResultSnapshot>(await lifecycle.CompleteAsync(work, true, artifact));
        var replay = Assert.IsType<GradingResultSnapshot>(await lifecycle.CompleteAsync(work, true, artifact));

        Assert.Equal(first.Id, replay.Id);
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => lifecycle.CompleteAsync(work, true, PersistenceFixture.Artifact("different-result")));
        Assert.Equal("modeling_result_conflict", error.Code);
    }

    [Fact]
    public async Task DifferentLeaseTokenCannotReplayACommittedResult()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        await RequestAsync(f, clock, PersistenceFixture.Publication());
        var lifecycle = Lifecycle(f, clock); var work = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));
        var artifact = PersistenceFixture.Artifact("stable-result");
        Assert.IsType<GradingResultSnapshot>(await lifecycle.CompleteAsync(work, true, artifact));

        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => lifecycle.CompleteAsync(work with { LeaseToken = Guid.NewGuid() }, true, artifact));

        Assert.Equal("grading_fence_lost", error.Code);
    }

    [Fact]
    public async Task ForeignScopeCannotCancelAJob()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        var publication = PersistenceFixture.Publication(); var job = await RequestAsync(f, clock, publication);
        var foreign = new ModelingScope(publication.Scope.CompanyId, "other-owner");

        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => Lifecycle(f, clock).CancelAsync(foreign, job.JobId));

        Assert.Equal("modeling_not_found", error.Code);
        Assert.Equal(GradingJobState.Pending, (await new ModelingQueries(f).JobAsync(publication.Scope, job.JobId))!.State);
    }

    [Fact]
    public async Task QueueAdmissionIsBoundedAndOperationReplayDoesNotConsumeAnotherSlot()
    {
        var clock = new FakeTimeProvider(); var policy = new GradingExecutionPolicy(1, TimeSpan.FromMinutes(1));
        await using var f = await PersistenceFixture.CreateAsync();
        var first = PersistenceFixture.Publication(); await new ModelingImportRepository(f, clock).ImportAsync(first);
        var drafts = new ModelingDraftRepository(f, clock, policy); var calculationOperation = Guid.NewGuid();
        var job = await drafts.RequestAsync(first.Scope, first.ModelId, 1, calculationOperation);

        Assert.Equal(job.JobId, (await drafts.RequestAsync(first.Scope, first.ModelId, 1, calculationOperation)).JobId);
        var second = PersistenceFixture.Publication(); await new ModelingImportRepository(f, clock).ImportAsync(second);
        var error = await Assert.ThrowsAsync<ModelingPersistenceException>(() => drafts.RequestAsync(second.Scope, second.ModelId, 1, Guid.NewGuid()));
        Assert.Equal("grading_queue_capacity", error.Code);
    }

    [Fact]
    public async Task ExpiredLeaseMakesRequestedCancellationEffectiveDuringRecovery()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        var publication = PersistenceFixture.Publication(); await RequestAsync(f, clock, publication);
        var lifecycle = Lifecycle(f, clock); var work = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));
        await lifecycle.CancelAsync(publication.Scope, work.JobId);
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Null(await lifecycle.ClaimAsync(DefaultPolicy));

        Assert.Equal(GradingJobState.Cancelled, (await new ModelingQueries(f).JobAsync(publication.Scope, work.JobId))!.State);
        Assert.Contains((await new ModelingQueries(f).AuditAsync(publication.Scope, publication.ModelId)), x => x.Action == "CancellationEffective" && x.ResourceId == work.JobId);
    }

    [Fact]
    public async Task PendingAndRecoveredJobsBlockArchiveUntilCancellationIsEffective()
    {
        var clock = new FakeTimeProvider();
        await using var f = await PersistenceFixture.CreateAsync();
        var publication = PersistenceFixture.Publication(); var job = await RequestAsync(f, clock, publication);
        var lifecycle = Lifecycle(f, clock); var work = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));
        clock.Advance(TimeSpan.FromMinutes(2));
        var recovered = Assert.IsType<GradingWorkItem>(await lifecycle.ClaimAsync(DefaultPolicy));

        var blocked = await Assert.ThrowsAsync<ModelingPersistenceException>(() => new ModelingLifecycleRepository(f, clock).SetArchivedAsync(publication.Scope, publication.ModelId, true));
        Assert.Contains(job.JobId, blocked.BlockingJobIds);
        await lifecycle.CancelAsync(publication.Scope, job.JobId);
        Assert.True(await lifecycle.AcknowledgeCancellationAsync(recovered));
        Assert.Equal(GradingJobState.Cancelled, (await new ModelingQueries(f).JobAsync(publication.Scope, job.JobId))!.State);
        Assert.True((await new ModelingLifecycleRepository(f, clock).SetArchivedAsync(publication.Scope, publication.ModelId, true)).IsArchived);
    }

    private static GradingJobLifecycleRepository Lifecycle(PersistenceFixture fixture, TimeProvider clock) => new(fixture, clock);

    private static async Task<GradingRequestSnapshot> RequestAsync(PersistenceFixture fixture, TimeProvider clock, ModelingImportPublication publication)
    {
        await new ModelingImportRepository(fixture, clock).ImportAsync(publication);
        return await new ModelingDraftRepository(fixture, clock, DefaultPolicy).RequestAsync(publication.Scope, publication.ModelId, 1, Guid.NewGuid());
    }

    private sealed class FakeTimeProvider(DateTimeOffset? initial = null) : TimeProvider
    {
        private DateTimeOffset now = initial ?? new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan duration) => now = now.Add(duration);
    }
}
