namespace Callu.Shared.Models.Auth;

/// <summary>
/// User data transfer object for user management
/// </summary>
public class UserDto
{
    public string Id { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Timezone { get; set; }
    public string? Initials { get; set; }
    public string Role { get; set; } = "Member";
    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
