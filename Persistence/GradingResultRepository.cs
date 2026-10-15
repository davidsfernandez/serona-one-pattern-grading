using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Persistence;

public sealed class GradingResultRepository(IDbContextFactory<GradingDbContext> factory, TimeProvider clock) : IGradingResultRepository
{
    public async Task<GradingResultSnapshot> CompleteAsync(ModelingScope scope, Guid jobId, long expectedJobVersion, bool valid,
        PrivateModelingArtifact artifact, CancellationToken ct = default)
    {
        scope.RequireValid(); await using var db = await factory.CreateDbContextAsync(ct);
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId && j.CompanyId == scope.CompanyId && j.OwnerId == scope.OwnerId &&
            ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == j.ModelId), ct) ?? throw new ModelingPersistenceException("modeling_not_found");
        var previous = await db.Results.AsNoTracking().SingleOrDefaultAsync(r => r.JobId == jobId, ct);
        if (previous?.SourceLeaseToken is not null) throw new ModelingPersistenceException("grading_lease_required");
        if (previous is not null)
            return previous.SourceJobVersion == expectedJobVersion && previous.IsValid == valid && previous.ArtifactHash == artifact.Sha256
                ? previous.Snapshot() : throw new ModelingPersistenceException("modeling_result_conflict");
        var doc = await ModelingWriteGuard.DocumentAsync(db, scope, job.ModelId, ct); ModelingWriteGuard.RequireActive(doc);
        if (job.LeaseToken is not null) throw new ModelingPersistenceException("grading_lease_required");
        if (job.State != GradingJobState.Running || job.Version != expectedJobVersion || job.CancellationRequestedAtUtc is not null)
            throw new ModelingPersistenceException("modeling_job_conflict");
        var now = clock.GetUtcNow().UtcDateTime;
        var result = new GradingResultRecord { Id = Guid.NewGuid(), JobId = job.Id, ModelId = doc.Id, RevisionId = job.RevisionId,
            IsValid = valid, SourceJobVersion = expectedJobVersion, ArtifactKey = artifact.Key, ArtifactHash = artifact.Sha256,
            ArtifactLength = artifact.Length, CreatedAtUtc = now };
        db.Results.Add(result); job.State = valid ? GradingJobState.Succeeded : GradingJobState.Failed; job.Version = checked(job.Version + 1);
        ModelingWriteGuard.Touch(doc); ModelingWriteGuard.Audit(db, doc, "ResultPublished", now, result.Id);
        await ModelingWriteGuard.CommitAsync(db, ct); return result.Snapshot();
    }

    public async Task<GradingApprovalSnapshot> ApproveAsync(ModelingScope scope, Guid resultId, Guid operationId, CancellationToken ct = default)
    {
        var fingerprint = ModelingJson.RequestFingerprint("Approve", resultId);
        await using var db = await factory.CreateDbContextAsync(ct);
        var replay = await ModelingWriteGuard.OperationAsync(db, scope, operationId, ct);
        if (replay is not null)
        {
            ModelingWriteGuard.RequireReplay(replay, "Approve", fingerprint);
            return (await db.Approvals.SingleAsync(a => a.Id == replay.ApprovalId, ct)).Snapshot();
        }
        var result = await db.Results.SingleOrDefaultAsync(r => r.Id == resultId &&
            ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == r.ModelId), ct) ?? throw new ModelingPersistenceException("modeling_not_found");
        var doc = await ModelingWriteGuard.DocumentAsync(db, scope, result.ModelId, ct); ModelingWriteGuard.RequireActive(doc);
        if (!result.IsValid) throw new ModelingPersistenceException("modeling_result_not_approvable");
        var approval = new GradingApprovalRecord { Id = Guid.NewGuid(), ModelId = doc.Id, ResultId = result.Id,
            ApprovedBy = scope.OwnerId, ApprovedAtUtc = clock.GetUtcNow().UtcDateTime };
        db.Approvals.Add(approval); doc.CurrentApprovedResultId = result.Id; ModelingWriteGuard.Touch(doc);
        db.Operations.Add(new() { Id = Guid.NewGuid(), OperationId = operationId, Kind = "Approve", Fingerprint = fingerprint,
            ModelId = doc.Id, RevisionId = result.RevisionId, CompanyId = scope.CompanyId, OwnerId = scope.OwnerId, ApprovalId = approval.Id });
        ModelingWriteGuard.Audit(db, doc, "ApprovedCurrent", approval.ApprovedAtUtc, result.Id);
        await ModelingWriteGuard.CommitAsync(db, ct); return approval.Snapshot();
    }
}
