using System.ComponentModel.DataAnnotations;
using Callu.Shared.Validation;

namespace Callu.Shared.Models.Teams;

/// <summary>
/// Create team request
/// </summary>
public record CreateTeamRequest
{
    [Required(ErrorMessage = "Team name is required")]
    [SafeStringLength(2, 100)]
    public string Name { get; init; } = string.Empty;
    
    [SafeStringLength(0, 500)]
    public string? Description { get; init; }
    
    [SafeStringLength(0, 10)]
    public string? Icon { get; init; }
    
    [SafeStringLength(0, 50)]
    public string? Color { get; init; }
}
