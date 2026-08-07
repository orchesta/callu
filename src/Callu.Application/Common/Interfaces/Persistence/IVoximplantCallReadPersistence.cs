using Callu.Domain.Enums;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// Read-side queries for Voximplant voice flows using an isolated DbContext per operation.
/// </summary>
public interface IVoximplantCallReadPersistence
{
    Task<IncidentVoiceRetryInfo?> GetIncidentForVoiceRetryAsync(Guid incidentId, CancellationToken cancellationToken = default);
}

/// <summary>Minimal incident row for post-failure voice retry scheduling.</summary>
public readonly record struct IncidentVoiceRetryInfo(
    IncidentStatus Status,
    string? CreatedBy,
    string Title,
    string? Description,
    IncidentSeverity Severity);
