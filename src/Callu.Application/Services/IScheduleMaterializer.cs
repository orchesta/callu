using NodaTime;

namespace Callu.Application.Services;

/// <summary>Expands recurring <c>ScheduleRotation</c>s into concrete UTC <c>ScheduleOccurrence</c> rows, so
/// on-call queries are range scans with no recurrence math at query time.</summary>
public interface IScheduleMaterializer
{
    /// <summary>Rematerialize occurrences for a single schedule up to <paramref name="horizon"/> from now.</summary>
    Task RematerializeScheduleAsync(Guid scheduleId, Duration horizon, CancellationToken cancellationToken = default);

    /// <summary>Rematerialize every non-deleted schedule. Called by the daily Quartz job.</summary>
    Task RematerializeAllAsync(Duration horizon, CancellationToken cancellationToken = default);

    /// <summary>Default materialization horizon used by hooks that don't override it.</summary>
    static Duration DefaultHorizon => Duration.FromDays(30);
}
