using Microsoft.Extensions.Logging;

namespace GestionProduccion.Grading.Persistence;

/// <summary>Reports committed queue facts without adding queries or exposing scope and artifacts.</summary>
internal static class GradingPersistenceTelemetry
{
    internal static void Admission(ILogger? logger, Guid jobId, int? reservations, bool replay)
    {
        try { logger?.LogInformation("Grading admission {Outcome}: job {JobId}, reserved nonterminal slots {Reservations}.", replay ? "replayed" : "accepted", jobId, reservations); }
        catch (Exception) { }
    }

    internal static void Capacity(ILogger? logger, int reservations)
    {
        try { logger?.LogWarning("Grading admission rejected: {Reason}, reserved nonterminal slots {Reservations}.", "grading_queue_capacity", reservations); }
        catch (Exception) { }
    }

    internal static void Claim(ILogger? logger, Guid jobId, bool recovered, double jobAgeSeconds)
    {
        try { logger?.LogInformation("Grading durable claim: job {JobId}, recovered {Recovered}, age {JobAgeSeconds}s.", jobId, recovered, jobAgeSeconds); }
        catch (Exception) { }
    }

    internal static void ExpiredCancellations(ILogger? logger, int count)
    {
        if (count == 0) return;
        try { logger?.LogInformation("Grading recovered effective cancellations: {Count}.", count); }
        catch (Exception) { }
    }
}
