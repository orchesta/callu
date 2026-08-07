using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.TimeZones;

namespace Callu.Infrastructure.Services;

/// <summary>Expands recurring rotations into concrete UTC occurrence rows.</summary>
// Each call wipes and regenerates the schedule's occurrences rather than reconciling partial state, serialized per schedule by a
// transaction-scoped advisory lock so the daily job, a rotation update and a timezone change cannot interleave.
public sealed class ScheduleMaterializer(
    ApplicationDbContext db,
    IScheduleRepository scheduleRepo,
    IScheduleRotationRepository rotationRepo,
    IScheduleOccurrenceRepository occurrenceRepo,
    IDateTimeZoneProvider tzProvider,
    IClock clock,
    ILogger<ScheduleMaterializer> logger) : IScheduleMaterializer
{
    private const int MaxOccurrencesPerRotation = 10_000;

    private const int ScheduleMaterializationLockNamespace = 0x5CED_0001;

    public static readonly ZoneLocalMappingResolver DstResolver =
        Resolvers.CreateMappingResolver(
            Resolvers.ReturnEarlier,
            Resolvers.ReturnForwardShifted);

    public static Instant ResolveHandoverInZone(LocalDateTime local, DateTimeZone zone)
    {
        return zone.ResolveLocal(local, DstResolver).ToInstant();
    }

    // No execution strategy wrapper: the scoped context is registered without EnableRetryOnFailure, and a retrying strategy would
    // replay this lambda on a change tracker still holding the failed attempt's occurrences. The advisory lock handles concurrency.
    public async Task RematerializeScheduleAsync(Guid scheduleId, Duration horizon, CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var scheduleKey = BitConverter.ToInt32(scheduleId.ToByteArray(), 0);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({ScheduleMaterializationLockNamespace}, {scheduleKey})",
                cancellationToken);

            var schedule = await scheduleRepo.GetQueryable()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == scheduleId && !s.IsDeleted, cancellationToken);

            if (schedule is null)
            {
                logger.LogWarning("ScheduleMaterializer: schedule {ScheduleId} not found or deleted — skipped", scheduleId);
                await tx.RollbackAsync(cancellationToken);
                return;
            }

            var rotations = await rotationRepo.GetQueryable()
                .AsNoTracking()
                .Where(r => r.ScheduleId == scheduleId && !r.IsDeleted)
                .OrderBy(r => r.Order)
                .ToListAsync(cancellationToken);

            var zone = ResolveZoneOrWarn(schedule.Timezone, scheduleId);
            if (zone is null)
            {
                await tx.RollbackAsync(cancellationToken);
                return;
            }

            var now = clock.GetCurrentInstant();
            var horizonInstant = now + horizon;
            var occurrences = new List<ScheduleOccurrence>();
            foreach (var rotation in rotations)
            {
                occurrences.AddRange(GenerateOccurrences(schedule, rotation, zone, now, horizonInstant));
            }

            await occurrenceRepo.GetQueryable()
                .Where(o => o.ScheduleId == scheduleId)
                .ExecuteDeleteAsync(cancellationToken);

            foreach (var occurrence in occurrences)
            {
                await occurrenceRepo.AddAsync(occurrence, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await ClearRematerializeFlagAsync(schedule, cancellationToken);
            await tx.CommitAsync(cancellationToken);

            logger.LogInformation(
                "ScheduleMaterializer: schedule {ScheduleId} ({Timezone}) — {Count} occurrences through {Horizon}",
                scheduleId, schedule.Timezone, occurrences.Count, horizonInstant);
        }
        catch
        {
            // The context is SHARED with the caller, so leaving this attempt's Added occurrences tracked means the next
            // SaveChanges inserts them on top of rows whose ExecuteDelete rolled back — two overlapping "primary" on-call slots.
            try
            {
                await tx.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackEx)
            {
                logger.LogError(rollbackEx, "ScheduleMaterializer: rollback failed for schedule {ScheduleId}", scheduleId);
            }

            db.AfterRollback();
            throw;
        }
    }

    public async Task RematerializeAllAsync(Duration horizon, CancellationToken cancellationToken = default)
    {
        var scheduleIds = await scheduleRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        // One bad schedule must not stop the rest, and the per-schedule change-tracker reset is what keeps this catch honest
        // rather than passing the damage on. A failed schedule keeps its flag, so tomorrow's run retries it.
        foreach (var id in scheduleIds)
        {
            try
            {
                await RematerializeScheduleAsync(id, horizon, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ScheduleMaterializer: failed to rematerialize schedule {ScheduleId}", id);
            }
        }
    }

    /// <summary>Clears the schedule's "occurrences are stale" flag, in the same transaction as the occurrence write.</summary>
    // Conditional on the flag value read at the top of this transaction, so a newer flag written mid-run is not cleared by a run
    // that never saw its rotations. The failure mode is a redundant regeneration, never a silently stale rota.
    private async Task ClearRematerializeFlagAsync(Schedule schedule, CancellationToken cancellationToken)
    {
        if (schedule.NeedsRematerializeSince is not { } flaggedAt) return;

        await scheduleRepo.GetQueryable()
            .Where(s => s.Id == schedule.Id && s.NeedsRematerializeSince == flaggedAt)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.NeedsRematerializeSince, (DateTime?)null),
                cancellationToken);
    }

    private DateTimeZone? ResolveZoneOrWarn(string timezoneId, Guid scheduleId)
    {
        var zone = tzProvider.GetZoneOrNull(timezoneId);
        if (zone is null)
        {
            logger.LogError(
                "ScheduleMaterializer: schedule {ScheduleId} has unknown timezone '{Timezone}' — occurrences will NOT be regenerated",
                scheduleId, timezoneId);
        }
        return zone;
    }

    internal IEnumerable<ScheduleOccurrence> GenerateOccurrences(
        Schedule schedule,
        ScheduleRotation rotation,
        DateTimeZone zone,
        Instant now,
        Instant horizonInstant)
    {
        if (rotation.ShiftLengthMinutes <= 0)
        {
            logger.LogWarning(
                "ScheduleMaterializer: rotation {RotationId} has non-positive shift length — skipped",
                rotation.Id);
            yield break;
        }

        var lowerBound = now - Duration.FromHours(1);
        var anchor = rotation.HandoverStartLocal;
        var materializedAt = clock.GetCurrentInstant();
        var ownershipDays = GetOwnershipDays(rotation);

        var startPeriod = PeriodsToSkipToWindow(anchor, rotation, zone, lowerBound);

        var emitted = 0;
        for (var period = startPeriod; period < startPeriod + MaxOccurrencesPerRotation; period++)
        {
            var current = PeriodStart(anchor, rotation, period);
            var pastHorizon = false;

            for (var day = 0; day < ownershipDays; day++)
            {
                var dayStartLocal = current.PlusDays(day);

                if (rotation.RecurrenceEndDate.HasValue && dayStartLocal.Date > rotation.RecurrenceEndDate.Value)
                    yield break;

                var startInstant = ResolveHandoverInZone(dayStartLocal, zone);
                var endInstant = ResolveHandoverInZone(dayStartLocal.PlusMinutes(rotation.ShiftLengthMinutes), zone);

                if (endInstant <= startInstant)
                {
                    endInstant = startInstant + Duration.FromMinutes(rotation.ShiftLengthMinutes);
                    logger.LogDebug(
                        "ScheduleMaterializer: rotation {RotationId} slot at {Local} landed in a DST gap — end clamped to start + shift length",
                        rotation.Id, dayStartLocal);
                }

                if (endInstant <= lowerBound)
                    continue;

                if (startInstant > horizonInstant)
                {
                    pastHorizon = true;
                    break;
                }

                yield return new ScheduleOccurrence
                {
                    Id = Guid.NewGuid(),
                    ScheduleId = schedule.Id,
                    RotationId = rotation.Id,
                    UserId = rotation.UserId,
                    StartUtc = startInstant,
                    EndUtc = endInstant,
                    IsPrimary = rotation.IsPrimary,
                    Order = rotation.Order,
                    MaterializedAt = materializedAt,
                    CreatedAt = DateTime.UtcNow
                };

                if (++emitted >= MaxOccurrencesPerRotation)
                {
                    logger.LogWarning(
                        "ScheduleMaterializer: rotation {RotationId} hit MaxOccurrencesPerRotation cap — schedule may be misconfigured",
                        rotation.Id);
                    yield break;
                }
            }

            if (pastHorizon || rotation.RecurrenceType == RecurrenceType.None)
                yield break;
        }

        logger.LogWarning(
            "ScheduleMaterializer: rotation {RotationId} hit MaxOccurrencesPerRotation cap — schedule may be misconfigured",
            rotation.Id);
    }

    /// <summary>
    /// Days of the recurrence period the member actually owns. Legacy rotations (null) get a single
    /// occurrence per period; partial-day shifts set it so each day of the block is covered.
    /// </summary>
    internal static int GetOwnershipDays(ScheduleRotation rotation) =>
        rotation.OwnershipDays is { } days && days > 0 ? days : 1;

    private static int PeriodsToSkipToWindow(
        LocalDateTime anchor, ScheduleRotation rotation, DateTimeZone zone, Instant lowerBound)
    {
        if (rotation.RecurrenceType == RecurrenceType.None)
            return 0;

        var blockEndLocal = anchor
            .PlusDays(GetOwnershipDays(rotation) - 1)
            .PlusMinutes(rotation.ShiftLengthMinutes);
        var anchorEndInstant = ResolveHandoverInZone(blockEndLocal, zone);
        if (anchorEndInstant > lowerBound)
            return 0;

        var daysPerPeriod = GetMaxPeriodDays(rotation);
        var gapMinutes = (lowerBound - anchorEndInstant).TotalMinutes;
        var gapDays = (int)(gapMinutes / (60 * 24));
        return Math.Max(0, gapDays / daysPerPeriod - 1);
    }

    internal static LocalDateTime PeriodStart(LocalDateTime anchor, ScheduleRotation rotation, int periodIndex)
    {
        if (rotation.RecurrenceIntervalDays is { } days && days > 0)
            return anchor.PlusDays(periodIndex * days);

        return rotation.RecurrenceType switch
        {
            RecurrenceType.Daily => anchor.PlusDays(periodIndex),
            RecurrenceType.Weekly => anchor.PlusWeeks(periodIndex),
            RecurrenceType.Biweekly => anchor.PlusWeeks(periodIndex * 2),
            RecurrenceType.Monthly => anchor.PlusMonths(periodIndex),
            _ => anchor.PlusDays(periodIndex)
        };
    }

    internal static int GetPeriodDays(ScheduleRotation rotation)
    {
        if (rotation.RecurrenceIntervalDays is { } days && days > 0)
            return days;

        return rotation.RecurrenceType switch
        {
            RecurrenceType.Daily    => 1,
            RecurrenceType.Weekly   => 7,
            RecurrenceType.Biweekly => 14,
            RecurrenceType.Monthly  => 30,
            _                        => 1,
        };
    }

    /// <summary>Longest a single period can run, used only as the divisor when skipping expired periods.</summary>
    // Monthly is 31 rather than the 30-day average, because a divisor that undershoots inflates the skip count and can jump the active shift.
    internal static int GetMaxPeriodDays(ScheduleRotation rotation)
    {
        if (rotation.RecurrenceIntervalDays is { } days && days > 0)
            return days;

        return rotation.RecurrenceType switch
        {
            RecurrenceType.Daily    => 1,
            RecurrenceType.Weekly   => 7,
            RecurrenceType.Biweekly => 14,
            RecurrenceType.Monthly  => 31,
            _                        => 1,
        };
    }
}
