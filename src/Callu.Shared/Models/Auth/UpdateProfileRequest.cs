namespace Callu.Shared.Models.Auth;

/// <summary>
/// Profile update request
/// </summary>
public record UpdateProfileRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Timezone { get; init; }
}
