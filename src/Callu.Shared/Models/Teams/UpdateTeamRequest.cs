using Callu.Shared.Validation;

namespace Callu.Shared.Models.Teams;

/// <summary>
/// Update team request
/// </summary>
public record UpdateTeamRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }

    [SafeStringLength(0, 64)]
    public string? Icon { get; init; }

    public string? Color { get; init; }
}