using GestionProduccion.Application.ModelingGrading;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Persistence;

public sealed class ModelingQueries(IDbContextFactory<GradingDbContext> factory) : IModelingQueries
{
    public Task<ModelingDocumentSnapshot?> DocumentAsync(ModelingScope scope, Guid id, CancellationToken ct = default)
        => Read(scope, async db => (await ModelingWriteGuard.Owned(db, scope).AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct))?.Snapshot(), ct);
    public Task<ModelingDraftSnapshot?> DraftAsync(ModelingScope scope, Guid id, CancellationToken ct = default)
        => Read(scope, async db => (await db.Drafts.AsNoTracking().SingleOrDefaultAsync(r => r.ModelId == id &&
            ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == r.ModelId), ct))?.Snapshot(), ct);
    public Task<ModelingRevisionSnapshot?> RevisionAsync(ModelingScope scope, Guid id, CancellationToken ct = default)
        => Read(scope, async db => (await db.Revisions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id &&
            ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == r.ModelId), ct))?.Snapshot(), ct);
    public Task<GradingRequestSnapshot?> JobAsync(ModelingScope scope, Guid id, CancellationToken ct = default)
        => Read(scope, async db => (await db.Jobs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id &&
            r.CompanyId == scope.CompanyId && r.OwnerId == scope.OwnerId &&
            ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == r.ModelId), ct))?.Snapshot(), ct);
    public Task<GradingResultSnapshot?> ResultAsync(ModelingScope scope, Guid id, CancellationToken ct = default)
        => Read(scope, async db => (await db.Results.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id &&
            ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == r.ModelId), ct))?.Snapshot(), ct);
    public Task<GradingResultSnapshot?> ResultForJobAsync(ModelingScope scope, Guid jobId, CancellationToken ct = default)
        => Read(scope, async db => (await db.Results.AsNoTracking().SingleOrDefaultAsync(r => r.JobId == jobId &&
            ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == r.ModelId), ct))?.Snapshot(), ct);
    public Task<ModelingOperationSnapshot?> OperationAsync(ModelingScope scope, Guid id, CancellationToken ct = default)
        => Read(scope, async db => (await ModelingWriteGuard.OperationAsync(db, scope, id, ct))?.Snapshot(), ct);
    public Task<IReadOnlyList<ModelingRevisionSnapshot>> RevisionsAsync(ModelingScope scope, Guid modelId, CancellationToken ct = default)
        => Read<IReadOnlyList<ModelingRevisionSnapshot>>(scope, async db => (await db.Revisions.AsNoTracking()
            .Where(r => r.ModelId == modelId && ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == r.ModelId))
            .OrderBy(r => r.Number).ToArrayAsync(ct)).Select(r => r.Snapshot()).ToArray(), ct);
    public Task<IReadOnlyList<GradingApprovalSnapshot>> ApprovalsAsync(ModelingScope scope, Guid modelId, CancellationToken ct = default)
        => Read<IReadOnlyList<GradingApprovalSnapshot>>(scope, async db => (await db.Approvals.AsNoTracking()
            .Where(r => r.ModelId == modelId && ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == r.ModelId))
            .OrderBy(r => r.ApprovedAtUtc).ThenBy(r => r.Id).ToArrayAsync(ct)).Select(r => r.Snapshot()).ToArray(), ct);
    public Task<IReadOnlyList<ModelingAuditSnapshot>> AuditAsync(ModelingScope scope, Guid modelId, CancellationToken ct = default)
        => Read<IReadOnlyList<ModelingAuditSnapshot>>(scope, async db => (await db.Audit.AsNoTracking()
            .Where(r => r.ModelId == modelId && ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == r.ModelId))
            .OrderBy(r => r.AtUtc).ThenBy(r => r.Id).ToArrayAsync(ct)).Select(r => r.Snapshot()).ToArray(), ct);
    private async Task<T> Read<T>(ModelingScope scope, Func<GradingDbContext, Task<T>> query, CancellationToken ct)
    { scope.RequireValid(); await using var db = await factory.CreateDbContextAsync(ct); return await query(db); }
}
