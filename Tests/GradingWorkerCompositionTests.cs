using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Application.Storage;
using GestionProduccion.Grading.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingWorkerCompositionTests
{
    [Fact]
    public void PublisherDependenciesResolveWithoutConnectingToTheDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "serona-grading-composition", Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContextFactory<GradingDbContext>(db => db.UseSqlite("Data Source=:memory:"));
        services.AddSingleton<IPrivateFileStorage>(_ => new LocalPrivateFileStorage(new PrivateFileStorageOptions { PrivateRoot = root }));
        services.AddSingleton(sp => new ModelingArtifactStore(sp.GetRequiredService<IPrivateFileStorage>(), 4096));
        services.AddSingleton<IGradingJobLifecycle, GradingJobLifecycleRepository>();
        services.AddSingleton<IModelingQueries, ModelingQueries>();
        services.AddSingleton<IGradingResultRepository, GradingResultRepository>();
        services.AddSingleton<IGradingResultPublisher, GradingResultPublicationService>();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.IsType<GradingResultPublicationService>(provider.GetRequiredService<IGradingResultPublisher>());
    }
}
