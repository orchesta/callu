namespace Callu.Shared.Models.Escalations;

/// <summary>
/// Escalation policy DTO
/// </summary>
public record EscalationDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? TeamId { get; init; }
    public string? TeamName { get; init; }
    public bool IsActive { get; init; }
    public int StepCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public IEnumerable<EscalationStepDto> Steps { get; init; } = Array.Empty<EscalationStepDto>();
}
