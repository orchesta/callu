namespace Callu.Shared.Models.Auth;

/// <summary>
/// User profile data
/// </summary>
public record UserProfileDto
{
    public string UserId { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public string? Timezone { get; init; }
    public DateTime CreatedAt { get; init; }
}
