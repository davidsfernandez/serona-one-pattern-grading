using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Persistence;

internal static class ModelingWriteGuard
{
    internal static IQueryable<ModelingDocumentRecord> Owned(GradingDbContext db, ModelingScope scope)
    {
        scope.RequireValid();
        return db.Documents.Where(d => d.CompanyId == scope.CompanyId && d.OwnerId == scope.OwnerId);
    }
    internal static async Task<ModelingDocumentRecord> DocumentAsync(GradingDbContext db, ModelingScope scope, Guid id, CancellationToken ct)
        => await Owned(db, scope).SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new ModelingPersistenceException("modeling_not_found");
    internal static void RequireActive(ModelingDocumentRecord doc)
    { if (doc.IsArchived) throw new ModelingPersistenceException("modeling_archived"); }
    internal static Task<ModelingOperationRecord?> OperationAsync(GradingDbContext db, ModelingScope scope, Guid id, CancellationToken ct)
    {
        scope.RequireValid();
        if (id == Guid.Empty) throw new ModelingPersistenceException("modeling_operation_invalid");
        return db.Operations.AsNoTracking().SingleOrDefaultAsync(o => o.CompanyId == scope.CompanyId && o.OwnerId == scope.OwnerId && o.OperationId == id, ct);
    }
    internal static void RequireReplay(ModelingOperationRecord operation, string kind, string fingerprint, Guid? modelId = null)
    {
        if (operation.Kind != kind || operation.Fingerprint != fingerprint || (modelId.HasValue && operation.ModelId != modelId))
            throw new ModelingPersistenceException("modeling_operation_conflict");
    }
    internal static void Touch(ModelingDocumentRecord doc) => doc.Version = checked(doc.Version + 1);
    internal static void Audit(GradingDbContext db, ModelingDocumentRecord doc, string action, DateTime now, Guid? resourceId = null)
        => db.Audit.Add(new() { Id = Guid.NewGuid(), ModelId = doc.Id, Action = action, ActorId = doc.OwnerId, AtUtc = now, ResourceId = resourceId });
    internal static async Task CommitAsync(GradingDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ModelingPersistenceException("modeling_write_conflict"); }
        catch (DbUpdateException) { throw new ModelingPersistenceException("modeling_persistence_failure"); }
    }
}
