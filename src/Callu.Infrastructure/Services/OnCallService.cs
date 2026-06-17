using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Shared.Models.Schedules;
using Callu.Shared.Extensions;
using Callu.Infrastructure.Identity;
using NodaTime;

namespace Callu.Infrastructure.Services;

/// <summary>
/// On-call status queries. Reads materialized occurrences (<see cref="IScheduleMaterializer"/>)
/// and active overrides. Boundary convention: [start, end).
/// </summary>
public class OnCallService(
    IScheduleRepository scheduleRepo,
    IScheduleOccurrenceRepository occurrenceRepo,
    IOnCallOverrideRepository overrideRepo,
    ITeamMemberRepository teamMemberRepo,
    ITransactionManager transactionManager,
    UserManager<ApplicationUser> userManager,
    IClock clock,
    HybridCache cache,
    ILogger<OnCallService> logger) : IOnCallService
{
    private static readonly HybridCacheEntryOptions OnCallCacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(30)
    };

    public async Task<OnCallStatusDto?> GetCurrentOnCallAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        return await cache.GetOrCreateAsync(
            $"oncall:{scheduleId}",
            async ct => await BuildCurrentOnCallAsync(scheduleId, ct),
            OnCallCacheOptions,
            cancellationToken: cancellationToken);
    }

    private async Task<OnCallStatusDto?> BuildCurrentOnCallAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var schedule = await scheduleRepo.GetQueryable()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken);

            if (schedule == null) return null;

            var now = clock.GetCurrentInstant();

            var activeOverride = await overrideRepo.GetQueryable()
                .AsNoTracking()
                .Where(o => o.ScheduleId == scheduleId &&
                            !o.IsDeleted &&
                            o.IsActive &&
                            o.StartUtc <= now &&
                            o.EndUtc > now)
                .OrderByDescending(o => o.StartUtc)
                .FirstOrDefaultAsync(cancellationToken);

            ApplicationUser? overrideUser = null;
            if (activeOverride != null)
            {
                overrideUser = await userManager.FindByIdAsync(activeOverride.OverrideUserId);
            }

            var currentOccurrences = await occurrenceRepo.GetQueryable()
                .AsNoTracking()
                .Where(o => o.ScheduleId == scheduleId &&
                            !o.IsDeleted &&
                            o.StartUtc <= now &&
                            o.EndUtc > now)
                .OrderBy(o => o.Order)
                .ToListAsync(cancellationToken);

            var rosterUserIds = await teamMemberRepo.GetQueryable()
                .AsNoTracking()
                .Where(tm => tm.TeamId == schedule.TeamId && !tm.IsDeleted)
                .Select(tm => tm.UserId)
                .ToListAsync(cancellationToken);
            var roster = new HashSet<string>(rosterUserIds, StringComparer.Ordinal);

            var droppedCount = currentOccurrences.Count(o => !roster.Contains(o.UserId));
            if (droppedCount > 0)
            {
                logger.LogWarning(
                    "Schedule {ScheduleId} has {Dropped} orphaned occurrence(s) — user(s) no longer on team {TeamId}",
                    scheduleId, droppedCount, schedule.TeamId);
            }
            currentOccurrences = currentOccurrences
                .Where(o => roster.Contains(o.UserId))
                .ToList();

            if (droppedCount > 0 && currentOccurrences.Count == 0 && roster.Count > 0)
            {
                logger.LogError(
                    "Schedule {ScheduleId} (team {TeamId}) has NO on-call: every materialised " +
                    "occurrence references a user not in the team roster. Re-add a rotation " +
                    "member or remove this schedule from active escalation policies.",
                    scheduleId, schedule.TeamId);
            }
            else if (currentOccurrences.Count == 0 && roster.Count == 0)
            {
                logger.LogError(
                    "Schedule {ScheduleId} has NO on-call: team {TeamId} has zero active members.",
                    scheduleId, schedule.TeamId);
            }

            if (activeOverride != null && !roster.Contains(activeOverride.OverrideUserId))
            {
                logger.LogWarning(
                    "Schedule {ScheduleId} active override {OverrideId} points at user not on team {TeamId} — suppressed",
                    scheduleId, activeOverride.Id, schedule.TeamId);
                activeOverride = null;
                overrideUser = null;
            }

            if (currentOccurrences.Count == 0 && overrideUser == null)
                return null;

            var primary = currentOccurrences.FirstOrDefault(o => o.IsPrimary)
                          ?? currentOccurrences.FirstOrDefault();
            var secondary = currentOccurrences.FirstOrDefault(o => o != primary);

            var nextOccurrence = await occurrenceRepo.GetQueryable()
                .AsNoTracking()
                .Where(o => o.ScheduleId == scheduleId &&
                            !o.IsDeleted &&
                            o.StartUtc > now &&
                            roster.Contains(o.UserId))
                .OrderBy(o => o.StartUtc)
                .FirstOrDefaultAsync(cancellationToken);

            ApplicationUser? primaryUser = primary is null ? null : await userManager.FindByIdAsync(primary.UserId);
            ApplicationUser? secondaryUser = secondary is null ? null : await userManager.FindByIdAsync(secondary.UserId);
            ApplicationUser? nextUser = nextOccurrence is null ? null : await userManager.FindByIdAsync(nextOccurrence.UserId);

            if (overrideUser != null)
            {
                return new OnCallStatusDto
                {
                    ScheduleId = schedule.Id,
                    ScheduleName = schedule.Name,
                    PrimaryUserId = activeOverride!.OverrideUserId,
                    PrimaryUserName = GetUserDisplayName(overrideUser) + " (Override)",
                    PrimaryUserInitials = GetInitials(GetUserDisplayName(overrideUser)),
                    SecondaryUserId = null,
                    SecondaryUserName = null,
                    SecondaryUserInitials = null,
                    NextRotation = nextOccurrence is null ? null : nextOccurrence.StartUtc.ToDateTimeUtc(),
                    NextOnCallUserName = GetUserDisplayName(nextUser)
                };
            }

            return new OnCallStatusDto
            {
                ScheduleId = schedule.Id,
                ScheduleName = schedule.Name,
                PrimaryUserId = primary?.UserId,
                PrimaryUserName = GetUserDisplayName(primaryUser),
                PrimaryUserInitials = GetInitials(GetUserDisplayName(primaryUser)),
                SecondaryUserId = secondary?.UserId,
                SecondaryUserName = GetUserDisplayName(secondaryUser),
                SecondaryUserInitials = GetInitials(GetUserDisplayName(secondaryUser)),
                NextRotation = nextOccurrence is null ? null : nextOccurrence.StartUtc.ToDateTimeUtc(),
                NextOnCallUserName = GetUserDisplayName(nextUser)
            };
        }, cancellationToken);
    }

    public async Task<OnCallStatusDto?> GetCurrentOnCallForTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var scheduleId = await scheduleRepo.GetQueryable()
                .AsNoTracking()
                .Where(s => s.TeamId == teamId && !s.IsDeleted)
                .OrderBy(s => s.Name)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (scheduleId is null) return null;
            return await GetCurrentOnCallAsync(scheduleId.Value, cancellationToken);
        }, cancellationToken);
    }

    public async Task<bool> IsUserOnCallAsync(string userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetCurrentInstant();
        return await occurrenceRepo.GetQueryable()
            .AsNoTracking()
            .AnyAsync(o => o.UserId == userId &&
                           !o.IsDeleted &&
                           o.IsPrimary &&
                           o.StartUtc <= now &&
                           o.EndUtc > now,
                cancellationToken);
    }

    private static string? GetUserDisplayName(ApplicationUser? user)
    {
        if (user == null) return null;
        return StringExtensions.FormatDisplayName(user.FirstName, user.LastName, user.Email);
    }

    private static string? GetInitials(string? name) => name.GetInitials();
}
