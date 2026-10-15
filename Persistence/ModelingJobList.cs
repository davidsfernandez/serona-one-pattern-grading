using GestionProduccion.Application.ModelingGrading;
using Microsoft.EntityFrameworkCore;

namespace GestionProduccion.Grading.Persistence;

/// <summary>Rediscovers durable calculations without relying on browser-local job identities.</summary>
public sealed class ModelingJobList(IDbContextFactory<GradingDbContext> factory) : IModelingJobList
{
    public async Task<IReadOnlyList<GradingRequestSnapshot>> ListAsync(ModelingScope scope, Guid modelId, int skip, int take, CancellationToken ct = default)
    {
        scope.RequireValid();
        if (skip < 0 || take is < 1 or > 100) throw new ModelingPersistenceException("modeling_pagination_invalid");
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.Jobs.AsNoTracking().Where(j => j.ModelId == modelId && j.CompanyId == scope.CompanyId && j.OwnerId == scope.OwnerId &&
            ModelingWriteGuard.Owned(db, scope).Any(d => d.Id == j.ModelId))
            .OrderByDescending(j => j.CreatedAtUtc).ThenBy(j => j.Id).Skip(skip).Take(take).ToArrayAsync(ct)).Select(j => j.Snapshot()).ToArray();
    }
}
