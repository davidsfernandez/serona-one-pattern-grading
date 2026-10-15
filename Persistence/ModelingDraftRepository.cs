using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionProduccion.Grading.Persistence;

public sealed class ModelingDraftRepository(IDbContextFactory<GradingDbContext> factory, TimeProvider clock, GradingExecutionPolicy policy,
    ILogger<ModelingDraftRepository>? logger = null) : IModelingDraftRepository
{
    public async Task<ModelingDraftSnapshot> SaveAsync(ModelingScope scope, Guid modelId, long expectedVersion, PrivateModelingArtifact artifact, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var doc = await ModelingWriteGuard.DocumentAsync(db, scope, modelId, ct); ModelingWriteGuard.RequireActive(doc);
        var draft = await db.Drafts.SingleAsync(d => d.ModelId == modelId, ct); RequireVersion(draft, expectedVersion);
        draft.Version = checked(draft.Version + 1); draft.ArtifactKey = artifact.Key; draft.ArtifactHash = artifact.Sha256;
        draft.ArtifactLength = artifact.Length; draft.UpdatedBy = scope.OwnerId; draft.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        ModelingWriteGuard.Touch(doc); ModelingWriteGuard.Audit(db, doc, "DraftSaved", draft.UpdatedAtUtc);
        await ModelingWriteGuard.CommitAsync(db, ct); return draft.Snapshot();
    }

    public async Task<GradingRequestSnapshot> RequestAsync(ModelingScope scope, Guid modelId, long expectedVersion, Guid operationId, CancellationToken ct = default)
    {
        var fingerprint = ModelingJson.RequestFingerprint("Calculate", modelId, expectedVersion);
        await using var db = await factory.CreateDbContextAsync(ct);
        var operation = await ModelingWriteGuard.OperationAsync(db, scope, operationId, ct);
        if (operation is not null)
        {
            ModelingWriteGuard.RequireReplay(operation, "Calculate", fingerprint, modelId);
            var replay = (await db.Jobs.SingleAsync(j => j.Id == operation.JobId && j.CompanyId == scope.CompanyId && j.OwnerId == scope.OwnerId, ct)).Snapshot();
            GradingPersistenceTelemetry.Admission(logger, replay.JobId, null, true);
            return replay;
        }
        var doc = await ModelingWriteGuard.DocumentAsync(db, scope, modelId, ct); ModelingWriteGuard.RequireActive(doc);
        var draft = await db.Drafts.SingleAsync(d => d.ModelId == modelId, ct); RequireVersion(draft, expectedVersion);
        policy.RequireValid();
        var admission = await db.Admission.SingleAsync(a => a.Id == 1, ct);
        // An accepted job retains its queue reservation while running, so release/recovery cannot overflow admission.
        var reservations = await db.Jobs.CountAsync(j => j.State == GradingJobState.Pending || j.State == GradingJobState.Running, ct);
        if (reservations >= policy.MaximumPending)
        {
            GradingPersistenceTelemetry.Capacity(logger, reservations);
            throw new ModelingPersistenceException("grading_queue_capacity");
        }
        admission.Version = checked(admission.Version + 1);
        var number = checked(await db.Revisions.Where(r => r.ModelId == modelId).MaxAsync(r => r.Number, ct) + 1);
        var now = clock.GetUtcNow().UtcDateTime;
        var revision = new ModelingRevisionRecord { Id = Guid.NewGuid(), ModelId = modelId, Number = number, DraftVersion = draft.Version,
            ArtifactKey = draft.ArtifactKey, ArtifactHash = draft.ArtifactHash, ArtifactLength = draft.ArtifactLength, CreatedBy = scope.OwnerId, CreatedAtUtc = now };
        var job = new GradingJobRecord { Id = Guid.NewGuid(), ModelId = modelId, RevisionId = revision.Id,
            CompanyId = scope.CompanyId, OwnerId = scope.OwnerId, State = GradingJobState.Pending, Version = 1, CreatedAtUtc = now };
        db.Revisions.Add(revision); db.Jobs.Add(job);
        db.Operations.Add(new() { Id = Guid.NewGuid(), ModelId = modelId, RevisionId = revision.Id, JobId = job.Id,
            CompanyId = scope.CompanyId, OwnerId = scope.OwnerId, OperationId = operationId, Kind = "Calculate", Fingerprint = fingerprint });
        ModelingWriteGuard.Touch(doc); ModelingWriteGuard.Audit(db, doc, "CalculationRequested", now, job.Id);
        // Revision, job, idempotency record and the archive fence share one EF transaction.
        await ModelingWriteGuard.CommitAsync(db, ct);
        GradingPersistenceTelemetry.Admission(logger, job.Id, reservations + 1, false);
        return job.Snapshot();
    }

    private static void RequireVersion(ModelingDraftRecord draft, long expected)
    { if (expected < 1 || draft.Version != expected) throw new ModelingPersistenceException("modeling_draft_conflict", expected, draft.Version); }
}
