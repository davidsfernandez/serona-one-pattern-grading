using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Persistence;

public sealed class GradingNestingHandoffRepository(IDbContextFactory<GradingDbContext> factory, TimeProvider clock)
    : IGradingNestingHandoffRepository
{
    public async Task<GradingNestingHandoffSnapshot?> OperationAsync(ModelingScope scope, Guid operationId, CancellationToken ct = default)
    {
        scope.RequireValid();
        if (operationId == Guid.Empty) throw new ModelingPersistenceException("modeling_operation_invalid");
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await Owned(db, scope).SingleOrDefaultAsync(h => h.OperationId == operationId, ct);
        return row is null ? null : Snapshot(row);
    }

    public async Task<GradingNestingHandoffSnapshot?> GetAsync(ModelingScope scope, Guid id, CancellationToken ct = default)
    {
        scope.RequireValid(); await using var db = await factory.CreateDbContextAsync(ct);
        var row = await Owned(db, scope).SingleOrDefaultAsync(h => h.Id == id, ct);
        return row is null ? null : Snapshot(row);
    }

    public async Task<GradingNestingHandoffSnapshot> PublishAsync(ModelingScope scope,
        GradingNestingHandoffPublication publication, CancellationToken ct = default)
    {
        scope.RequireValid(); var snapshot = publication.Snapshot; var source = snapshot.Source;
        if (source.OwnerId != scope.OwnerId || source.CompanyId != scope.CompanyId)
            throw new ModelingPersistenceException("modeling_not_found");
        if (snapshot.OperationId == Guid.Empty) throw new ModelingPersistenceException("modeling_operation_invalid");
        await using var db = await factory.CreateDbContextAsync(ct);
        var replay = await Owned(db, scope).SingleOrDefaultAsync(h => h.OperationId == snapshot.OperationId, ct);
        if (replay is not null) return RequireReplay(replay, snapshot);
        var doc = await ModelingWriteGuard.DocumentAsync(db, scope, source.ModelId, ct);
        ModelingWriteGuard.RequireActive(doc);
        if (doc.Version != publication.ExpectedDocumentVersion)
            throw new ModelingPersistenceException("modeling_write_conflict", publication.ExpectedDocumentVersion, doc.Version);
        await RequireApprovedSourceAsync(db, scope, source, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var row = new GradingNestingHandoffRecord { Id = source.HandoffId, OperationId = snapshot.OperationId,
            ModelId = source.ModelId, RevisionId = source.RevisionId, ResultId = source.ResultId, ApprovalId = source.ApprovalId,
            CompanyId = scope.CompanyId, OwnerId = scope.OwnerId, Fingerprint = snapshot.Fingerprint,
            ArtifactKey = snapshot.Artifact.Key, ArtifactHash = snapshot.Artifact.Sha256, ArtifactLength = snapshot.Artifact.Length,
            GeometryHash = source.GeometryHash, CreatedAtUtc = now };
        db.Handoffs.Add(row);
        // Handoff publication and archive compete on the same document token in one transaction.
        ModelingWriteGuard.Touch(doc); ModelingWriteGuard.Audit(db, doc, "NestingHandoffPublished", now, row.Id);
        await ModelingWriteGuard.CommitAsync(db, ct);
        return Snapshot(row);
    }

    private static IQueryable<GradingNestingHandoffRecord> Owned(GradingDbContext db, ModelingScope scope)
        => db.Handoffs.AsNoTracking().Where(h => h.CompanyId == scope.CompanyId && h.OwnerId == scope.OwnerId);

    private static async Task RequireApprovedSourceAsync(GradingDbContext db, ModelingScope scope,
        GradingHandoffReference source, CancellationToken ct)
    {
        var result = await db.Results.AsNoTracking().SingleOrDefaultAsync(r => r.Id == source.ResultId &&
            r.ModelId == source.ModelId && r.RevisionId == source.RevisionId &&
            ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == r.ModelId), ct);
        if (result is null) throw new ModelingPersistenceException("modeling_not_found");
        if (!result.IsValid) throw new ModelingPersistenceException("modeling_result_not_approvable");
        var approved = await db.Approvals.AsNoTracking().AnyAsync(a => a.Id == source.ApprovalId &&
            a.ModelId == source.ModelId && a.ResultId == source.ResultId &&
            ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == a.ModelId), ct);
        if (!approved) throw new ModelingPersistenceException("modeling_result_not_approvable");
    }

    private static GradingNestingHandoffSnapshot RequireReplay(GradingNestingHandoffRecord row, GradingNestingHandoffSnapshot requested)
    {
        var original = Snapshot(row); var source = requested.Source;
        // Storage locations and generated identifiers may differ on a concurrent retry; content and provenance cannot.
        if (row.Fingerprint != requested.Fingerprint || row.ModelId != source.ModelId || row.RevisionId != source.RevisionId ||
            row.ResultId != source.ResultId || row.ApprovalId != source.ApprovalId || row.GeometryHash != source.GeometryHash ||
            row.ArtifactHash != requested.Artifact.Sha256 || row.ArtifactLength != requested.Artifact.Length)
            throw new ModelingPersistenceException("modeling_operation_conflict");
        return original;
    }

    private static GradingNestingHandoffSnapshot Snapshot(GradingNestingHandoffRecord row)
        => new(new(row.Id, row.ModelId, row.RevisionId, row.ResultId, row.ApprovalId, row.OwnerId, row.CompanyId, row.GeometryHash),
            row.OperationId, row.Fingerprint, new(row.ArtifactKey, row.ArtifactHash, row.ArtifactLength));
}
