using System.ComponentModel.DataAnnotations;

namespace Callu.Shared.Models.Escalations;

public record ReorderStepsRequest
{
    [Required]
    public List<Guid> StepIds { get; init; } = new();
}
