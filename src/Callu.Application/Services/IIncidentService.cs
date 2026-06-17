using Callu.Shared.Models.Incidents;
using Callu.Shared.Results;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for Incident command operations (CRUD + workflow).
/// For read-only queries and dashboard, use IIncidentQueryService.
/// </summary>
public interface IIncidentService
{
    /// <summary>
    /// Get incidents with optional filter
    /// </summary>
    Task<IEnumerable<IncidentListItemDto>> GetIncidentsAsync(IncidentFilter? filter = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get incidents with filtering and pagination
    /// </summary>
    Task<PagedResult<IncidentListItemDto>> GetIncidentsPagedAsync(IncidentFilter filter, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get incident by ID
    /// </summary>
    Task<IncidentDto?> GetIncidentByIdAsync(Guid incidentId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Persist an incident or report that an active maintenance window suppressed
    /// the create. Returns an envelope so callers can distinguish between the two
    /// (the old contract returned a dummy IncidentDto with Id = Guid.Empty,
    /// which forced every caller to inspect a magic string). Fix 02.G4 / 11.G12.
    /// </summary>
    Task<IncidentCreateResult> CreateIncidentAsync(CreateIncidentRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update an existing incident
    /// </summary>
    Task UpdateIncidentAsync(Guid incidentId, UpdateIncidentRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Acknowledge an incident
    /// </summary>
    Task AcknowledgeIncidentAsync(Guid incidentId, string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resolve an incident
    /// </summary>
    Task ResolveIncidentAsync(Guid incidentId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Close a resolved incident. Terminal — no further workflow transitions.
    /// </summary>
    Task CloseIncidentAsync(Guid incidentId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopen a resolved or closed incident back to Open. Escalation is NOT restarted automatically.
    /// </summary>
    Task ReopenIncidentAsync(Guid incidentId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an incident (soft delete)
    /// </summary>
    Task DeleteIncidentAsync(Guid incidentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually escalate an incident
    /// </summary>
    Task EscalateIncidentAsync(Guid incidentId, string userId, string? reason = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Reassign an incident to a different user
    /// </summary>
    Task ReassignIncidentAsync(Guid incidentId, string targetUserId, string assignedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// List outbound webhook delivery attempts for an incident (newest first).
    /// Surfaced on the incident detail page so operators see retry status and
    /// failure reasons for ACK callbacks. Fix 10.P1-7.
    /// </summary>
    Task<IReadOnlyList<WebhookDeliveryDto>> GetWebhookDeliveriesAsync(Guid incidentId, int limit = 20, CancellationToken cancellationToken = default);
}
