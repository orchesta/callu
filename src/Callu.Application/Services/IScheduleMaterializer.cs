using NodaTime;

namespace Callu.Application.Services;

/// <summary>
/// Expands recurring <c>ScheduleRotation</c>s into concrete UTC occurrences and persists
/// them in <c>ScheduleOccurrence</c>. Runtime on-call queries read the occurrence table
/// directly via range scans — no recurrence math at query time.
///
/// DST policy:
///   * Spring-forward gap: LenientResolver — a nonexistent local time shifts forward to the
///     first valid moment after DST applies.
///   * Fall-back ambiguity: ReturnEarlierMapping — the earlier of the two mappings (standard
///     time, before DST ends) wins.
///
/// Shift duration is wall-clock. A "9-to-9 shift" remains labelled 9→9 regardless of DST,
/// but its actual UTC duration is 23h or 25h on transition days.
/// </summary>
public interface IScheduleMaterializer
{
    /// <summary>Rematerialize occurrences for a single schedule up to <paramref name="horizon"/> from now.</summary>
    Task RematerializeScheduleAsync(Guid scheduleId, Duration horizon, CancellationToken cancellationToken = default);

    /// <summary>Rematerialize every non-deleted schedule. Called by the daily Quartz job.</summary>
    Task RematerializeAllAsync(Duration horizon, CancellationToken cancellationToken = default);

    /// <summary>Default materialization horizon used by hooks that don't override it.</summary>
    static Duration DefaultHorizon => Duration.FromDays(30);
}
