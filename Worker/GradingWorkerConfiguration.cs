using GestionProduccion.Application.ModelingGrading;
using GestionProduccion.Application.Storage;
using Microsoft.Extensions.Configuration;

namespace GestionProduccion.Grading.Worker;

/// <summary>Reads explicit worker configuration without returning sensitive configuration values in diagnostics.</summary>
public static class GradingWorkerConfiguration
{
    public static GradingWorkerOptions Create(IConfiguration configuration)
    {
        try
        {
            var options = configuration.GetSection(GradingWorkerOptions.SectionName).Get<GradingWorkerOptions>() ?? new GradingWorkerOptions();
            options.RequireValid();
            return options;
        }
        catch (Exception error) when (error is ModelingPersistenceException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new InvalidOperationException("Invalid Grading:Worker configuration.");
        }
    }

    public static PrivateFileStorageOptions CreatePrivateStorage(IConfiguration configuration)
    {
        try
        {
            var options = configuration.GetSection(PrivateFileStorageOptions.SectionName).Get<PrivateFileStorageOptions>()
                ?? throw new InvalidOperationException();
            _ = options.ValidateAndGetRoot();
            return options;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            throw new InvalidOperationException("Invalid private storage configuration for the grading worker.");
        }
    }

    public static string GetConnectionString(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Nesting") ?? configuration["NESTING_CONNECTION_STRING"];
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("A grading worker connection string must be explicitly configured.");
        return connection;
    }
}
