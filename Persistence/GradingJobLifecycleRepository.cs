using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionProduccion.Grading.Persistence;

public sealed class GradingJobLifecycleRepository(IDbContextFactory<GradingDbContext> factory, TimeProvider clock,
    ILogger<GradingJobLifecycleRepository>? logger = null) : IGradingJobLifecycle
{
    public async Task<GradingWorkItem?> ClaimAsync(GradingExecutionPolicy policy, CancellationToken ct = default)
    {
        policy.RequireValid(); var now = clock.GetUtcNow().UtcDateTime;
        await using var db = await factory.CreateDbContextAsync(ct);
        await GradingLeaseGuard.FenceAdmissionAsync(db, ct);
        var expiredCancellation = await db.Jobs.Where(j => j.State == GradingJobState.Running && j.LeaseExpiresAtUtc <= now && j.CancellationRequestedAtUtc != null).ToArrayAsync(ct);
        foreach (var j in expiredCancellation)
        { GradingLeaseGuard.Cancel(j, now); await GradingLeaseGuard.TouchDocumentAsync(db, j, "CancellationEffective", now, ct); }
        var active = db.Jobs.Where(j => j.State == GradingJobState.Running && j.LeaseExpiresAtUtc > now);
        GradingJobRecord? selected = null;
        if (await active.CountAsync(ct) < policy.MaximumGlobalActive)
            selected = await db.Jobs.Where(j => (j.State == GradingJobState.Pending ||
                j.State == GradingJobState.Running && j.LeaseExpiresAtUtc <= now) && j.CancellationRequestedAtUtc == null &&
                db.Documents.Any(d => d.Id == j.ModelId && !d.IsArchived && d.CompanyId == j.CompanyId && d.OwnerId == j.OwnerId) &&
                active.Count(a => a.CompanyId == j.CompanyId && a.OwnerId == j.OwnerId) < policy.MaximumOwnerActive)
                .OrderBy(j => j.CreatedAtUtc).ThenBy(j => j.Id).FirstOrDefaultAsync(ct);
        if (selected is null)
        {
            if (expiredCancellation.Length > 0) await ModelingWriteGuard.CommitAsync(db, ct);
            GradingPersistenceTelemetry.ExpiredCancellations(logger, expiredCancellation.Length);
            return null;
        }
        var recovered = selected.State == GradingJobState.Running;
        selected.State = GradingJobState.Running; selected.Version = checked(selected.Version + 1);
        selected.ClaimVersion = selected.Version; selected.LeaseToken = Guid.NewGuid(); selected.LeaseExpiresAtUtc = now + policy.LeaseDuration;
        await GradingLeaseGuard.TouchDocumentAsync(db, selected, "CalculationClaimed", now, ct);
        var revision = await db.Revisions.SingleAsync(r => r.Id == selected.RevisionId && r.ModelId == selected.ModelId, ct);
        await ModelingWriteGuard.CommitAsync(db, ct);
        GradingPersistenceTelemetry.ExpiredCancellations(logger, expiredCancellation.Length);
        GradingPersistenceTelemetry.Claim(logger, selected.Id, recovered, Math.Max(0, (now - selected.CreatedAtUtc).TotalSeconds));
        return new(selected.Id, selected.ModelId, selected.RevisionId, new(selected.CompanyId, selected.OwnerId),
            selected.LeaseToken.Value, selected.ClaimVersion, new(revision.ArtifactKey, revision.ArtifactHash, revision.ArtifactLength));
    }

    public async Task<GradingLeaseObservation> RenewAsync(GradingWorkItem work, GradingExecutionPolicy policy, CancellationToken ct = default)
    {
        policy.RequireValid(); await using var db = await factory.CreateDbContextAsync(ct); var now = clock.GetUtcNow().UtcDateTime;
        var job = await GradingLeaseGuard.JobAsync(db, work, ct);
        if (job is null || !GradingLeaseGuard.Current(job, work, now)) return GradingLeaseObservation.Lost;
        await GradingLeaseGuard.FenceAdmissionAsync(db, ct); job.LeaseExpiresAtUtc = now + policy.LeaseDuration; job.Version++;
        await ModelingWriteGuard.CommitAsync(db, ct);
        return job.CancellationRequestedAtUtc is null ? GradingLeaseObservation.Current : GradingLeaseObservation.CancellationRequested;
    }

    public Task<bool> ReleaseAsync(GradingWorkItem work, CancellationToken ct = default) => FinishLeaseAsync(work, false, null, ct);
    public Task<bool> AcknowledgeCancellationAsync(GradingWorkItem work, CancellationToken ct = default) => FinishLeaseAsync(work, true, null, ct);
    public Task<bool> FailAsync(GradingWorkItem work, string diagnosticCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(diagnosticCode) || diagnosticCode.Length > 160) throw new ModelingPersistenceException("grading_diagnostic_invalid");
        return FinishLeaseAsync(work, false, diagnosticCode, ct);
    }
    private async Task<bool> FinishLeaseAsync(GradingWorkItem work, bool requireCancellation, string? diagnostic, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var now = clock.GetUtcNow().UtcDateTime;
        var job = await GradingLeaseGuard.JobAsync(db, work, ct);
        if (job is null || !GradingLeaseGuard.Current(job, work, now) || requireCancellation && job.CancellationRequestedAtUtc is null) return false;
        await GradingLeaseGuard.FenceAdmissionAsync(db, ct);
        if (job.CancellationRequestedAtUtc is not null) GradingLeaseGuard.Cancel(job, now);
        else { job.State = diagnostic is null ? GradingJobState.Pending : GradingJobState.Failed; job.LastDiagnosticCode = diagnostic; job.Version++; GradingLeaseGuard.ClearLease(job); }
        await GradingLeaseGuard.TouchDocumentAsync(db, job, job.State == GradingJobState.Cancelled ? "CancellationEffective" : diagnostic is null ? "CalculationReleased" : "CalculationFailed", now, ct);
        await ModelingWriteGuard.CommitAsync(db, ct); return true;
    }

    public async Task<GradingCancellationSnapshot> CancelAsync(ModelingScope scope, Guid jobId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId && j.CompanyId == scope.CompanyId && j.OwnerId == scope.OwnerId &&
            ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == j.ModelId), ct) ?? throw new ModelingPersistenceException("modeling_not_found");
        if ((job.State is GradingJobState.Pending or GradingJobState.Running) && job.CancellationRequestedAtUtc is null)
        {
            var now = clock.GetUtcNow().UtcDateTime; await GradingLeaseGuard.FenceAdmissionAsync(db, ct);
            job.CancellationRequestedAtUtc = now; job.CancellationRequestedBy = scope.OwnerId;
            if (job.State == GradingJobState.Pending) GradingLeaseGuard.Cancel(job, now); else job.Version++;
            await GradingLeaseGuard.TouchDocumentAsync(db, job, job.State == GradingJobState.Cancelled ? "CancellationEffective" : "CancellationRequested", now, ct);
            await ModelingWriteGuard.CommitAsync(db, ct);
        }
        return new(job.Id, job.State, job.CancellationRequestedBy, Utc(job.CancellationRequestedAtUtc), Utc(job.CancelledAtUtc));
    }

    public async Task<GradingResultSnapshot?> CompleteAsync(GradingWorkItem work, bool valid, PrivateModelingArtifact artifact, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var now = clock.GetUtcNow().UtcDateTime;
        var job = await GradingLeaseGuard.JobAsync(db, work, ct) ?? throw new ModelingPersistenceException("modeling_not_found");
        if (job.ModelId != work.ModelId || job.RevisionId != work.RevisionId || job.ClaimVersion != work.ClaimVersion)
            throw new ModelingPersistenceException("grading_fence_lost");
        var previous = await db.Results.SingleOrDefaultAsync(r => r.JobId == work.JobId, ct);
        if (previous is not null && previous.SourceLeaseToken != work.LeaseToken)
            throw new ModelingPersistenceException("grading_fence_lost");
        if (previous is not null)
            return previous.SourceJobVersion == work.ClaimVersion && previous.SourceLeaseToken == work.LeaseToken && previous.ArtifactHash == artifact.Sha256 && previous.IsValid == valid
                ? previous.Snapshot() : throw new ModelingPersistenceException("modeling_result_conflict");
        if (!GradingLeaseGuard.Current(job, work, now)) throw new ModelingPersistenceException("grading_fence_lost");
        await GradingLeaseGuard.FenceAdmissionAsync(db, ct);
        if (job.CancellationRequestedAtUtc is not null)
        {
            GradingLeaseGuard.Cancel(job, now); await GradingLeaseGuard.TouchDocumentAsync(db, job, "CancellationEffective", now, ct);
            await ModelingWriteGuard.CommitAsync(db, ct); return null;
        }
        var doc = await ModelingWriteGuard.DocumentAsync(db, work.Scope, work.ModelId, ct); ModelingWriteGuard.RequireActive(doc);
        var result = new GradingResultRecord { Id = Guid.NewGuid(), JobId = job.Id, ModelId = job.ModelId, RevisionId = job.RevisionId,
            SourceJobVersion = work.ClaimVersion, SourceLeaseToken = work.LeaseToken, IsValid = valid, ArtifactKey = artifact.Key, ArtifactHash = artifact.Sha256, ArtifactLength = artifact.Length, CreatedAtUtc = now };
        db.Results.Add(result); job.State = valid ? GradingJobState.Succeeded : GradingJobState.Failed; job.Version++; GradingLeaseGuard.ClearLease(job);
        ModelingWriteGuard.Touch(doc); ModelingWriteGuard.Audit(db, doc, "ResultPublished", now, result.Id);
        await ModelingWriteGuard.CommitAsync(db, ct); return result.Snapshot();
    }
    private static DateTimeOffset? Utc(DateTime? value) => value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
