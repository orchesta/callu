namespace Callu.Shared.Models.Teams;

/// <summary>
/// Team member info
/// </summary>
public record TeamMemberDto
{
    public Guid Id { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Initials { get; init; }
    public string Role { get; init; } = string.Empty;
    public DateTime JoinedAt { get; init; }
}