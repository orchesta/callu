using System.ComponentModel.DataAnnotations;

namespace Callu.Shared.Models.StatusPages;

/// <summary>
/// Request to create a new status page
/// </summary>
public record CreateStatusPageRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Slug { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; init; }
}

/// <summary>
/// Request to update a status page
/// </summary>
public record UpdateStatusPageRequest
{
    [StringLength(200)]
    public string? Name { get; init; }

    [StringLength(1000)]
    public string? Description { get; init; }

    [StringLength(100)]
    public string? Slug { get; init; }

    public bool? IsPublic { get; init; }

    [StringLength(200)]
    [EmailAddress]
    public string? SupportEmail { get; init; }

    public bool? AllowSubscriptions { get; init; }
}

/// <summary>
/// Request to add a component to a status page
/// </summary>
public record AddComponentRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public Guid? ServiceId { get; init; }

    public bool HealthCheckEnabled { get; init; } = false;

    [StringLength(2000)]
    public string? HealthCheckUrl { get; init; }

    [StringLength(10)]
    public string? HealthCheckHttpMethod { get; init; }

    public int HealthCheckIntervalSeconds { get; init; } = 60;
    public int HealthCheckTimeoutSeconds { get; init; } = 10;

    [StringLength(2000)]
    public string? HealthCheckHeaders { get; init; }

    public string? HealthCheckBody { get; init; }

    [StringLength(100)]
    public string? HealthCheckContentType { get; init; }

    public string? HealthCheckFieldMappings { get; init; }
    public string? HealthCheckStateMapping { get; init; }
}

/// <summary>
/// Request to update a component
/// </summary>
public record UpdateComponentRequest
{
    [StringLength(200)]
    public string? Name { get; init; }

    [StringLength(50)]
    public string? Status { get; init; }

    public int? DisplayOrder { get; init; }

    public bool? HealthCheckEnabled { get; init; }

    [StringLength(2000)]
    public string? HealthCheckUrl { get; init; }

    [StringLength(10)]
    public string? HealthCheckHttpMethod { get; init; }

    public int? HealthCheckIntervalSeconds { get; init; }
    public int? HealthCheckTimeoutSeconds { get; init; }

    [StringLength(2000)]
    public string? HealthCheckHeaders { get; init; }

    public string? HealthCheckBody { get; init; }

    [StringLength(100)]
    public string? HealthCheckContentType { get; init; }

    public string? HealthCheckFieldMappings { get; init; }
    public string? HealthCheckStateMapping { get; init; }
}

/// <summary>
/// Request to create a status page incident
/// </summary>
public record CreateStatusIncidentRequest
{
    [Required]
    [StringLength(300)]
    public string Title { get; init; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Status { get; init; } = "investigating";

    [StringLength(50)]
    public string? Impact { get; init; }
}

/// <summary>
/// Request to add an update to an incident
/// </summary>
public record AddIncidentUpdateRequest
{
    [Required]
    public string Message { get; init; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// Request to subscribe to status page notifications
/// </summary>
public record SubscribeRequest(string Email);
