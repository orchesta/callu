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
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence.Transactions;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Team service implementation
/// </summary>
public class TeamService(
    ITeamRepository teamRepo,
    ITeamMemberRepository memberRepo,
    IScheduleRepository scheduleRepo,
    IEscalationPolicyRepository escalationPolicyRepo,
    ITransactionManager transactionManager,
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser,
    IValidator<CreateTeamRequest> createValidator,
    ICommunicationEventDispatcher eventDispatcher,
    HybridCache cache,
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

            return team.Adapt<TeamDto>();
        }, cancellationToken);
    }

    public async Task<bool> UpdateTeamAsync(Guid teamId, UpdateTeamRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var team = await teamRepo.FindSingleAsync(t => t.Id == teamId && !t.IsDeleted, cancellationToken);
            
            if (team == null)
            {
                logger.LogWarning("Team not found for update: {TeamId}", teamId);
                return false;
            }
            
            if (!string.IsNullOrWhiteSpace(request.Name))
                team.Name = request.Name.Trim();
            
            if (request.Description != null)
                team.Description = request.Description.Trim();
            
            if (!string.IsNullOrWhiteSpace(request.Icon))
                team.Icon = request.Icon;
            
            if (!string.IsNullOrWhiteSpace(request.Color))
                team.Color = NormalizeColor(request.Color);
            
            team.UpdatedAt = DateTime.UtcNow;
            
            logger.LogInformation("Updated team {TeamId}", teamId);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> DeleteTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var team = await teamRepo.GetQueryable()
                .Include(t => t.Members)
                .Include(t => t.Services)
                .Where(t => !t.IsDeleted)
                .FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);

            if (team == null)
            {
                logger.LogWarning("Team not found for delete: {TeamId}", teamId);
                return false;
            }

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
            return true;
        }, cancellationToken);
    }

    public async Task<bool> AddMemberAsync(Guid teamId, string userId, string role, CancellationToken cancellationToken = default)
    {
        if (!AppConstants.TeamMemberRole.IsValid(role))
        {
            logger.LogWarning("Rejected AddMember with invalid role '{Role}'", role);
            return false;
        }

        role = AppConstants.TeamMemberRole.Normalize(role);

        var result = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var team = await teamRepo.FindSingleAsync(t => t.Id == teamId && !t.IsDeleted, cancellationToken);

            if (team == null)
                return false;

            var existing = await memberRepo.GetByTeamAndUserIncludingDeletedAsync(
                teamId, userId, cancellationToken);

            if (existing is not null)
            {
                if (!existing.IsDeleted)
                    return false;

                existing.IsDeleted = false;
                existing.Role = role;
                existing.UpdatedAt = DateTime.UtcNow;
                memberRepo.Update(existing);
                logger.LogInformation("Re-added (undeleted) member {UserId} to team {TeamId}", userId, teamId);
                return true;
            }

            var member = new TeamMember
            {
                Id = Guid.NewGuid(),
                TeamId = teamId,
                UserId = userId,
                Role = role,
                CreatedAt = DateTime.UtcNow
            };

            await memberRepo.AddAsync(member, cancellationToken);
            logger.LogInformation("Added member {UserId} to team {TeamId}", userId, teamId);
            return true;
        }, cancellationToken);

        if (result)
        {
            var user = await userManager.FindByIdAsync(userId);
            var displayName = user?.DisplayName ?? user?.UserName ?? userId;
            await eventDispatcher.DispatchAsync(
                new TeamMemberAddedEvent(userId, displayName, teamId), cancellationToken);
        }
        
        return result;
    }

    public async Task<bool> RemoveMemberAsync(Guid teamId, Guid memberId, CancellationToken cancellationToken = default)
    {
        string? removedUserId = null;

        var result = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var member = await memberRepo.FindSingleAsync(m => m.Id == memberId && m.TeamId == teamId, cancellationToken);

            if (member == null)
                return false;

            removedUserId = member.UserId;
            member.IsDeleted = true;
            member.UpdatedAt = DateTime.UtcNow;
            memberRepo.Update(member);
            logger.LogInformation("Removed (soft-deleted) member {MemberId} from team {TeamId}", memberId, teamId);
            return true;
        }, cancellationToken);

        if (result && removedUserId != null)
        {
            await eventDispatcher.DispatchAsync(
                new TeamMemberRemovedEvent(removedUserId, teamId), cancellationToken);

            await InvalidateOnCallCacheForTeamAsync(teamId, cancellationToken);
        }

        return result;
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

    public async Task<bool> UpdateMemberRoleAsync(Guid teamId, Guid memberId, string newRole, CancellationToken cancellationToken = default)
    {
        if (!AppConstants.TeamMemberRole.IsValid(newRole))
        {
            logger.LogWarning("Rejected UpdateMemberRole with invalid role '{Role}'", newRole);
            return false;
        }

        newRole = AppConstants.TeamMemberRole.Normalize(newRole);

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var member = await memberRepo.FindSingleAsync(m => m.Id == memberId && m.TeamId == teamId, cancellationToken);

            if (member == null)
                return false;

            member.Role = newRole;
            member.UpdatedAt = DateTime.UtcNow;

            logger.LogInformation("Updated role of member {MemberId} in team {TeamId} to {NewRole}", memberId, teamId, newRole);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Canonical hex form for <see cref="Team.Color"/>. Accepts: a known hex from
    /// <see cref="AppConstants.TeamColors.All"/>, a legacy Tailwind class
    /// (translated via <see cref="AppConstants.TeamColors.LegacyMap"/>), or any
    /// "#RRGGBB" / "#RGB" value. Falls back to the default brand colour when the
    /// input doesn't match.
    /// </summary>
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
