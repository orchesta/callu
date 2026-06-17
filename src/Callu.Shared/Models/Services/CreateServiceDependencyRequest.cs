using Callu.Domain.Enums;

namespace Callu.Shared.Models.Services;

/// <summary>
/// Create service dependency request
/// </summary>
public record CreateServiceDependencyRequest
{
    public Guid DependsOnServiceId { get; init; }
    public DependencyType Type { get; init; }
    public DependencyCriticality Criticality { get; init; }
    public string? Description { get; init; }
    public bool CascadeStatus { get; init; } = true;
}
