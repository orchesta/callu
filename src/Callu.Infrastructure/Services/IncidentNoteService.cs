using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Exceptions;
using Callu.Shared.Models.Incidents;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Handles Incident Note CRUD operations.
/// Extracted from IncidentService for single responsibility.
/// </summary>
public class IncidentNoteService(
    IIncidentNoteRepository noteRepo,
    IIncidentRepository incidentRepo,
    IIncidentTimelineEventRepository timelineRepo,
    ITeamMemberRepository teamMemberRepo,
    ITransactionManager transactionManager,
    ICurrentUserService currentUser,
    ILogger<IncidentNoteService> logger) : IIncidentNoteService
{
    public async Task<IEnumerable<IncidentNoteDto>> GetNotesAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        if (!await CanAccessIncidentAsync(incidentId, cancellationToken))
            throw new NotFoundException("Incident", incidentId);

        var notes = await noteRepo.GetQueryable()
            .AsNoTracking()
            .Where(n => n.IncidentId == incidentId && !n.IsDeleted)
            .OrderByDescending(n => n.IsPinned)
            .ThenByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);

        return notes.Select(n => n.Adapt<IncidentNoteDto>()).ToList();
    }

    public async Task<IncidentNoteDto> AddNoteAsync(Guid incidentId, CreateIncidentNoteRequest request, string userId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            if (!await CanAccessIncidentAsync(incidentId, cancellationToken))
                throw new NotFoundException($"Incident {incidentId} not found");

            var note = new IncidentNote
            {
                IncidentId = incidentId,
                Content = request.Content.Trim(),
                IsInternal = request.IsInternal,
                CreatedBy = userId
            };

            await noteRepo.AddAsync(note, cancellationToken);

            await timelineRepo.AddAsync(new IncidentTimelineEvent
            {
                IncidentId = incidentId,
                EventType = TimelineEventType.NoteAdded,
                Title = "Note Added",
                Description = request.IsInternal ? "Internal note added" : "Note added",
                ActorUserId = userId
            }, cancellationToken);

            logger.LogInformation("Added note {NoteId} to incident {IncidentId}", note.Id, incidentId);
            return note.Adapt<IncidentNoteDto>();
        }, cancellationToken);
    }

    public async Task<bool> UpdateNoteAsync(Guid noteId, UpdateIncidentNoteRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var note = await noteRepo.FindSingleAsync(n => n.Id == noteId && !n.IsDeleted, cancellationToken);

            if (note == null) return false;
            if (!await CanAccessIncidentAsync(note.IncidentId, cancellationToken))
                throw new NotFoundException($"Note {noteId} not found");

            note.Content = request.Content.Trim();
            note.IsPinned = request.IsPinned;
            note.UpdatedAt = DateTime.UtcNow;

            logger.LogInformation("Updated note {NoteId}", noteId);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var note = await noteRepo.FindSingleAsync(n => n.Id == noteId && !n.IsDeleted, cancellationToken);

            if (note == null) return false;
            if (!await CanAccessIncidentAsync(note.IncidentId, cancellationToken))
                throw new NotFoundException($"Note {noteId} not found");

            note.IsDeleted = true;
            note.UpdatedAt = DateTime.UtcNow;

            logger.LogInformation("Deleted note {NoteId}", noteId);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Mirrors <c>IncidentService.ApplyTeamScopingAsync</c>: unauthenticated callers
    /// (background jobs, webhooks) and Admin/Owner roles see all incidents; others
    /// see only their team's incidents plus unassigned (TeamId IS NULL). Returns
    /// 404 rather than 403 at the call sites to avoid leaking the existence of
    /// out-of-scope incidents.
    /// </summary>
    private async Task<bool> CanAccessIncidentAsync(Guid incidentId, CancellationToken cancellationToken)
    {
        var incidentTeamId = await incidentRepo.GetQueryable()
            .AsNoTracking()
            .Where(i => i.Id == incidentId && !i.IsDeleted)
            .Select(i => new { Found = true, i.TeamId })
            .FirstOrDefaultAsync(cancellationToken);

        if (incidentTeamId is null) return false;

        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.UserId))
            return true;

        if (currentUser.IsInRole("Admin") || currentUser.IsInRole("Owner"))
            return true;

        if (incidentTeamId.TeamId is null) return true;

        return await teamMemberRepo.GetQueryable()
            .AsNoTracking()
            .AnyAsync(tm => tm.UserId == currentUser.UserId && tm.TeamId == incidentTeamId.TeamId.Value, cancellationToken);
    }
}
