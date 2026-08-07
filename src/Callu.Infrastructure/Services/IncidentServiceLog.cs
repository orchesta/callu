using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Source-generated high-performance log messages for IncidentService.
/// </summary>
internal static partial class IncidentServiceLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Created incident {IncidentId}")]
    public static partial void IncidentCreated(ILogger logger, Guid incidentId);
    
    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-triggered escalation for incident {IncidentId} with policy {PolicyId}")]
    public static partial void EscalationAutoTriggered(ILogger logger, Guid incidentId, Guid policyId);
    
    [LoggerMessage(Level = LogLevel.Warning, Message = "Incident not found for update: {IncidentId}")]
    public static partial void IncidentNotFoundForUpdate(ILogger logger, Guid incidentId);
    
    [LoggerMessage(Level = LogLevel.Information, Message = "Updated incident {IncidentId}")]
    public static partial void IncidentUpdated(ILogger logger, Guid incidentId);
    
    [LoggerMessage(Level = LogLevel.Information, Message = "Escalation stopped for incident {IncidentId} due to acknowledgment")]
    public static partial void EscalationStoppedAck(ILogger logger, Guid incidentId);
    
    [LoggerMessage(Level = LogLevel.Information, Message = "Incident {IncidentId} acknowledged by {UserId}")]
    public static partial void IncidentAcknowledged(ILogger logger, Guid incidentId, string userId);
    
    [LoggerMessage(Level = LogLevel.Warning, Message = "Concurrency conflict while acknowledging incident {IncidentId}")]
    public static partial void AckConcurrencyConflict(ILogger logger, Exception ex, Guid incidentId);
    
    [LoggerMessage(Level = LogLevel.Information, Message = "Escalation stopped for incident {IncidentId} due to resolution")]
    public static partial void EscalationStoppedResolution(ILogger logger, Guid incidentId);
    
    [LoggerMessage(Level = LogLevel.Information, Message = "Incident {IncidentId} resolved by {UserId}")]
    public static partial void IncidentResolved(ILogger logger, Guid incidentId, string userId);
    
    [LoggerMessage(Level = LogLevel.Warning, Message = "Concurrency conflict while resolving incident {IncidentId}")]
    public static partial void ResolveConcurrencyConflict(ILogger logger, Exception ex, Guid incidentId);
    
    [LoggerMessage(Level = LogLevel.Warning, Message = "Incident not found for delete: {IncidentId}")]
    public static partial void IncidentNotFoundForDelete(ILogger logger, Guid incidentId);
    
    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted incident {IncidentId}")]
    public static partial void IncidentDeleted(ILogger logger, Guid incidentId);
    
    [LoggerMessage(Level = LogLevel.Information, Message = "Incident {IncidentId} manually escalated by {UserId}")]
    public static partial void IncidentManuallyEscalated(ILogger logger, Guid incidentId, string userId);
    
    [LoggerMessage(Level = LogLevel.Information, Message = "Incident {IncidentId} reassigned to {TargetUserId} by {AssignedBy}")]
    public static partial void IncidentReassigned(ILogger logger, Guid incidentId, string targetUserId, string assignedBy);
}
