namespace Callu.Shared.Models.Auth;

/// <summary>
/// Password change result
/// </summary>
public record PasswordChangeResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
