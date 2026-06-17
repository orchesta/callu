using Callu.Domain.Enums;

namespace Callu.Shared.Models.Services;

/// <summary>
/// Service list item DTO for compact service views
/// </summary>
public record ServiceListDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public ServiceType Type { get; init; }
    public ServiceStatus Status { get; init; }
    public string? Description { get; init; }
    public double Uptime { get; init; }
    public string? TeamName { get; init; }
}
