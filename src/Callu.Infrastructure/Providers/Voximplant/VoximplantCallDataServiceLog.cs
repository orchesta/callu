using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Providers.Voximplant;

/// <summary>
/// Source-generated log methods for VoximplantCallDataService — zero-allocation, compile-time template validation.
/// </summary>
internal static partial class VoximplantCallDataServiceLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Created call token for incident {IncidentId}, expires in {Minutes} min")]
    public static partial void CallTokenCreated(ILogger logger, string? incidentId, double minutes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Call token not found or already consumed: {Token}")]
    public static partial void CallTokenNotFound(ILogger logger, string token);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Call token expired: created {CreatedAt}, expired {ExpiresAt}")]
    public static partial void CallTokenExpired(ILogger logger, DateTime createdAt, DateTime expiresAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Call token consumed for incident {IncidentId}")]
    public static partial void CallTokenConsumed(ILogger logger, string? incidentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Validating scenario API key: incoming={IncomingKey} (providers: {Count})")]
    public static partial void ValidatingScenarioApiKey(ILogger logger, string incomingKey, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Comparing keys: stored={StoredKey} vs incoming={IncomingKey} match={Match}")]
    public static partial void ComparingApiKeys(ILogger logger, string storedKey, string incomingKey, bool match);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No provisioning.scenarioApiKey found in config for provider {Id}")]
    public static partial void NoScenarioApiKeyInConfig(ILogger logger, Guid id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse config for provider {Id}")]
    public static partial void FailedToParseProviderConfig(ILogger logger, Exception ex, Guid id);

    [LoggerMessage(Level = LogLevel.Information, Message = "VoxEngine callback: IncidentId={IncidentId} Status={Status} Duration={Duration}s")]
    public static partial void VoxEngineCallback(ILogger logger, string? incidentId, string status, int duration);

    [LoggerMessage(Level = LogLevel.Information, Message = "Incident {IncidentId} acknowledged via phone call")]
    public static partial void IncidentAcknowledgedViaCall(ILogger logger, Guid incidentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Incident {IncidentId} escalation requested via phone call")]
    public static partial void IncidentEscalationRequested(ILogger logger, Guid incidentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Call to incident {IncidentId} ended with status {Status}, attempt {Attempt}/{MaxAttempts}")]
    public static partial void CallEndedWithStatus(ILogger logger, Guid incidentId, string status, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Max retry attempts reached for incident {IncidentId}, phone {Phone}")]
    public static partial void MaxRetryAttemptsReached(ILogger logger, Guid incidentId, string phone);

    [LoggerMessage(Level = LogLevel.Information, Message = "Call connected for incident {IncidentId}")]
    public static partial void CallConnected(ILogger logger, Guid incidentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduling retry #{Attempt} for incident {IncidentId} in {Delay}s")]
    public static partial void SchedulingRetry(ILogger logger, int attempt, Guid incidentId, int delay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retry cancelled for incident {IncidentId} — already {Status}")]
    public static partial void RetryCancelled(ILogger logger, Guid incidentId, string? status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retry #{Attempt} triggered for incident {IncidentId}, calling {Phone}")]
    public static partial void RetryTriggered(ILogger logger, int attempt, Guid incidentId, string phone);

    [LoggerMessage(Level = LogLevel.Information, Message = "Retry call #{Attempt} initiated for incident {IncidentId}: {Message}")]
    public static partial void RetryCallInitiated(ILogger logger, int attempt, Guid incidentId, string? message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retry call #{Attempt} failed for incident {IncidentId}: {Message}")]
    public static partial void RetryCallFailed(ILogger logger, int attempt, Guid incidentId, string? message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to execute retry for incident {IncidentId}")]
    public static partial void RetryExecutionFailed(ILogger logger, Exception ex, Guid incidentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cleaned {Count} expired/consumed call tokens")]
    public static partial void CleanedExpiredTokens(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to clean expired call tokens")]
    public static partial void CleanupFailed(ILogger logger, Exception ex);
}
