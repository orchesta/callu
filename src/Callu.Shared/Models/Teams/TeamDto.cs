namespace Callu.Shared.Models.Teams;

/// <summary>
/// Team DTO for list views
/// </summary>
public record TeamDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public string? Color { get; init; }
    public int MemberCount { get; init; }
    public int ServiceCount { get; init; }
    public DateTime CreatedAt { get; init; }
}