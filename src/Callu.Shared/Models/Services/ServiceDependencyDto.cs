using Callu.Domain.Enums;

namespace Callu.Shared.Models.Services;

/// <summary>
/// Service dependency DTO
/// </summary>
public record ServiceDependencyDto
{
    public Guid Id { get; init; }
    public Guid ServiceId { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public Guid DependsOnServiceId { get; init; }
    public string DependsOnServiceName { get; init; } = string.Empty;
    public DependencyType Type { get; init; }
    public DependencyCriticality Criticality { get; init; }
    public string? Description { get; init; }
}
