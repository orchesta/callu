using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Source-generated log methods for EscalationOrchestrator — zero-allocation, compile-time template validation.
/// </summary>
internal static partial class EscalationOrchestratorLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting escalation processing run for {Count} incidents")]
    public static partial void EscalationRunStarted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing escalation for incident {IncidentId}")]
    public static partial void EscalationProcessingError(ILogger logger, Exception ex, Guid incidentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Escalation completed for incident {IncidentId} - all steps exhausted")]
    public static partial void EscalationCompleted(ILogger logger, Guid incidentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Triggering escalation step {StepLevel} for incident {IncidentId}")]
    public static partial void TriggeringEscalationStep(ILogger logger, int stepLevel, Guid incidentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Escalation triggered for incident {IncidentId} with policy {PolicyId}")]
    public static partial void EscalationTriggered(ILogger logger, Guid incidentId, Guid policyId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Escalation cancelled for incident {IncidentId}")]
    public static partial void EscalationCancelled(ILogger logger, Guid incidentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Escalation step {StepId} for incident {IncidentId} has no target (ScheduleId, TargetedUsers, TeamId all empty) — skipped")]
    public static partial void EscalationStepHasNoTarget(ILogger logger, Guid stepId, Guid incidentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Incident {IncidentId} has IsEscalationActive=true but {FieldName} is null — falling back to {Fallback}")]
    public static partial void EscalationTimestampMissing(ILogger logger, Guid incidentId, string fieldName, string fallback);
}
