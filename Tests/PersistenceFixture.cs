using System.Security.Cryptography;
using System.Text;
using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GestionProduccion.Grading.Tests;

internal sealed class PersistenceFixture : IAsyncDisposable, IDbContextFactory<GradingDbContext>
{
    private readonly SqliteConnection keeper;
    internal DbContextOptions<GradingDbContext> Options { get; }
    internal static readonly ModelingScope Scope = new("company-a", "owner-a");
    private PersistenceFixture(SqliteConnection keeper, DbContextOptions<GradingDbContext> options)
    { this.keeper = keeper; Options = options; }
    internal static async Task<PersistenceFixture> CreateAsync(params IInterceptor[] interceptors)
    {
        var connection = new SqliteConnection($"Data Source=grading-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GradingDbContext>().UseSqlite(connection.ConnectionString).AddInterceptors(interceptors).Options;
        var f = new PersistenceFixture(connection, options);
        await using var context = f.CreateDbContext(); await context.Database.EnsureCreatedAsync();
        return f;
    }
    public GradingDbContext CreateDbContext() => new(Options);
    public Task<GradingDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
    public async ValueTask DisposeAsync() => await keeper.DisposeAsync();
    internal static PrivateModelingArtifact Artifact(string tag) => new($"grading/{Guid.NewGuid():N}",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tag))).ToLowerInvariant(), tag.Length);
    internal static ModelingImportPublication Publication(ModelingScope? scope = null, Guid? operation = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), operation ?? Guid.NewGuid(), Artifact("input").Sha256,
            scope ?? Scope, Artifact("original"), Artifact("configuration"), Artifact("fixed").Sha256);
}
