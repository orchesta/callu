using System.Text.Json.Nodes;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit.OpenAuditModel;
using Callu.Shared.Models.Audit;
using Callu.Shared.Models.Incidents;

namespace Callu.Infrastructure.Services;

/// <summary>What an incident's lifecycle rows carry for the incident-management profile.</summary>
// Priority is the severity because severity is what callu pages on; there is no second scale to report.
// Values in change are the status and, on create, severity and service: nothing an operator typed.
internal static class IncidentAuditDetails
{
    public static AuditDetails Created(Incident incident)
    {
        var after = new JsonObject
        {
            ["severity"] = Priority(incident),
            ["status"] = Status(incident.Status),
        };
        if (incident.ServiceId is { } serviceId) after["serviceId"] = serviceId.ToString("D");

        var metadata = Core(incident);
        metadata["detectedAt"] = OpenAuditTime.Format(incident.StartedAt);

        return new AuditDetails
        {
            RelatedResources = Related(incident),
            Metadata = new JsonObject { ["incident"] = metadata },
            Change = new AuditChange("create", [.. after.Select(p => p.Key)], After: after),
        };
    }

    public static AuditDetails Transition(Incident incident, IncidentStatus previous, IncidentTransitionNote note)
    {
        var metadata = Core(incident);
        if (incident.Status == IncidentStatus.Resolved)
        {
            if (note.ResolutionType is { } type) metadata["resolutionType"] = type;
            if (incident.ResolvedAt is { } at) metadata["resolvedAt"] = OpenAuditTime.Format(at);
        }

        return new AuditDetails
        {
            Reason = note.Reason is null && note.ReasonCode is null ? null : new AuditReason(note.ReasonCode, note.Reason),
            RelatedResources = Related(incident),
            Metadata = new JsonObject { ["incident"] = metadata },
            Change = new AuditChange("update", ["status"],
                new JsonObject { ["status"] = Status(previous) },
                new JsonObject { ["status"] = Status(incident.Status) }),
        };
    }

    /// <summary>A note added to the incident: the incident as it stands, and which part of it changed.</summary>
    // The row is an incident.create the profile governs, so it carries the state. The note's text is kept
    // in the row's own change column for the keyed chain, never in this exported change.
    public static AuditDetails NoteAdded(Incident incident) => new()
    {
        RelatedResources = Related(incident),
        Metadata = new JsonObject { ["incident"] = Core(incident) },
        Change = new AuditChange("update", ["notes"]),
    };

    private static JsonObject Core(Incident incident) => new()
    {
        ["status"] = Status(incident.Status),
        ["priority"] = Priority(incident),
    };

    private static string Status(IncidentStatus status) => status.ToString().ToLowerInvariant();

    private static string Priority(Incident incident) => incident.Severity.ToString().ToLowerInvariant();

    private static IReadOnlyList<AuditRelatedResource>? Related(Incident incident)
    {
        var related = new List<AuditRelatedResource>();
        if (incident.ServiceId is { } serviceId) related.Add(new("service", serviceId.ToString("D")));
        if (incident.TeamId is { } teamId) related.Add(new("team", teamId.ToString("D")));
        return related.Count == 0 ? null : related;
    }
}
