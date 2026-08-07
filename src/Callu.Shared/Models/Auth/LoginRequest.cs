using System.ComponentModel.DataAnnotations;

namespace Callu.Shared.Models.Auth;

/// <summary>
/// Request model for login
/// </summary>
public record LoginRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    public string Password { get; init; } = string.Empty;
    
    /// <summary>
    /// Remember me option for persistent session
    /// </summary>
    public bool RememberMe { get; init; }
}