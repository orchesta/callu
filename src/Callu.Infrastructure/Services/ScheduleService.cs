using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using FluentValidation;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Shared.Models.Schedules;
using Callu.Shared.Extensions;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Infrastructure.Persistence.Transactions;
using NodaTime;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Schedule CRUD. Changing the timezone forces a rematerialize because every shift's
/// UTC instant shifts with the zone.
/// </summary>
public class ScheduleService(
    IScheduleRepository scheduleRepo,
    IScheduleOccurrenceRepository occurrenceRepo,
    ITransactionManager transactionManager,
    UserManager<ApplicationUser> userManager,
    IValidator<CreateScheduleRequest> createScheduleValidator,
    IScheduleMaterializer materializer,
    IDateTimeZoneProvider tzProvider,
    HybridCache cache,
    IOnCallService onCallService) : IScheduleService
{
    public async Task<IEnumerable<ScheduleDto>> GetSchedulesAsync(CancellationToken cancellationToken = default)
    {
        var schedules = await scheduleRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .Include(s => s.Team)
            .Include(s => s.Rotations)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var result = new List<ScheduleDto>();
        foreach (var schedule in schedules)
        {
            var currentOnCall = await GetCurrentOnCallUserNameAsync(schedule.Id);
            result.Add(new ScheduleDto
            {
                Id = schedule.Id,
                Name = schedule.Name,
                Description = schedule.Description,
                TeamId = schedule.TeamId,
                TeamName = schedule.Team?.Name,
                Timezone = schedule.Timezone,
                CurrentOnCallUser = currentOnCall,
                RotationCount = schedule.Rotations.Count(r => !r.IsDeleted),
                CreatedAt = schedule.CreatedAt
            });
        }
        return result;
    }

    public async Task<ScheduleDetailDto?> GetScheduleByIdAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await scheduleRepo.GetQueryable()
            .AsNoTracking()
            .Include(s => s.Team)
            .Include(s => s.Rotations)
            .FirstOrDefaultAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken);

        if (schedule == null) return null;

        var rotationDtos = new List<ScheduleRotationDto>();
        foreach (var rotation in schedule.Rotations.Where(r => !r.IsDeleted).OrderBy(r => r.Order))
        {
            var user = await userManager.FindByIdAsync(rotation.UserId);
            var userName = GetUserDisplayName(user);
            rotationDtos.Add(new ScheduleRotationDto
            {
                Id = rotation.Id,
                ScheduleId = rotation.ScheduleId,
                UserId = rotation.UserId,
                UserName = userName,
                UserInitials = GetInitials(userName),
                HandoverStartLocal = rotation.HandoverStartLocal,
                ShiftLengthMinutes = rotation.ShiftLengthMinutes,
                RecurrenceType = rotation.RecurrenceType,
                RecurrenceEndDate = rotation.RecurrenceEndDate,
                IsPrimary = rotation.IsPrimary,
                Order = rotation.Order
            });
        }

        var currentOnCall = await GetCurrentOnCallUserNameAsync(schedule.Id);

        return new ScheduleDetailDto
        {
            Id = schedule.Id,
            Name = schedule.Name,
            Description = schedule.Description,
            TeamId = schedule.TeamId,
            TeamName = schedule.Team?.Name,
            Timezone = schedule.Timezone,
            CurrentOnCallUser = currentOnCall,
            RotationCount = schedule.Rotations.Count(r => !r.IsDeleted),
            CreatedAt = schedule.CreatedAt,
            Rotations = rotationDtos,
            Overrides = new List<OnCallOverrideDto>()
        };
    }

    public async Task<IEnumerable<ScheduleDto>> GetSchedulesByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        var schedules = await scheduleRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => s.TeamId == teamId && !s.IsDeleted)
            .Include(s => s.Team)
            .Include(s => s.Rotations)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var result = new List<ScheduleDto>();
        foreach (var schedule in schedules)
        {
            var currentOnCall = await GetCurrentOnCallUserNameAsync(schedule.Id);
            result.Add(new ScheduleDto
            {
                Id = schedule.Id,
                Name = schedule.Name,
                Description = schedule.Description,
                TeamId = schedule.TeamId,
                TeamName = schedule.Team?.Name,
                Timezone = schedule.Timezone,
                CurrentOnCallUser = currentOnCall,
                RotationCount = schedule.Rotations.Count(r => !r.IsDeleted),
                CreatedAt = schedule.CreatedAt
            });
        }
        return result;
    }

    public async Task<ScheduleDto> CreateScheduleAsync(CreateScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await createScheduleValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        EnsureKnownTimezone(request.Timezone);

        var dto = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var schedule = new Schedule
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                TeamId = request.TeamId,
                Timezone = request.Timezone,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await scheduleRepo.AddAsync(schedule, cancellationToken);

            return new ScheduleDto
            {
                Id = schedule.Id,
                Name = schedule.Name,
                Description = schedule.Description,
                TeamId = schedule.TeamId,
                Timezone = schedule.Timezone,
                RotationCount = 0,
                CreatedAt = schedule.CreatedAt
            };
        }, cancellationToken);

        await materializer.RematerializeScheduleAsync(dto.Id, IScheduleMaterializer.DefaultHorizon, cancellationToken);
        return dto;
    }

    public async Task<bool> UpdateScheduleAsync(Guid scheduleId, UpdateScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var tzChanged = false;
        var updated = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var schedule = await scheduleRepo.FindSingleAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken);
            if (schedule == null) return false;

            if (request.Name != null) schedule.Name = request.Name;
            if (request.Description != null) schedule.Description = request.Description;
            if (request.Timezone != null && schedule.Timezone != request.Timezone)
            {
                EnsureKnownTimezone(request.Timezone);
                schedule.Timezone = request.Timezone;
                tzChanged = true;
            }
            if (request.TeamId.HasValue) schedule.TeamId = request.TeamId.Value;
            schedule.UpdatedAt = DateTime.UtcNow;

            return true;
        }, cancellationToken);

        if (updated && tzChanged)
        {
            await materializer.RematerializeScheduleAsync(scheduleId, IScheduleMaterializer.DefaultHorizon, cancellationToken);
            await cache.RemoveAsync($"oncall:{scheduleId}", cancellationToken);
        }
        return updated;
    }

    public async Task<bool> DeleteScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var deleted = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var schedule = await scheduleRepo.GetQueryable()
                .Include(s => s.Rotations)
                .Where(s => !s.IsDeleted)
                .FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken);
            if (schedule == null) return false;

            var now = DateTime.UtcNow;
            schedule.IsDeleted = true;
            schedule.UpdatedAt = now;

            foreach (var rotation in schedule.Rotations)
            {
                rotation.IsDeleted = true;
                rotation.UpdatedAt = now;
            }

            await occurrenceRepo.GetQueryable()
                .Where(o => o.ScheduleId == scheduleId)
                .ExecuteDeleteAsync(cancellationToken);

            return true;
        }, cancellationToken);

        if (deleted)
            await cache.RemoveAsync($"oncall:{scheduleId}", cancellationToken);
        return deleted;
    }

    #region Private Helpers

    private void EnsureKnownTimezone(string id)
    {
        if (tzProvider.GetZoneOrNull(id) is null)
            throw new ValidationException($"Unknown IANA timezone: '{id}'");
    }

    private async Task<string?> GetCurrentOnCallUserNameAsync(Guid scheduleId, CancellationToken ct = default)
    {
        var status = await onCallService.GetCurrentOnCallAsync(scheduleId, ct);
        return status?.PrimaryUserName;
    }

    private static string? GetUserDisplayName(ApplicationUser? user)
    {
        if (user == null) return null;
        return StringExtensions.FormatDisplayName(user.FirstName, user.LastName, user.Email);
    }

    private static string? GetInitials(string? name) => name.GetInitials();

    #endregion
}
