using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Infrastructure.Identity;
using Callu.Shared.Extensions;
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
    IAuditLogService auditLogService,
    ILogger<IncidentNoteService> logger,
    UserManager<ApplicationUser>? userManager = null) : IIncidentNoteService
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

        var authorNames = await ResolveAuthorNamesAsync(
            notes.Select(n => n.CreatedBy), cancellationToken);

        return notes.Select(n => n.Adapt<IncidentNoteDto>() with
        {
            CreatedByName = n.CreatedBy is null ? null : authorNames.GetValueOrDefault(n.CreatedBy),
        }).ToList();
    }

    /// <summary>Display names for the note authors, keyed by user id.</summary>
    // One lookup for the whole page: the author id is an audit field, and rendering it raw is
    // what the screen used to do.
    private async Task<Dictionary<string, string>> ResolveAuthorNamesAsync(
        IEnumerable<string?> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        if (ids.Length == 0 || userManager is null) return [];

        return await userManager.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(
                u => u.Id,
                u => StringExtensions.FormatDisplayName(u.FirstName, u.LastName, u.Email) ?? u.Email ?? u.Id,
                cancellationToken);
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

            await auditLogService.LogAsync(
                userId, AuditAction.Created, "Incident", incidentId.ToString(),
                newValues: note.Content,
                description: $"Note {note.Id} added ({(note.IsInternal ? "internal" : "visible")})",
                cancellationToken: cancellationToken);

            logger.LogInformation("Added note {NoteId} to incident {IncidentId}", note.Id, incidentId);
            var authorNames = await ResolveAuthorNamesAsync([note.CreatedBy], cancellationToken);
            return note.Adapt<IncidentNoteDto>() with
            {
                CreatedByName = note.CreatedBy is null ? null : authorNames.GetValueOrDefault(note.CreatedBy),
            };
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

            var previousContent = note.Content;

            note.Content = request.Content.Trim();
            note.IsPinned = request.IsPinned;
            note.UpdatedAt = DateTime.UtcNow;

            // The note is part of the incident's written record, so a rewrite keeps both versions.
            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Updated, "Incident", note.IncidentId.ToString(),
                oldValues: previousContent,
                newValues: note.Content,
                description: $"Note {noteId} edited",
                cancellationToken: cancellationToken);

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

            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Deleted, "Incident", note.IncidentId.ToString(),
                oldValues: note.Content,
                description: $"Note {noteId} deleted",
                cancellationToken: cancellationToken);

            logger.LogInformation("Deleted note {NoteId}", noteId);
            return true;
        }, cancellationToken);
    }

    /// <summary>Team-scoping check mirroring <c>IncidentService.ApplyTeamScopingAsync</c>.</summary>
    // Out-of-scope incidents are reported 404, not 403, so their existence is not leaked.
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

        // Same exemption as the incident query: a read-only org-wide role is in no team, so team
        // scoping would hide from an auditor the notes that explain what the trail records.
        if (currentUser.IsInRole("Admin") || currentUser.IsInRole("Owner") || currentUser.IsInRole("Auditor"))
            return true;

        if (incidentTeamId.TeamId is null) return true;

        return await teamMemberRepo.GetQueryable()
            .AsNoTracking()
            .AnyAsync(tm => tm.UserId == currentUser.UserId && tm.TeamId == incidentTeamId.TeamId.Value, cancellationToken);
    }
}
