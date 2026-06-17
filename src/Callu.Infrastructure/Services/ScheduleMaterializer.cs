using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.TimeZones;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Expands recurring rotations into concrete UTC occurrence rows. Each rematerialize call
/// wipes and regenerates the target schedule's occurrences up to the supplied horizon —
/// simpler than reconciling partial state. See docs/timezone-design.md for DST semantics.
/// Concurrency: the rematerialize window is serialized per-schedule via a PostgreSQL
/// transaction-scoped advisory lock so that Quartz's daily run, a rotation update, and
/// a timezone change cannot interleave and produce duplicate occurrences.
/// </summary>
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

    public async Task RematerializeScheduleAsync(Guid scheduleId, Duration horizon, CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

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
            await tx.CommitAsync(cancellationToken);

            logger.LogInformation(
                "ScheduleMaterializer: schedule {ScheduleId} ({Timezone}) — {Count} occurrences through {Horizon}",
                scheduleId, schedule.Timezone, occurrences.Count, horizonInstant);
        });
    }

    public async Task RematerializeAllAsync(Duration horizon, CancellationToken cancellationToken = default)
    {
        var scheduleIds = await scheduleRepo.GetQueryable()
            .AsNoTracking()
            .Where(s => !s.IsDeleted)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

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
        var current = rotation.HandoverStartLocal;
        var materializedAt = clock.GetCurrentInstant();

        current = FastForwardToWindow(current, rotation, zone, lowerBound);

        for (var i = 0; i < MaxOccurrencesPerRotation; i++)
        {
            if (rotation.RecurrenceEndDate.HasValue && current.Date > rotation.RecurrenceEndDate.Value)
                yield break;

            var startInstant = ResolveHandoverInZone(current, zone);
            var endLocal = current.PlusMinutes(rotation.ShiftLengthMinutes);
            var endInstant = ResolveHandoverInZone(endLocal, zone);

            if (endInstant <= lowerBound)
            {
                current = Advance(current, rotation);
                if (rotation.RecurrenceType == RecurrenceType.None)
                    yield break;
                continue;
            }

            if (startInstant > horizonInstant)
                yield break;

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

            if (rotation.RecurrenceType == RecurrenceType.None)
                yield break;

            current = Advance(current, rotation);
        }

        logger.LogWarning(
            "ScheduleMaterializer: rotation {RotationId} hit MaxOccurrencesPerRotation cap — schedule may be misconfigured",
            rotation.Id);
    }

    /// <summary>
    /// For recurring rotations whose anchor is in the distant past, jump `current`
    /// forward to the first period whose end could still fall inside the
    /// materialization window. Keeps wall-clock semantics — we don't touch the
    /// time-of-day — but skips all the fully-expired periods in one shot.
    /// Conservatively lands one period before the boundary so the caller's
    /// normal loop still runs a couple of early iterations and handles DST
    /// edge cases naturally. Returns `current` unchanged for one-off rotations.
    /// </summary>
    private static LocalDateTime FastForwardToWindow(
        LocalDateTime current, ScheduleRotation rotation, DateTimeZone zone, Instant lowerBound)
    {
        if (rotation.RecurrenceType == RecurrenceType.None)
            return current;

        var currentEndInstant = ResolveHandoverInZone(current.PlusMinutes(rotation.ShiftLengthMinutes), zone);
        if (currentEndInstant > lowerBound)
            return current;

        var daysPerPeriod = GetPeriodDays(rotation);

        var gapMinutes = (lowerBound - currentEndInstant).TotalMinutes;
        var gapDays = (int)(gapMinutes / (60 * 24));
        var periodsToSkip = Math.Max(0, gapDays / daysPerPeriod - 1);
        if (periodsToSkip <= 0) return current;

        if (rotation.RecurrenceIntervalDays.HasValue)
            return current.PlusDays(periodsToSkip * rotation.RecurrenceIntervalDays.Value);

        return rotation.RecurrenceType switch
        {
            RecurrenceType.Daily    => current.PlusDays(periodsToSkip),
            RecurrenceType.Weekly   => current.PlusWeeks(periodsToSkip),
            RecurrenceType.Biweekly => current.PlusWeeks(periodsToSkip * 2),
            RecurrenceType.Monthly  => current.PlusMonths(periodsToSkip),
            _                        => current,
        };
    }

    internal static LocalDateTime Advance(LocalDateTime current, ScheduleRotation rotation)
    {
        if (rotation.RecurrenceIntervalDays is { } days && days > 0)
            return current.PlusDays(days);

        return rotation.RecurrenceType switch
        {
            RecurrenceType.Daily => current.PlusDays(1),
            RecurrenceType.Weekly => current.PlusWeeks(1),
            RecurrenceType.Biweekly => current.PlusWeeks(2),
            RecurrenceType.Monthly => current.PlusMonths(1),
            _ => current.PlusDays(1)
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
}
