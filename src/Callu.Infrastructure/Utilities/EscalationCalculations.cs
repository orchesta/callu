namespace Callu.Infrastructure.Utilities;

/// <summary>
/// Pure calculation utilities for escalation timing logic.
/// Extracted to allow proper unit testing without infrastructure dependencies.
/// </summary>
public static class EscalationCalculations
{
    /// <summary>
    /// Determines if an escalation step should trigger based on elapsed time.
    /// </summary>
    /// <param name="lastTriggerTime">When the escalation was started or last step was triggered</param>
    /// <param name="delayMinutes">Delay in minutes before this step should trigger</param>
    /// <param name="now">Current time</param>
    /// <returns>True if the step should be triggered</returns>
    public static bool ShouldTriggerStep(DateTime lastTriggerTime, int delayMinutes, DateTime now)
    {
        return now >= lastTriggerTime.AddMinutes(delayMinutes);
    }
}
