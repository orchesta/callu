namespace Callu.Shared.Models.Incidents;

public record EscalateRequest(string? Reason);
public record ReassignRequest(string TargetUserId);
