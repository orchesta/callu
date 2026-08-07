namespace Callu.Domain.Enums;

/// <summary>Incident lifecycle status states.</summary>
/// <remarks>The numeric values do not follow the lifecycle order — never compare them; use <see cref="IncidentStatusExtensions"/>.</remarks>
public enum IncidentStatus
{
    Open = 1,
    Acknowledged = 2,
    Investigating = 5,
    Mitigated = 6,
    Resolved = 3,
    Closed = 4
}

/// <summary>Predicate helpers over <see cref="IncidentStatus"/>, so its numeric values do not leak into callers.</summary>
public static class IncidentStatusExtensions
{
    /// <summary>
    /// Resolved or Closed — no further workflow transitions and escalation is stopped.
    /// </summary>
    public static bool IsTerminal(this IncidentStatus status) =>
        status is IncidentStatus.Resolved or IncidentStatus.Closed;

    /// <summary>
    /// Anything that is not terminal (Open/Acknowledged/Investigating/Mitigated).
    /// </summary>
    public static bool IsActive(this IncidentStatus status) => !status.IsTerminal();

    /// <summary>
    /// Open or Acknowledged — used by deduplication checks that want to correlate
    /// incoming alerts with in-flight (non-terminal) incidents regardless of investigation phase.
    /// </summary>
    public static bool IsOpenOrAcknowledged(this IncidentStatus status) =>
        status is IncidentStatus.Open or IncidentStatus.Acknowledged;
}
