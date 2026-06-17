using System.ComponentModel.DataAnnotations;

namespace Callu.Shared.Models.Escalations;

public record UpdateEscalationRequest
{
    [StringLength(100)]
    public string? Name { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }

    public bool? IsActive { get; init; }
    
    public Guid? TeamId { get; init; }
}
