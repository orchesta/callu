namespace Callu.Shared.Models.Incidents;

public record EscalateRequest(string? Reason);
public record ReassignRequest(string TargetUserId);

/// <summary>Optional body of POST /incidents/{id}/resolve.</summary>
public record ResolveIncidentRequest(string? ResolutionType, string? Reason);

/// <summary>Optional body of POST /incidents/{id}/close and /reopen.</summary>
public record IncidentTransitionRequest(string? Reason);
