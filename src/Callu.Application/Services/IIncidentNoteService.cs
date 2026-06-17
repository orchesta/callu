using Callu.Shared.Models.Incidents;

namespace Callu.Application.Services;

/// <summary>
/// Service for Incident Note CRUD operations.
/// Extracted from IIncidentService for single responsibility.
/// </summary>
public interface IIncidentNoteService
{
    /// <summary>
    /// Get all notes for an incident
    /// </summary>
    Task<IEnumerable<IncidentNoteDto>> GetNotesAsync(Guid incidentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a note to an incident
    /// </summary>
    Task<IncidentNoteDto> AddNoteAsync(Guid incidentId, CreateIncidentNoteRequest request, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a note
    /// </summary>
    Task<bool> UpdateNoteAsync(Guid noteId, UpdateIncidentNoteRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a note
    /// </summary>
    Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken = default);
}
