using Callu.Domain.Enums;

namespace Callu.Shared.Models.Incidents;

/// <summary>Where an incident's escalation run stands.</summary>
public enum IncidentEscalationRunState
{
    /// <summary>No policy is attached, or the attached one has no usable steps.</summary>
    NotConfigured = 0,

    /// <summary>A policy is attached but no step has paged yet.</summary>
    Waiting = 1,

    /// <summary>Steps are being worked through.</summary>
    Running = 2,

    /// <summary>Someone took the incident, so escalation stopped.</summary>
    Stopped = 3,

    /// <summary>Every step ran and the incident is still open. Whether anyone else is paged depends
    /// on the policy: a repeating one starts its next pass, a stopping one is over.</summary>
    Exhausted = 4,
}

/// <summary>Where one step of the policy stands in this incident's run.</summary>
public enum IncidentEscalationStepState
{
    /// <summary>The run has not reached this step.</summary>
    Pending = 0,

    /// <summary>The step the run is on.</summary>
    Current = 1,

    /// <summary>The run has moved past this step.</summary>
    Passed = 2,
}

/// <summary>One step of the policy, as it stands in this incident's run.</summary>
public record IncidentEscalationStepDto
{
    public Guid Id { get; init; }
    public int Level { get; init; }
    public string Title { get; init; } = string.Empty;
    public int DelayMinutes { get; init; }
    public Guid? ScheduleId { get; init; }
    public string? ScheduleName { get; init; }
    public Guid? TeamId { get; init; }
    public string? TeamName { get; init; }
    public bool NotifyAllTeamMembers { get; init; }
    public bool NotifyBothOnCall { get; init; }
    public IReadOnlyList<string> NotifyUserNames { get; init; } = [];
    public IncidentEscalationStepState State { get; init; }

    /// <summary>When this step paged, where that was recorded.</summary>
    // Null on a passed step means the run predates the timeline carrying the step id, not that
    // nothing was paged.
    public DateTime? PagedAt { get; init; }
}

/// <summary>An incident's escalation run: which step it is on, and when the next one is due.</summary>
public record IncidentEscalationDto
{
    public Guid? PolicyId { get; init; }
    public string? PolicyName { get; init; }
    public IncidentEscalationRunState RunState { get; init; }
    public DateTime? StartedAt { get; init; }
    public Guid? CurrentStepId { get; init; }

    /// <summary>The instant the next step becomes due, or null when none is coming.</summary>
    // Computed here rather than by the caller: the gap between two pages is floored, and a step
    // whose page reached nobody deliberately backdates the clock so the next one goes at once.
    // Both rules live with the orchestrator, and a second copy of them would drift.
    public DateTime? NextStepDueAt { get; init; }

    /// <summary>Whether the policy starts another pass once the last step has run, or stops there.</summary>
    public EscalationExhaustionBehavior ExhaustionBehavior { get; init; }

    /// <summary>How many passes a repeating policy may run in total.</summary>
    public int MaxRepeatCycles { get; init; }

    /// <summary>Passes already completed; 0 while the first one is still running.</summary>
    public int CyclesCompleted { get; init; }

    public IReadOnlyList<IncidentEscalationStepDto> Steps { get; init; } = [];
}
