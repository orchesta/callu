using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using FluentValidation;
using Mapster;
using Callu.Application.Events;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Shared;
using Callu.Shared.Models.Teams;
using Callu.Shared.Results;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using FluentValidation.Results;
using ConflictException = Callu.Shared.Exceptions.ConflictException;
using NotFoundException = Callu.Shared.Exceptions.NotFoundException;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Team service implementation
/// </summary>
public class TeamService(
    ITeamRepository teamRepo,
    ITeamMemberRepository memberRepo,
    IScheduleRepository scheduleRepo,
    IScheduleOccurrenceRepository occurrenceRepo,
    IEscalationPolicyRepository escalationPolicyRepo,
    IEscalationStepRepository escalationStepRepo,
    ITransactionManager transactionManager,
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser,
    IValidator<CreateTeamRequest> createValidator,
    ICommunicationEventDispatcher eventDispatcher,
    IScheduleMaterializer materializer,
    ApplicationDbContext db,
    HybridCache cache,
    IAuditLogService auditLogService,
    ILogger<TeamService> logger) : ITeamService
{
    public async Task<IEnumerable<TeamDto>> GetTeamsAsync(CancellationToken cancellationToken = default)
    {
        var teams = await teamRepo.GetQueryable()
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .Include(t => t.Members)
            .Include(t => t.Services)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
        
        return teams.Select(t =>
        {
            var dto = t.Adapt<TeamDto>();
            return dto with { ServiceCount = t.Services.Count(s => !s.IsDeleted) };
        });
    }

    public async Task<TeamDetailDto?> GetTeamByIdAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        var team = await teamRepo.GetQueryable()
            .Include(t => t.Members)
            .Include(t => t.Services)
            .FirstOrDefaultAsync(t => t.Id == teamId && !t.IsDeleted, cancellationToken);
        
        if (team == null)
            return null;

        var memberDtos = new List<TeamMemberDto>();
        foreach (var member in team.Members)
        {
            var user = await userManager.FindByIdAsync(member.UserId);
            if (user != null)
            {
                var memberDto = member.Adapt<TeamMemberDto>();
                memberDtos.Add(memberDto with
                {
                    Name = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
                    Email = user.Email ?? "",
                    Initials = user.Initials
                });
            }
        }
        
        var dto = team.Adapt<TeamDetailDto>();
        return dto with
        {
            ServiceCount = team.Services.Count(s => !s.IsDeleted),
            Members = memberDtos
        };
    }

    public async Task<TeamDto> CreateTeamAsync(CreateTeamRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }
        
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var team = new Team
            {
                Id = Guid.NewGuid(),
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                Icon = request.Icon ?? "fa-users",
                Color = NormalizeColor(request.Color),
                CreatedAt = DateTime.UtcNow
            };
            
            await teamRepo.AddAsync(team, cancellationToken);

            var creatorId = currentUser.UserId;
            if (!string.IsNullOrEmpty(creatorId))
            {
                await memberRepo.AddAsync(new TeamMember
                {
                    Id = Guid.NewGuid(),
                    TeamId = team.Id,
                    UserId = creatorId,
                    Role = AppConstants.TeamMemberRole.Lead,
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken);
            }

            logger.LogInformation("Created team {TeamId}{Owner}", team.Id,
                string.IsNullOrEmpty(creatorId) ? "" : $" with creator {creatorId} as Lead");

            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Created, "Team", team.Id.ToString(),
                newValues: $"name={team.Name}",
                description: "Team created",
                cancellationToken: cancellationToken);

            return team.Adapt<TeamDto>();
        }, cancellationToken);
    }

    public async Task UpdateTeamAsync(Guid teamId, UpdateTeamRequest request, CancellationToken cancellationToken = default)
    {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var team = await teamRepo.FindSingleAsync(t => t.Id == teamId && !t.IsDeleted, cancellationToken);

            if (team == null)
                throw new NotFoundException("Team", teamId);

            if (!string.IsNullOrWhiteSpace(request.Name))
                team.Name = request.Name.Trim();
            
            if (request.Description != null)
                team.Description = request.Description.Trim();
            
            if (!string.IsNullOrWhiteSpace(request.Icon))
                team.Icon = request.Icon;
            
            if (!string.IsNullOrWhiteSpace(request.Color))
                team.Color = NormalizeColor(request.Color);
            
            team.UpdatedAt = DateTime.UtcNow;

            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Updated, "Team", teamId.ToString(),
                newValues: $"name={team.Name}",
                description: "Team updated",
                cancellationToken: cancellationToken);

            logger.LogInformation("Updated team {TeamId}", teamId);
        }, cancellationToken);
    }

    public async Task DeleteTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        var deletedScheduleIds = new List<Guid>();

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var team = await teamRepo.GetQueryable()
                .Include(t => t.Members)
                .Include(t => t.Services)
                .Where(t => !t.IsDeleted)
                .FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);

            if (team == null)
                throw new NotFoundException("Team", teamId);

            // Refuse while a SURVIVING policy still pages this team or one of its schedules. A policy
            // of this same team is part of the same cleanup and does not block.
            var scheduleIds = await scheduleRepo.GetQueryable()
                .AsNoTracking()
                .Where(s => s.TeamId == teamId && !s.IsDeleted)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            var referencing = await escalationStepRepo.GetQueryable()
                .AsNoTracking()
                .Where(s => !s.IsDeleted
                            && !s.EscalationPolicy.IsDeleted
                            && (s.EscalationPolicy.TeamId == null || s.EscalationPolicy.TeamId != teamId)
                            && (s.TeamId == teamId
                                || (s.ScheduleId != null && scheduleIds.Contains(s.ScheduleId.Value))))
                .Select(s => s.EscalationPolicy.Name)
                .Distinct()
                .Take(6)
                .ToListAsync(cancellationToken);

            if (referencing.Count > 0)
                throw new Callu.Shared.Exceptions.BusinessRuleException(
                    "This team (or one of its schedules) is still a paging target of the escalation "
                    + "policy/policies: " + string.Join(", ", referencing)
                    + ". Repoint or remove those steps first — deleting it would leave them paging nobody.");

            var now = DateTime.UtcNow;
            team.IsDeleted = true;
            team.UpdatedAt = now;

            foreach (var member in team.Members)
            {
                member.IsDeleted = true;
                member.UpdatedAt = now;
            }
            foreach (var service in team.Services)
            {
                service.IsDeleted = true;
                service.UpdatedAt = now;
            }

            var schedules = await scheduleRepo.GetQueryable()
                .Include(s => s.Rotations)
                .Where(s => s.TeamId == teamId && !s.IsDeleted)
                .ToListAsync(cancellationToken);


            foreach (var schedule in schedules)
            {
                schedule.IsDeleted = true;
                schedule.UpdatedAt = now;
                foreach (var rotation in schedule.Rotations)
                {
                    rotation.IsDeleted = true;
                    rotation.UpdatedAt = now;
                }
            }

            deletedScheduleIds = schedules.Select(s => s.Id).ToList();

            // Occurrences are derived data: the materializer skips deleted schedules, so they
            // would otherwise never be pruned again. Hard-delete matches its own prune semantics.
            if (deletedScheduleIds.Count > 0)
            {
                await occurrenceRepo.GetQueryable()
                    .Where(o => deletedScheduleIds.Contains(o.ScheduleId))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            var policies = await escalationPolicyRepo.GetQueryable()
                .Include(p => p.Steps)
                .Where(p => p.TeamId == teamId && !p.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var policy in policies)
            {
                policy.IsDeleted = true;
                policy.UpdatedAt = now;
                foreach (var step in policy.Steps)
                {
                    step.IsDeleted = true;
                    step.UpdatedAt = now;
                }
            }

            logger.LogInformation(
                "Deleted team {TeamId} cascaded to {Members} members, {Services} services, {Schedules} schedules, {Policies} policies",
                teamId, team.Members.Count, team.Services.Count, schedules.Count, policies.Count);

            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Deleted, "Team", teamId.ToString(),
                oldValues: $"name={team.Name}",
                description: "Team deleted",
                cancellationToken: cancellationToken);
        }, cancellationToken);

        foreach (var scheduleId in deletedScheduleIds)
            await cache.RemoveAsync($"oncall:{scheduleId}", cancellationToken);
    }

    public async Task AddMemberAsync(Guid teamId, string userId, string role, CancellationToken cancellationToken = default)
    {
        EnsureValidRole(role, nameof(AddMemberRequest.Role));

        var normalizedRole = AppConstants.TeamMemberRole.Normalize(role);

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var team = await teamRepo.FindSingleAsync(t => t.Id == teamId && !t.IsDeleted, cancellationToken);

            if (team == null)
                throw new NotFoundException("Team", teamId);

            var existing = await memberRepo.GetByTeamAndUserIncludingDeletedAsync(
                teamId, userId, cancellationToken);

            if (existing is not null)
            {
                if (!existing.IsDeleted)
                    throw new ConflictException($"User {userId} is already a member of team {teamId}.");

                existing.IsDeleted = false;
                existing.Role = normalizedRole;
                existing.UpdatedAt = DateTime.UtcNow;
                memberRepo.Update(existing);
                logger.LogInformation("Re-added (undeleted) member {UserId} to team {TeamId}", userId, teamId);

                await auditLogService.LogAsync(
                    currentUser.UserId, AuditAction.Updated, "Team", teamId.ToString(),
                    newValues: $"member={userId}; role={normalizedRole}",
                    description: "Team member re-added",
                    cancellationToken: cancellationToken);
                return;
            }

            var member = new TeamMember
            {
                Id = Guid.NewGuid(),
                TeamId = teamId,
                UserId = userId,
                Role = normalizedRole,
                CreatedAt = DateTime.UtcNow
            };

            await memberRepo.AddAsync(member, cancellationToken);
            logger.LogInformation("Added member {UserId} to team {TeamId}", userId, teamId);

            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Updated, "Team", teamId.ToString(),
                newValues: $"member={userId}; role={normalizedRole}",
                description: "Team member added",
                cancellationToken: cancellationToken);
        }, cancellationToken);

        // Same reason removal drops it: the on-call roster filter turns on team membership, so adding
        // a member changes the answer too. Without this, a member removed and then re-added keeps the
        // cached "nobody is on call" for the rest of the 60s TTL.
        await InvalidateOnCallCacheForTeamAsync(teamId, cancellationToken);

        var user = await userManager.FindByIdAsync(userId);
        var displayName = user?.DisplayName ?? user?.UserName ?? userId;
        await eventDispatcher.DispatchAsync(
            new TeamMemberAddedEvent(userId, displayName, teamId), cancellationToken);
    }

    /// <summary>Removes a member from a team, taking them off that team's rota through <see cref="OnCallMembershipCascade"/>.</summary>
    // Scoped to THIS team: the person may still be on another team's rota, and that one must not move.
    public async Task RemoveMemberAsync(Guid teamId, Guid memberId, CancellationToken cancellationToken = default)
    {
        string? removedUserId = null;
        var detachment = default(OnCallDetachment);

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var member = await memberRepo.FindSingleAsync(m => m.Id == memberId && m.TeamId == teamId, cancellationToken);

            if (member == null)
                throw new NotFoundException("TeamMember", memberId);

            var now = DateTime.UtcNow;
            removedUserId = member.UserId;
            member.IsDeleted = true;
            member.UpdatedAt = now;
            memberRepo.Update(member);

            detachment = await OnCallMembershipCascade.DetachAsync(
                db, member.UserId, teamId, now, cancellationToken);

            logger.LogInformation(
                "Removed (soft-deleted) member {MemberId} from team {TeamId}; {Rotations} rotations soft-deleted, "
                + "{Targets} escalation targets dropped, {Schedules} schedules to rematerialize",
                memberId, teamId, detachment.RotationsRemoved,
                detachment.EscalationTargetsRemoved, detachment.AffectedScheduleIds.Count);

            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Updated, "Team", teamId.ToString(),
                oldValues: $"member={removedUserId}",
                description: "Team member removed",
                cancellationToken: cancellationToken);
        }, cancellationToken);

        if (removedUserId == null)
            return;

        await eventDispatcher.DispatchAsync(
            new TeamMemberRemovedEvent(removedUserId, teamId), cancellationToken);

        // Every schedule on the team, not just the rematerialized ones: the roster filter changes the
        // answer for all of them, and the cached status carries the schedule's name as well as its
        // on-call member.
        await InvalidateOnCallCacheForTeamAsync(teamId, cancellationToken);

        await OnCallMembershipCascade.RepairAsync(
            materializer, cache, logger, removedUserId, detachment.AffectedScheduleIds, cancellationToken);
    }

    private async Task InvalidateOnCallCacheForTeamAsync(Guid teamId, CancellationToken cancellationToken)
    {
        var scheduleIds = await scheduleRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => s.TeamId == teamId && !s.IsDeleted)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in scheduleIds)
            await cache.RemoveAsync($"oncall:{id}", cancellationToken);
    }

    public async Task UpdateMemberRoleAsync(Guid teamId, Guid memberId, string newRole, CancellationToken cancellationToken = default)
    {
        EnsureValidRole(newRole, nameof(UpdateMemberRoleRequest.Role));

        var normalizedRole = AppConstants.TeamMemberRole.Normalize(newRole);

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var member = await memberRepo.FindSingleAsync(m => m.Id == memberId && m.TeamId == teamId, cancellationToken);

            if (member == null)
                throw new NotFoundException("TeamMember", memberId);

            member.Role = normalizedRole;
            member.UpdatedAt = DateTime.UtcNow;

            await auditLogService.LogAsync(
                currentUser.UserId, AuditAction.Updated, "Team", teamId.ToString(),
                newValues: $"member={member.UserId}; role={normalizedRole}",
                description: "Team member role updated",
                cancellationToken: cancellationToken);

            logger.LogInformation("Updated role of member {MemberId} in team {TeamId} to {NewRole}", memberId, teamId, normalizedRole);
        }, cancellationToken);
    }

    /// <summary>
    /// Rejects unknown member roles as a 400 through the FluentValidation path the rest of
    /// the write endpoints use, instead of collapsing into a not-found/bad-request bool.
    /// </summary>
    private static void EnsureValidRole(string? role, string propertyName)
    {
        if (AppConstants.TeamMemberRole.IsValid(role))
            return;

        throw new ValidationException(
        [
            new ValidationFailure(
                propertyName,
                $"Role must be one of: {string.Join(", ", AppConstants.TeamMemberRole.All)}.")
        ]);
    }

    /// <summary>Canonical hex form for <see cref="Team.Color"/>.</summary>
    // Accepts a known hex, a legacy Tailwind class name, or any #RRGGBB / #RGB value; anything else falls back to the brand colour.
    private static string NormalizeColor(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return AppConstants.TeamColors.Default;

        var trimmed = raw.Trim();
        if (AppConstants.TeamColors.LegacyMap.TryGetValue(trimmed, out var mapped))
            return mapped;

        if (trimmed.StartsWith('#') &&
            (trimmed.Length == 7 || trimmed.Length == 4) &&
            System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^#[0-9a-fA-F]+$"))
            return trimmed.ToUpperInvariant();

        return AppConstants.TeamColors.Default;
    }
}
