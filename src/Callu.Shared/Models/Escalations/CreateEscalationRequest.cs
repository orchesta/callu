using System.ComponentModel.DataAnnotations;
using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Shared.Models.Escalations;

public record CreateEscalationRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public Guid? TeamId { get; init; }

    public EscalationExhaustionBehavior? ExhaustionBehavior { get; init; }

    public int? MaxRepeatCycles { get; init; }

    public int? MaxRepeatDurationMinutes { get; init; }
}
