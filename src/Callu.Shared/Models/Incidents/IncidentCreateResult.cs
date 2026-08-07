namespace Callu.Shared.Models.Incidents;

/// <summary>Outcome of an incident-create request.</summary>
public enum IncidentCreateOutcome
{
    /// <summary>Incident was persisted normally; <c>Incident</c> is populated.</summary>
    Created = 0,

    /// <summary>An active maintenance window suppressed the incident; no row was persisted.</summary>
    Suppressed = 1,
}

/// <summary>
/// Envelope returned by <c>POST /api/v1/incidents</c>. Callers must inspect
/// <see cref="Outcome"/> before reading <see cref="Incident"/>.
/// </summary>
public sealed record IncidentCreateResult
{
    public required IncidentCreateOutcome Outcome { get; init; }

    /// <summary>Null when <see cref="Outcome"/> is not <c>Created</c>.</summary>
    public IncidentDto? Incident { get; init; }

    /// <summary>Human-readable reason for non-<c>Created</c> outcomes.</summary>
    public string? Reason { get; init; }
}
