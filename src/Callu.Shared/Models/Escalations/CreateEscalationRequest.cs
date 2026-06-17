using System.ComponentModel.DataAnnotations;

namespace Callu.Shared.Models.Escalations;

public record CreateEscalationRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public Guid? TeamId { get; init; }
}
