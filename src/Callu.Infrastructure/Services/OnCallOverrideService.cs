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

namespace Callu.Infrastructure.Services;

/// <summary>
/// On-Call override CRUD. Every mutation invalidates the on-call cache so the next
/// escalation tick picks up the change immediately.
/// </summary>
public class OnCallOverrideService(
    IOnCallOverrideRepository overrideRepo,
    ITransactionManager transactionManager,
    UserManager<ApplicationUser> userManager,
    IValidator<CreateOverrideRequest> validator,
    IValidator<UpdateOverrideRequest> updateValidator,
    HybridCache cache,
    IClock clock,
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

                if (request.OverrideUserId != null) overrideEntity.OverrideUserId = request.OverrideUserId;
                if (request.StartUtc.HasValue)
                    overrideEntity.StartUtc = Instant.FromDateTimeUtc(DateTime.SpecifyKind(request.StartUtc.Value, DateTimeKind.Utc));
                if (request.EndUtc.HasValue)
                    overrideEntity.EndUtc = Instant.FromDateTimeUtc(DateTime.SpecifyKind(request.EndUtc.Value, DateTimeKind.Utc));
                if (request.Reason != null) overrideEntity.Reason = request.Reason;
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
