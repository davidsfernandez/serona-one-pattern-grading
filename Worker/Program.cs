using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Application.Storage;
using GestionProduccion.Grading.Persistence;
using GestionProduccion.Grading.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
var options = GradingWorkerConfiguration.Create(builder.Configuration);
var storageOptions = GradingWorkerConfiguration.CreatePrivateStorage(builder.Configuration);
var connectionString = GradingWorkerConfiguration.GetConnectionString(builder.Configuration);

builder.Services.AddSingleton(options);
builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = options.ShutdownTimeout);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GradingWorkerActivity>();
builder.Services.AddSingleton<IPrivateFileStorage>(_ => new LocalPrivateFileStorage(storageOptions));
builder.Services.AddSingleton(sp => new ModelingArtifactStore(sp.GetRequiredService<IPrivateFileStorage>(), options.MaximumArtifactBytes));
builder.Services.AddDbContextFactory<GradingDbContext>(db => db.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)),
    mysql => mysql.MigrationsHistoryTable("__GradingMigrationsHistory")));
builder.Services.AddSingleton<IGradingJobLifecycle, GradingJobLifecycleRepository>();
builder.Services.AddSingleton<IGradingCalculator>(sp => new GradingCalculationService(sp.GetRequiredService<ModelingArtifactStore>(), options.ToWorkloadLimits()));
builder.Services.AddSingleton<IModelingQueries, ModelingQueries>();
builder.Services.AddSingleton<IGradingResultRepository, GradingResultRepository>();
builder.Services.AddSingleton<IGradingResultPublisher, GradingResultPublicationService>();
builder.Services.AddSingleton<GradingHealthCheckPublisher>();
builder.Services.AddSingleton<IHealthCheckPublisher>(sp => sp.GetRequiredService<GradingHealthCheckPublisher>());
builder.Services.AddHealthChecks()
    .AddCheck<GradingPersistenceHealthCheck>("grading-persistence", tags: ["ready"])
    .AddCheck<GradingWorkerHealthCheck>("grading-activity", tags: ["ready"]);
builder.Services.Configure<HealthCheckPublisherOptions>(health =>
{
    health.Delay = TimeSpan.Zero;
    health.Period = options.HeartbeatInterval;
    health.Predicate = registration => registration.Tags.Contains("ready");
});
builder.Services.Configure<HostOptions>(host => host.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
builder.Services.AddHostedService<GradingJobWorker>();

await builder.Build().RunAsync();
