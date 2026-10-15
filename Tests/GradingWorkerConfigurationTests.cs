using GestionProduccion.Grading.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GestionProduccion.Grading.Tests;

public sealed class GradingWorkerConfigurationTests
{
    [Fact]
    public void MissingWorkloadAndDurationConfigurationFailsClosed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GradingWorkerConfiguration.Create(Configuration(new Dictionary<string, string?>())));

        Assert.Equal("Invalid Grading:Worker configuration.", exception.Message);
    }

    [Fact]
    public void ExplicitConfigurationBindsAllRequiredLimitsAndDurations()
    {
        var options = GradingWorkerConfiguration.Create(Configuration(Values));

        Assert.Equal(7, options.MaximumPending);
        Assert.Equal(4096, options.MaximumArtifactBytes);
        Assert.Equal(TimeSpan.FromSeconds(10), options.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), options.MaximumCalculationDuration);
        Assert.Equal(1, options.MaximumGlobalActive);
        Assert.Equal(1, options.MaximumOwnerActive);
    }

    [Fact]
    public async Task ActivityHealthReportsStartingAsDegradedAndStoppedAsUnhealthy()
    {
        var options = GradingWorkerConfiguration.Create(Configuration(Values));
        var activity = new GradingWorkerActivity(TimeProvider.System);
        var check = new GradingWorkerHealthCheck(activity, options);

        Assert.Equal(HealthStatus.Degraded, (await check.CheckHealthAsync(new())).Status);
        activity.Stopped();
        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new())).Status);
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private static readonly IReadOnlyDictionary<string, string?> Values = new Dictionary<string, string?>
    {
        ["Grading:Worker:MaximumPending"] = "7", ["Grading:Worker:MaximumArtifactBytes"] = "4096",
        ["Grading:Worker:MaximumPieces"] = "2", ["Grading:Worker:MaximumSizes"] = "3", ["Grading:Worker:MaximumNodes"] = "4",
        ["Grading:Worker:ProofDepth"] = "5", ["Grading:Worker:ProofWork"] = "6", ["Grading:Worker:LeaseDuration"] = "00:00:10",
        ["Grading:Worker:PollInterval"] = "00:00:01", ["Grading:Worker:HeartbeatInterval"] = "00:00:01",
        ["Grading:Worker:ShutdownTimeout"] = "00:00:05", ["Grading:Worker:MaximumCalculationDuration"] = "00:00:02"
    };
}
