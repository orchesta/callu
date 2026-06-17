namespace Callu.Shared.Models.StatusPages;

/// <summary>
/// Status page list DTO
/// </summary>
public record StatusPageDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public bool IsPublic { get; init; }
    public string OverallStatus { get; init; } = "operational";
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Status page detail DTO — includes components and incidents
/// </summary>
public record StatusPageDetailDto : StatusPageDto
{
    public string? Description { get; init; }
    public string? LogoUrl { get; init; }
    public string? CustomDomain { get; init; }
    public string? SupportEmail { get; init; }
    public bool AllowSubscriptions { get; init; } = true;
    public List<StatusPageComponentDto> Components { get; init; } = [];
    public List<StatusPageIncidentDto> Incidents { get; init; } = [];
}

/// <summary>
/// Component DTO — includes health check configuration and results
/// </summary>
public record StatusPageComponentDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = "operational";
    public int DisplayOrder { get; init; }
    public Guid? ServiceId { get; init; }

    public bool HealthCheckEnabled { get; init; }
    public string? HealthCheckUrl { get; init; }
    public string? HealthCheckHttpMethod { get; init; }
    public int HealthCheckIntervalSeconds { get; init; }
    public int HealthCheckTimeoutSeconds { get; init; }
    public DateTime? LastHealthCheckAt { get; init; }
    public string? LastHealthCheckResult { get; init; }
    public int? LastHealthCheckResponseMs { get; init; }
    public int HealthCheckConsecutiveFailures { get; init; }
    public string? HealthCheckSampleResponse { get; init; }
    public string? HealthCheckFieldMappings { get; init; }
    public string? HealthCheckStateMapping { get; init; }
    public bool HealthCheckListeningMode { get; init; }
}

/// <summary>
/// Component DTO trimmed for public consumption — only the operator-authored display
/// fields are kept. Internal probe configuration (HealthCheckUrl, sample response,
/// header/JSONPath mappings) is intentionally absent — the OpenAPI schema must not
/// even advertise those fields to anonymous callers.
/// </summary>
public record PublicStatusPageComponentDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = "operational";
    public int DisplayOrder { get; init; }
}

/// <summary>
/// Public status-page DTO returned by the anonymous /slug/{slug} endpoint. Mirrors
/// the admin StatusPageDetailDto but with the trimmed component shape and without
/// audit fields beyond what visitors need to render the page.
/// </summary>
public record PublicStatusPageDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string OverallStatus { get; init; } = "operational";
    public string? Description { get; init; }
    public string? LogoUrl { get; init; }
    public string? CustomDomain { get; init; }
    public string? SupportEmail { get; init; }
    public bool AllowSubscriptions { get; init; } = true;
    public List<PublicStatusPageComponentDto> Components { get; init; } = [];
    public List<StatusPageIncidentDto> Incidents { get; init; } = [];
}

/// <summary>
/// Incident DTO
/// </summary>
public record StatusPageIncidentDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = "investigating";
    public string Impact { get; init; } = "minor";
    public DateTime CreatedAt { get; init; }
    public List<StatusPageIncidentUpdateDto> Updates { get; init; } = [];
}

/// <summary>
/// Incident update DTO
/// </summary>
public record StatusPageIncidentUpdateDto
{
    public Guid Id { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Status page statistics DTO
/// </summary>
public record StatusPageStatsDto(int ComponentCount, int ActiveIncidentCount, long PageViews, int SubscriberCount);

/// <summary>
/// A subscriber admin DTO
/// </summary>
public record StatusPageSubscriberDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public bool IsConfirmed { get; init; }
    public DateTime SubscribedAt { get; init; }
}

/// <summary>
/// A single day's uptime status for a component
/// </summary>
public record UptimeDayDto
{
    /// <summary>UTC date (date portion only)</summary>
    public DateOnly Date { get; init; }

    /// <summary>Worst status seen that day: operational | degraded | partial_outage | major_outage | maintenance | no_data</summary>
    public string Status { get; init; } = "no_data";

    /// <summary>Uptime percentage (0–100). Null when no data.</summary>
    public double? UptimePercent { get; init; }
}

/// <summary>
/// 30-day uptime history for a single component.
/// NOTE: these figures are page-level and day-granular (approximate), not an SLA-grade
/// measurement — every page incident is attributed to every component and a partial-day
/// outage counts as a fixed per-status percentage for the whole day. See
/// StatusPageService.BuildUptimeAsync. (CAS-3)
/// </summary>
public record ComponentUptimeDto
{
    public Guid ComponentId { get; init; }
    public string ComponentName { get; init; } = string.Empty;
    public string CurrentStatus { get; init; } = "operational";

    /// <summary>Average uptime % over the window</summary>
    public double AverageUptimePercent { get; init; }

    /// <summary>30 entries, oldest first</summary>
    public List<UptimeDayDto> Days { get; init; } = [];
}
