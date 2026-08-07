namespace Callu.Infrastructure.Utilities;

/// <summary>
/// Pure calculation utilities for escalation timing logic.
/// Extracted to allow proper unit testing without infrastructure dependencies.
/// </summary>
public static class EscalationCalculations
{
    /// <summary>Floor on the gap between two consecutive pages of one incident.</summary>
    public const int MinDelayMinutesBetweenSteps = 2;

    /// <summary>Whether an escalation step should trigger based on elapsed time.</summary>
    public static bool ShouldTriggerStep(DateTime lastTriggerTime, int delayMinutes, DateTime now)
    {
        return now >= lastTriggerTime.AddMinutes(delayMinutes);
    }

    /// <summary>When the first step of a run becomes due.</summary>
    // The delay is taken as written: the floor exists to space two pages apart, and there is no
    // earlier page to space this one from.
    public static DateTime FirstStepDueAt(DateTime escalationStartedAt, int delayMinutes)
        => escalationStartedAt.AddMinutes(delayMinutes);

    /// <summary>When a step that follows an already-paged one becomes due.</summary>
    public static DateTime NextStepDueAt(DateTime lastStepAt, int delayMinutes)
        => lastStepAt.AddMinutes(Math.Max(delayMinutes, MinDelayMinutesBetweenSteps));
}
