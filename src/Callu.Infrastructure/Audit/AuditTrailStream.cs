using Callu.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Audit;

/// <summary>Writes each audit entry to a log stream a collector can ship to a SIEM.</summary>
public sealed class AuditTrailStream(ILoggerFactory loggerFactory) : IAuditSink
{
    /// <summary>The logger name the stream sink filters on; operators configure against it.</summary>
    public const string SourceContext = "Callu.AuditTrail";

    private readonly ILogger _logger = loggerFactory.CreateLogger(SourceContext);

    public void Emit(AuditLog log) =>
        _logger.LogInformation("audit {@Audit}", new AuditTrailEvent
        {
            Id = log.Id,
            At = log.CreatedAt,
            Action = log.Action.ToString(),
            EventName = log.EventName,
            ResourceType = log.ResourceType,
            ResourceId = log.ResourceId,
            ActorId = log.ActorId,
            ActorDisplayName = log.ActorDisplayName,
            Summary = log.Summary,
            RequestIpAddress = log.RequestIpAddress,
            RequestUserAgent = log.RequestUserAgent,
            RequestRoute = log.RequestRoute,
        });
}

// Old and new values are left out on purpose: they are unbounded, they are already in the table and
// in the export, and a log line is the wrong place to carry a payload of that size.
public sealed class AuditTrailEvent
{
    public Guid Id { get; init; }
    public DateTime At { get; init; }
    public string Action { get; init; } = string.Empty;
    public string? EventName { get; init; }
    public string? ResourceType { get; init; }
    public Guid? ResourceId { get; init; }
    public string? ActorId { get; init; }
    public string? ActorDisplayName { get; init; }
    public string? Summary { get; init; }
    public string? RequestIpAddress { get; init; }
    public string? RequestUserAgent { get; init; }
    public string? RequestRoute { get; init; }
}
