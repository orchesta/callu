namespace Callu.Shared.Models.Teams;

/// <summary>
/// Team detail DTO with members
/// </summary>
public record TeamDetailDto : TeamDto
{
    public List<TeamMemberDto> Members { get; init; } = new();
}