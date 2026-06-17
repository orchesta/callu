using Callu.Domain.Entities;
using Callu.Shared.Models.Conference;

namespace Callu.Application.Services;

/// <summary>
/// Service for managing video conference rooms triggered during incident calls.
/// </summary>
public interface IVideoConferenceService
{
    /// <summary>
    /// Creates a conference room for an incident and sends unique links to team members via SMS.
    /// Called when a responder presses 999 during an incident call.
    /// </summary>
    Task<ConferenceRoomResult> CreateRoomAsync(Guid incidentId, CancellationToken ct = default);
    
    /// <summary>
    /// Validates a participant token and returns participant/room info.
    /// Returns null if token is invalid, expired, or room has ended.
    /// </summary>
    Task<ParticipantInfoDto?> ValidateParticipantAsync(string participantToken, CancellationToken ct = default);
    
    /// <summary>
    /// Marks a participant as joined. Rejects if another session is active on same token.
    /// Returns the Voximplant one-time login key for Web SDK authentication.
    /// </summary>
    Task<JoinResultDto> JoinConferenceAsync(string participantToken, string? sourceIp = null, string? userAgent = null, CancellationToken ct = default);
    
    /// <summary>
    /// Marks a participant as left (no longer active).
    /// </summary>
    Task LeaveConferenceAsync(string participantToken, CancellationToken ct = default);
    
    /// <summary>
    /// Ends a conference room (marks as Ended, disconnects all participants).
    /// </summary>
    Task EndConferenceAsync(Guid roomId, CancellationToken ct = default);
    
    /// <summary>
    /// Gets active conference rooms for an incident.
    /// </summary>
    Task<ConferenceRoom?> GetActiveRoomForIncidentAsync(Guid incidentId, CancellationToken ct = default);

    /// <summary>
    /// Gets the active conference for a specific user. If the user is not a participant, creates a new token dynamically.
    /// </summary>
    Task<ActiveConferenceDto?> GetActiveConferenceForUserAsync(Guid incidentId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Gets a paginated list of conference rooms
    /// </summary>
    Task<(IEnumerable<ConferenceRoomDto> Items, int TotalCount)> GetConferenceRoomsPagedAsync(ConferenceRoomFilter filter, CancellationToken ct = default);
}
