using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using FluentValidation;
using Mapster;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Shared.Models.Schedules;
using Callu.Shared.Extensions;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence.Transactions;
using NodaTime;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Services;

/// <summary>On-call override CRUD.</summary>
// Every mutation drops the on-call cache so the next escalation tick sees it. Off-team users are rejected at write time, because
// on-call reads honour only an override whose user is on the schedule's team and would silently ignore the row.
public class OnCallOverrideService(
    IOnCallOverrideRepository overrideRepo,
    IScheduleRepository scheduleRepo,
    ITeamMemberRepository teamMemberRepo,
    ITransactionManager transactionManager,
    UserManager<ApplicationUser> userManager,
    IValidator<CreateOverrideRequest> validator,
    IValidator<UpdateOverrideRequest> updateValidator,
    HybridCache cache,
    IClock clock,
    IAuditLogService auditLogService,
    ILogger<OnCallOverrideService> logger) : IOnCallOverrideService
{
    private static string OnCallCacheKey(Guid scheduleId) => $"oncall:{scheduleId}";

    public async Task<IEnumerable<OnCallOverrideDto>> GetOverridesAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var overrides = await overrideRepo.GetQueryable()
            .AsNoTracking()
            .Include(o => o.Schedule)
            .Where(o => o.ScheduleId == scheduleId && !o.IsDeleted)
            .OrderByDescending(o => o.StartUtc)
            .ToListAsync(cancellationToken);

        var dtos = new List<OnCallOverrideDto>();
        foreach (var o in overrides)
            dtos.Add(await MapToDtoAsync(o));
        return dtos;
    }

    public async Task<IEnumerable<OnCallOverrideDto>> GetActiveOverridesAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetCurrentInstant();

        var overrides = await overrideRepo.GetQueryable()
            .AsNoTracking()
            .Include(o => o.Schedule)
            .Where(o => o.ScheduleId == scheduleId &&
                        !o.IsDeleted &&
                        o.IsActive &&
                        o.EndUtc > now)
            .OrderBy(o => o.StartUtc)
            .ToListAsync(cancellationToken);

        var dtos = new List<OnCallOverrideDto>();
        foreach (var o in overrides)
            dtos.Add(await MapToDtoAsync(o));
        return dtos;
    }

    public async Task<OnCallOverrideDto?> GetOverrideByIdAsync(Guid overrideId, CancellationToken cancellationToken = default)
    {
        var overrideEntity = await overrideRepo.GetQueryable()
            .AsNoTracking()
            .Include(o => o.Schedule)
            .FirstOrDefaultAsync(o => o.Id == overrideId && !o.IsDeleted, cancellationToken);

        return overrideEntity == null ? null : await MapToDtoAsync(overrideEntity);
    }

    public async Task<OnCallOverrideDto> CreateOverrideAsync(CreateOverrideRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var dto = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var teamId = await GetScheduleTeamIdAsync(request.ScheduleId, cancellationToken);
            await EnsureTeamMemberAsync(teamId, request.OverrideUserId, "Override user", cancellationToken);
            if (!string.IsNullOrEmpty(request.OriginalUserId))
                await EnsureTeamMemberAsync(teamId, request.OriginalUserId, "Original user", cancellationToken);

            var overrideEntity = new OnCallOverride
            {
                Id = Guid.NewGuid(),
                ScheduleId = request.ScheduleId,
                OverrideUserId = request.OverrideUserId,
                OriginalUserId = request.OriginalUserId,
                StartUtc = Instant.FromDateTimeUtc(DateTime.SpecifyKind(request.StartUtc, DateTimeKind.Utc)),
                EndUtc = Instant.FromDateTimeUtc(DateTime.SpecifyKind(request.EndUtc, DateTimeKind.Utc)),
                Reason = request.Reason,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await overrideRepo.AddAsync(overrideEntity, cancellationToken);

            // An override decides who a page reaches, so who created it and for whom is auditable.
            await auditLogService.LogAsync(
                request.OverrideUserId, AuditAction.OverrideCreated, "OnCallOverride", overrideEntity.Id.ToString(),
                newValues: $"schedule={request.ScheduleId}; user={request.OverrideUserId}; {request.StartUtc:O}..{request.EndUtc:O}",
                description: request.Reason,
                cancellationToken: cancellationToken);

            logger.LogInformation("Created on-call override {OverrideId} for schedule {ScheduleId}", overrideEntity.Id, request.ScheduleId);
            return await MapToDtoAsync(overrideEntity);
        }, cancellationToken);

        await cache.RemoveAsync(OnCallCacheKey(request.ScheduleId), cancellationToken);
        return dto;
    }

    public async Task<Guid?> UpdateOverrideAsync(Guid overrideId, UpdateOverrideRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var anyFieldSet = request.OverrideUserId is not null ||
                          request.StartUtc.HasValue ||
                          request.EndUtc.HasValue ||
                          request.Reason is not null;

        Guid? scheduleId = null;
        bool updated;
        try
        {
            updated = await transactionManager.ExecuteInTransactionAsync(async () =>
            {
                var overrideEntity = await overrideRepo.FindSingleAsync(o => o.Id == overrideId && !o.IsDeleted, cancellationToken);
                if (overrideEntity == null) return false;

                scheduleId = overrideEntity.ScheduleId;

                if (!anyFieldSet)
                {
                    logger.LogInformation("UpdateOverride no-op (empty body) for {OverrideId}", overrideId);
                    return true;
                }

                if (request.OverrideUserId != null && request.OverrideUserId != overrideEntity.OverrideUserId)
                {
                    var teamId = await GetScheduleTeamIdAsync(overrideEntity.ScheduleId, cancellationToken);
                    await EnsureTeamMemberAsync(teamId, request.OverrideUserId, "Override user", cancellationToken);
                    overrideEntity.OverrideUserId = request.OverrideUserId;
                }
                if (request.StartUtc.HasValue)
                    overrideEntity.StartUtc = Instant.FromDateTimeUtc(DateTime.SpecifyKind(request.StartUtc.Value, DateTimeKind.Utc));
                if (request.EndUtc.HasValue)
                    overrideEntity.EndUtc = Instant.FromDateTimeUtc(DateTime.SpecifyKind(request.EndUtc.Value, DateTimeKind.Utc));
                if (request.Reason != null) overrideEntity.Reason = request.Reason;

                // A one-sided patch can invert the stored interval; the request validator only sees
                // the fields that were sent, so re-check the merged bounds before they persist.
                if (overrideEntity.EndUtc <= overrideEntity.StartUtc)
                    throw new Callu.Shared.Exceptions.BusinessRuleException(
                        "End time must be after start time.");

                overrideEntity.UpdatedAt = DateTime.UtcNow;

                logger.LogInformation("Updated on-call override {OverrideId}", overrideId);
                return true;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Override {OverrideId} update lost concurrency race", overrideId);
            throw new Callu.Shared.Exceptions.ConflictException(
                "This override was modified by another user. Reload and try again.");
        }

        if (updated && scheduleId.HasValue)
            await cache.RemoveAsync(OnCallCacheKey(scheduleId.Value), cancellationToken);
        return updated ? scheduleId : null;
    }

    public async Task<Guid?> DeleteOverrideAsync(Guid overrideId, CancellationToken cancellationToken = default)
    {
        Guid? scheduleId = null;
        var deleted = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var overrideEntity = await overrideRepo.FindSingleAsync(o => o.Id == overrideId && !o.IsDeleted, cancellationToken);
            if (overrideEntity == null) return false;

            overrideEntity.IsDeleted = true;
            overrideEntity.UpdatedAt = DateTime.UtcNow;

            scheduleId = overrideEntity.ScheduleId;

            await auditLogService.LogAsync(
                overrideEntity.OverrideUserId, AuditAction.OverrideCancelled, "OnCallOverride", overrideId.ToString(),
                oldValues: $"schedule={overrideEntity.ScheduleId}; user={overrideEntity.OverrideUserId}",
                cancellationToken: cancellationToken);

            logger.LogInformation("Deleted on-call override {OverrideId}", overrideId);
            return true;
        }, cancellationToken);

        if (deleted && scheduleId.HasValue)
            await cache.RemoveAsync(OnCallCacheKey(scheduleId.Value), cancellationToken);
        return deleted ? scheduleId : null;
    }

    public async Task<string?> GetOverrideUserIdAsync(Guid scheduleId, DateTime atTime, CancellationToken cancellationToken = default)
    {
        var at = Instant.FromDateTimeUtc(DateTime.SpecifyKind(atTime, DateTimeKind.Utc));
        var activeOverride = await overrideRepo.GetQueryable()
            .AsNoTracking()
            .Where(o => o.ScheduleId == scheduleId &&
                        !o.IsDeleted &&
                        o.IsActive &&
                        o.StartUtc <= at &&
                        o.EndUtc > at)
            .OrderByDescending(o => o.StartUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return activeOverride?.OverrideUserId;
    }

    private async Task<Guid> GetScheduleTeamIdAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        var teamId = await scheduleRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => s.Id == scheduleId && !s.IsDeleted)
            .Select(s => (Guid?)s.TeamId)
            .FirstOrDefaultAsync(cancellationToken);

        if (teamId is null)
            throw new Callu.Shared.Exceptions.NotFoundException("Schedule", scheduleId);

        return teamId.Value;
    }

    private async Task EnsureTeamMemberAsync(Guid teamId, string userId, string label, CancellationToken cancellationToken)
    {
        var isMember = await teamMemberRepo.GetByTeamAndUserAsync(teamId, userId, cancellationToken) is not null;
        if (isMember) return;

        logger.LogWarning(
            "Rejected on-call override: {Label} {UserId} is not a member of team {TeamId}",
            label, userId, teamId);
        throw new Callu.Shared.Exceptions.BusinessRuleException(
            $"{label} is not a member of the schedule's team. Add them to the team first.");
    }

    private async Task<OnCallOverrideDto> MapToDtoAsync(OnCallOverride entity)
    {
        var overrideUser = await userManager.FindByIdAsync(entity.OverrideUserId);
        string? originalUserName = null;

        if (!string.IsNullOrEmpty(entity.OriginalUserId))
        {
            var originalUser = await userManager.FindByIdAsync(entity.OriginalUserId);
            originalUserName = originalUser != null
                ? StringExtensions.FormatDisplayName(originalUser.FirstName, originalUser.LastName, originalUser.Email)
                : null;
        }

        return new OnCallOverrideDto
        {
            Id = entity.Id,
            ScheduleId = entity.ScheduleId,
            ScheduleName = entity.Schedule?.Name ?? string.Empty,
            OverrideUserId = entity.OverrideUserId,
            OverrideUserName = overrideUser != null
                ? StringExtensions.FormatDisplayName(overrideUser.FirstName, overrideUser.LastName, overrideUser.Email)
                : null,
            OverrideUserInitials = overrideUser?.Initials,
            OriginalUserId = entity.OriginalUserId,
            OriginalUserName = originalUserName,
            StartUtc = entity.StartUtc.ToDateTimeUtc(),
            EndUtc = entity.EndUtc.ToDateTimeUtc(),
            Reason = entity.Reason,
            IsActive = entity.IsActive
        };
    }
}
