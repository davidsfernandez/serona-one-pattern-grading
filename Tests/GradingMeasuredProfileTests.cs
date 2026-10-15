using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Grading.Worker;
using Microsoft.Extensions.Configuration;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingMeasuredProfileTests
{
    [Fact]
    public void PackagedOptInProfileBindsAndSupportsTheMeasuredEnvelope()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.GradingMeasuredAlpha.json");
        Assert.True(File.Exists(path), "The same opt-in configuration must be packaged with the worker.");
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        var options = GradingWorkerConfiguration.Create(configuration);
        var limits = options.ToWorkloadLimits();
        GradingWorkloadValidation.RequireWithin(GradingOperationalFixtures.Create("ordinary-curve", 32, 9), 535168, limits);
        Assert.Equal(1, options.MaximumGlobalActive);
        Assert.Equal(1, options.MaximumOwnerActive);
        Assert.Equal(2, options.MaximumPending);
        Assert.True(options.MaximumCalculationDuration > options.HeartbeatInterval);
        Assert.True(options.LeaseDuration > options.HeartbeatInterval);
        Assert.True(options.MaximumArtifactBytes >= 1128385);
    }
}
