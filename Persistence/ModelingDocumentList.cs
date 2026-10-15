using GestionProduccion.Application.ModelingGrading;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Persistence;

public sealed class ModelingDocumentList(IDbContextFactory<GradingDbContext> factory) : IModelingDocumentList
{
    public async Task<IReadOnlyList<ModelingDocumentSnapshot>> ListAsync(ModelingScope scope, bool archived, int skip, int take, CancellationToken ct = default)
    {
        scope.RequireValid();
        if (skip < 0 || take is < 1 or > 100) throw new ModelingPersistenceException("modeling_pagination_invalid");
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await ModelingWriteGuard.Owned(db, scope).AsNoTracking().Where(d => d.IsArchived == archived)
            .OrderByDescending(d => d.CreatedAtUtc).ThenBy(d => d.Id).Skip(skip).Take(take).ToArrayAsync(ct))
            .Select(d => d.Snapshot()).ToArray();
    }
}
