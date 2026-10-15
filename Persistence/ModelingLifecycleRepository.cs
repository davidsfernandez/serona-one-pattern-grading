using GestionProduccion.Application.ModelingGrading;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Persistence;

public sealed class ModelingLifecycleRepository(IDbContextFactory<GradingDbContext> factory, TimeProvider clock) : IModelingLifecycleRepository
{
    public async Task<ModelingDocumentSnapshot> SetArchivedAsync(ModelingScope scope, Guid modelId, bool archived, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var doc = await ModelingWriteGuard.DocumentAsync(db, scope, modelId, ct);
        if (doc.IsArchived == archived) return doc.Snapshot();
        if (archived)
        {
            var blocking = await db.Jobs.Where(j => j.ModelId == modelId && j.State != GradingJobState.Succeeded &&
                j.State != GradingJobState.Failed && j.State != GradingJobState.Cancelled).OrderBy(j => j.CreatedAtUtc).ThenBy(j => j.Id)
                .Select(j => j.Id).ToArrayAsync(ct);
            if (blocking.Length > 0) throw new ModelingPersistenceException("modeling_jobs_block_archive") { BlockingJobIds = blocking };
        }
        doc.IsArchived = archived; ModelingWriteGuard.Touch(doc);
        ModelingWriteGuard.Audit(db, doc, archived ? "Archived" : "Reactivated", clock.GetUtcNow().UtcDateTime);
        await ModelingWriteGuard.CommitAsync(db, ct); return doc.Snapshot();
    }
}
