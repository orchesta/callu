using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Hybrid;
using FluentValidation;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Shared.Models.Schedules;
using Callu.Shared.Extensions;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using NodaTime;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Rotation CRUD. Every mutation rematerializes the owning schedule and invalidates the
/// on-call cache so reads pick up the change on the next tick.
/// </summary>
public class RotationService(
    IScheduleRotationRepository rotationRepo,
    IScheduleRepository scheduleRepo,
    IScheduleOccurrenceRepository occurrenceRepo,
    ITeamMemberRepository teamMemberRepo,
    ITransactionManager transactionManager,
    UserManager<ApplicationUser> userManager,
    IValidator<CreateRotationRequest> createRotationValidator,
    IValidator<UpdateRotationRequest> updateRotationValidator,
    IScheduleMaterializer materializer,
    HybridCache cache,
    IClock clock,
    ILogger<RotationService> logger) : IRotationService
{
    public async Task<IEnumerable<ScheduleRotationDto>> GetRotationsAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var rotations = await rotationRepo.GetQueryable()
                .AsNoTracking()
                .Where(r => r.ScheduleId == scheduleId && !r.IsDeleted && r.Schedule != null && !r.Schedule.IsDeleted)
                .OrderBy(r => r.Order)
                .ToListAsync(cancellationToken);

            var result = new List<ScheduleRotationDto>();
            foreach (var rotation in rotations)
            {
                result.Add(await MapAsync(rotation));
            }
            return result;
        }, cancellationToken);
    }

    public async Task<ScheduleRotationDto> AddRotationAsync(Guid scheduleId, CreateRotationRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await createRotationValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        var dto = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var schedule = await scheduleRepo.FindSingleAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken);
            if (schedule == null)
                throw new Callu.Shared.Exceptions.NotFoundException("Schedule", scheduleId);

            var isMember = await teamMemberRepo.GetByTeamAndUserAsync(
                schedule.TeamId, request.UserId, cancellationToken) is not null;
            if (!isMember)
                throw new ValidationException(
                    $"User {request.UserId} is not a member of team {schedule.TeamId}.");

            var rotation = new ScheduleRotation
            {
                Id = Guid.NewGuid(),
                ScheduleId = scheduleId,
                UserId = request.UserId,
                HandoverStartLocal = request.HandoverStartLocal,
                ShiftLengthMinutes = request.ShiftLengthMinutes,
                IsPrimary = request.IsPrimary,
                Order = request.Order,
                RecurrenceType = request.RecurrenceType,
                RecurrenceIntervalDays = request.RecurrenceIntervalDays,
                OwnershipDays = request.OwnershipDays,
                RecurrenceEndDate = request.RecurrenceEndDate,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await rotationRepo.AddAsync(rotation, cancellationToken);
            FlagForRematerialize(schedule);
            logger.LogInformation("Added rotation {RotationId} to schedule {ScheduleId}", rotation.Id, scheduleId);
            return await MapAsync(rotation);
        }, cancellationToken);

        await OnScheduleChangedAsync(scheduleId, cancellationToken);
        return dto;
    }

    public async Task<Guid?> UpdateRotationAsync(Guid rotationId, UpdateRotationRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await updateRotationValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        Guid? scheduleId = null;
        bool updated;
        try
        {
            updated = await transactionManager.ExecuteInTransactionAsync(async () =>
            {
                var rotation = await rotationRepo.GetQueryable()
                    .Include(r => r.Schedule)
                    .FirstOrDefaultAsync(r => r.Id == rotationId && r.Schedule != null && !r.Schedule.IsDeleted, cancellationToken);

                if (rotation == null) return false;

                if (request.HandoverStartLocal.HasValue) rotation.HandoverStartLocal = request.HandoverStartLocal.Value;
                if (request.ShiftLengthMinutes.HasValue) rotation.ShiftLengthMinutes = request.ShiftLengthMinutes.Value;
                if (request.IsPrimary.HasValue) rotation.IsPrimary = request.IsPrimary.Value;
                if (request.Order.HasValue) rotation.Order = request.Order.Value;
                if (request.RecurrenceType.HasValue)
                {
                    rotation.RecurrenceType = request.RecurrenceType.Value;
                    if (!request.RecurrenceIntervalDays.HasValue)
                        rotation.RecurrenceIntervalDays = null;
                }
                if (request.RecurrenceIntervalDays.HasValue) rotation.RecurrenceIntervalDays = request.RecurrenceIntervalDays;
                if (request.OwnershipDays.HasValue) rotation.OwnershipDays = request.OwnershipDays;
                if (request.RecurrenceEndDate.HasValue) rotation.RecurrenceEndDate = request.RecurrenceEndDate;

                rotation.UpdatedAt = DateTime.UtcNow;
                scheduleId = rotation.ScheduleId;
                FlagForRematerialize(rotation.Schedule);
                logger.LogInformation("Updated rotation {RotationId}", rotationId);
                return true;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Rotation {RotationId} update lost concurrency race", rotationId);
            throw new Callu.Shared.Exceptions.ConflictException(
                "This rotation was modified by another user. Reload and try again.");
        }

        if (updated && scheduleId.HasValue)
            await OnScheduleChangedAsync(scheduleId.Value, cancellationToken);
        return updated ? scheduleId : null;
    }

    public async Task<Guid?> RemoveRotationAsync(Guid rotationId, CancellationToken cancellationToken = default)
    {
        Guid? scheduleId = null;
        var removed = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var rotation = await rotationRepo.GetQueryable()
                .Include(r => r.Schedule)
                .FirstOrDefaultAsync(r => r.Id == rotationId && r.Schedule != null && !r.Schedule.IsDeleted, cancellationToken);

            if (rotation == null) return false;
            rotation.IsDeleted = true;
            rotation.UpdatedAt = DateTime.UtcNow;
            scheduleId = rotation.ScheduleId;
            FlagForRematerialize(rotation.Schedule);
            logger.LogInformation("Removed rotation {RotationId}", rotationId);
            return true;
        }, cancellationToken);

        if (removed && scheduleId.HasValue)
            await OnScheduleChangedAsync(scheduleId.Value, cancellationToken);
        return removed ? scheduleId : null;
    }

    public async Task<IEnumerable<ScheduleRotationDto>> GetUpcomingRotationsAsync(Guid scheduleId, int days = 7, CancellationToken cancellationToken = default)
    {
        var horizon = clock.GetCurrentInstant() + Duration.FromDays(days);
        var now = clock.GetCurrentInstant();

        var occurrences = await occurrenceRepo.GetQueryable()
            .AsNoTracking()
            .Include(o => o.Rotation)
            .Where(o => o.ScheduleId == scheduleId &&
                        !o.IsDeleted &&
                        o.EndUtc >= now &&
                        o.StartUtc <= horizon)
            .OrderBy(o => o.StartUtc)
            .ToListAsync(cancellationToken);

        var result = new List<ScheduleRotationDto>();
        foreach (var occurrence in occurrences)
        {
            var user = await userManager.FindByIdAsync(occurrence.UserId);
            var userName = GetUserDisplayName(user);
            result.Add(new ScheduleRotationDto
            {
                Id = occurrence.RotationId,
                ScheduleId = occurrence.ScheduleId,
                UserId = occurrence.UserId,
                UserName = userName,
                UserInitials = userName.GetInitials(),
                StartUtc = occurrence.StartUtc.ToDateTimeUtc(),
                EndUtc = occurrence.EndUtc.ToDateTimeUtc(),
                IsPrimary = occurrence.IsPrimary,
                Order = occurrence.Order,
                HandoverStartLocal = occurrence.Rotation?.HandoverStartLocal,
                ShiftLengthMinutes = occurrence.Rotation?.ShiftLengthMinutes ?? 0,
                RecurrenceType = occurrence.Rotation?.RecurrenceType,
                RecurrenceIntervalDays = occurrence.Rotation?.RecurrenceIntervalDays,
                OwnershipDays = occurrence.Rotation?.OwnershipDays
            });
        }
        return result;
    }

    public async Task<RotationCoverageResult> ValidateRotationCoverageAsync(Guid scheduleId, int days = 30, CancellationToken cancellationToken = default)
    {
        var now = clock.GetCurrentInstant();
        var periodEnd = now + Duration.FromDays(days);

        var occurrences = await occurrenceRepo.GetQueryable()
            .AsNoTracking()
            .Where(o => o.ScheduleId == scheduleId &&
                        !o.IsDeleted &&
                        o.IsPrimary &&
                        o.EndUtc > now &&
                        o.StartUtc < periodEnd)
            .OrderBy(o => o.StartUtc)
            .ToListAsync(cancellationToken);

        if (occurrences.Count == 0)
        {
            var totalHours = (periodEnd - now).TotalHours;
            return new RotationCoverageResult
            {
                HasFullCoverage = false,
                GapHours = totalHours,
                CoveragePercent = 0,
                Gaps = new[] { new CoverageGap { Start = now.ToDateTimeUtc(), End = periodEnd.ToDateTimeUtc() } }
            };
        }

        var gaps = new List<CoverageGap>();
        var currentCoveredUntil = now;

        foreach (var occurrence in occurrences)
        {
            var effectiveStart = occurrence.StartUtc < now ? now : occurrence.StartUtc;
            var effectiveEnd = occurrence.EndUtc > periodEnd ? periodEnd : occurrence.EndUtc;

            if (effectiveStart > currentCoveredUntil)
            {
                gaps.Add(new CoverageGap
                {
                    Start = currentCoveredUntil.ToDateTimeUtc(),
                    End = effectiveStart.ToDateTimeUtc()
                });
            }

            if (effectiveEnd > currentCoveredUntil)
            {
                currentCoveredUntil = effectiveEnd;
            }
        }

        if (currentCoveredUntil < periodEnd)
        {
            gaps.Add(new CoverageGap
            {
                Start = currentCoveredUntil.ToDateTimeUtc(),
                End = periodEnd.ToDateTimeUtc()
            });
        }

        var totalPeriodHours = (periodEnd - now).TotalHours;
        var gapHours = gaps.Sum(g => g.Duration.TotalHours);
        var coveragePercent = totalPeriodHours > 0
            ? Math.Round((1 - gapHours / totalPeriodHours) * 100, 1)
            : 100;

        return new RotationCoverageResult
        {
            HasFullCoverage = !gaps.Any(),
            GapHours = Math.Round(gapHours, 1),
            CoveragePercent = coveragePercent,
            Gaps = gaps
        };
    }

    private async Task<ScheduleRotationDto> MapAsync(ScheduleRotation rotation)
    {
        var user = await userManager.FindByIdAsync(rotation.UserId);
        var userName = GetUserDisplayName(user);
        return new ScheduleRotationDto
        {
            Id = rotation.Id,
            ScheduleId = rotation.ScheduleId,
            UserId = rotation.UserId,
            UserName = userName,
            UserInitials = userName.GetInitials(),
            HandoverStartLocal = rotation.HandoverStartLocal,
            ShiftLengthMinutes = rotation.ShiftLengthMinutes,
            RecurrenceType = rotation.RecurrenceType,
            RecurrenceIntervalDays = rotation.RecurrenceIntervalDays,
            OwnershipDays = rotation.OwnershipDays,
            RecurrenceEndDate = rotation.RecurrenceEndDate,
            IsPrimary = rotation.IsPrimary,
            Order = rotation.Order
        };
    }

    private static string? GetUserDisplayName(ApplicationUser? user)
    {
        if (user == null) return null;
        return StringExtensions.FormatDisplayName(user.FirstName, user.LastName, user.Email);
    }

    /// <summary>Marks the occurrence table as owing this schedule a regeneration, in the caller's transaction.</summary>
    // The rematerialize cannot join that transaction, so without this flag a crash in between leaves the occurrences quietly
    // expanding the old rotation. Re-stamped on every flagging write, because the materializer clears only the value it read.
    private static void FlagForRematerialize(Schedule? schedule)
    {
        if (schedule is not null)
            schedule.NeedsRematerializeSince = DateTime.UtcNow;
    }

    private async Task OnScheduleChangedAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        await materializer.RematerializeScheduleAsync(scheduleId, IScheduleMaterializer.DefaultHorizon, cancellationToken);
        await cache.RemoveAsync($"oncall:{scheduleId}", cancellationToken);

        try
        {
            var horizonDays = (int)IScheduleMaterializer.DefaultHorizon.TotalDays;
            var coverage = await ValidateRotationCoverageAsync(scheduleId, horizonDays, cancellationToken);
            if (!coverage.HasFullCoverage)
                logger.LogWarning(
                    "Schedule {ScheduleId} saved with {Pct:F1}% on-call coverage — {Gap:F1}h uncovered over the next {Days} days.",
                    scheduleId, coverage.CoveragePercent, coverage.GapHours, horizonDays);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Coverage check after save failed for schedule {ScheduleId}.", scheduleId);
        }
    }
}
