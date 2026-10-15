using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Persistence;

public sealed class ModelingImportRepository(IDbContextFactory<GradingDbContext> factory, TimeProvider clock) : IModelingImportRepository
{
    public async Task<ModelingDocumentSnapshot> ImportAsync(ModelingImportPublication p, CancellationToken ct = default)
    {
        p.Scope.RequireValid();
        await using var db = await factory.CreateDbContextAsync(ct);
        var replay = await ModelingWriteGuard.OperationAsync(db, p.Scope, p.OperationId, ct);
        if (replay is not null)
        {
            ModelingWriteGuard.RequireReplay(replay, "Import", p.Fingerprint);
            return (await ModelingWriteGuard.DocumentAsync(db, p.Scope, replay.ModelId, ct)).Snapshot();
        }
        if (p.ModelId == Guid.Empty || p.RevisionId == Guid.Empty) throw new ModelingPersistenceException("modeling_identity_invalid");
        var now = clock.GetUtcNow().UtcDateTime;
        var doc = new ModelingDocumentRecord { Id = p.ModelId, CompanyId = p.Scope.CompanyId, OwnerId = p.Scope.OwnerId,
            Version = 1, OriginalKey = p.Original.Key, OriginalHash = p.Original.Sha256, OriginalLength = p.Original.Length,
            FixedGeometryHash = p.FixedGeometryHash, CreatedAtUtc = now };
        db.Documents.Add(doc);
        db.Drafts.Add(new() { ModelId = doc.Id, Version = 1, ArtifactKey = p.Configuration.Key, ArtifactHash = p.Configuration.Sha256,
            ArtifactLength = p.Configuration.Length, UpdatedBy = p.Scope.OwnerId, UpdatedAtUtc = now });
        db.Revisions.Add(new() { Id = p.RevisionId, ModelId = doc.Id, Number = 1, DraftVersion = 1, ArtifactKey = p.Configuration.Key,
            ArtifactHash = p.Configuration.Sha256, ArtifactLength = p.Configuration.Length, CreatedBy = p.Scope.OwnerId, CreatedAtUtc = now });
        db.Operations.Add(new() { Id = Guid.NewGuid(), OperationId = p.OperationId, CompanyId = p.Scope.CompanyId, OwnerId = p.Scope.OwnerId,
            Kind = "Import", Fingerprint = p.Fingerprint, ModelId = doc.Id, RevisionId = p.RevisionId });
        ModelingWriteGuard.Audit(db, doc, "Imported", now, p.RevisionId);
        // EF commits every publication row together; private bytes already exist and remain immutable.
        await ModelingWriteGuard.CommitAsync(db, ct);
        return doc.Snapshot();
    }
}
