using Callu.Shared.Models.Auth;

namespace Callu.Application.Services;

/// <summary>User management operations.</summary>
public interface IUserManagementService
{
    /// <summary>
    /// Get all users
    /// </summary>
    Task<IEnumerable<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get user by ID
    /// </summary>
    Task<UserDto?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Create a new user
    /// </summary>
    Task<(bool Success, string? ErrorMessage, UserDto? User)> CreateUserAsync(
        string email, 
        string password, 
        string displayName, 
        string role,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Invite a user by email
    /// </summary>
    // EmailSent false means the account exists and the link is the only way to reach the invitee.
    Task<(bool Success, string? ErrorMessage, bool EmailSent, string? InviteLink)> InviteUserAsync(
        string email, 
        string role,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Change user role
    /// </summary>
    Task<bool> ChangeUserRoleAsync(string userId, string newRole, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resend invitation to user
    /// </summary>
    Task<(bool Found, bool EmailSent, string? InviteLink)> ResendInvitationAsync(string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Remove/deactivate a user
    /// </summary>
    Task<bool> RemoveUserAsync(string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Admin: update another user's name and phone number. The phone is required for
    /// voice paging, so admins must be able to set it for users who never filled it in.
    /// </summary>
    Task<bool> UpdateUserAsync(string userId, string? firstName, string? lastName, string? phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Send a password reset email if the address matches an active, confirmed user. Always completes
    /// without throwing, so the controller's enumeration-safe response holds either way.</summary>
    Task SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset user password using token
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> ResetPasswordAsync(string email, string token, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Accept an invitation and set the password. Requires the account to be <em>unconfirmed</em>, so a
    /// stolen reset link cannot claim a pending invite (or vice versa).</summary>
    Task<(bool Success, string? ErrorMessage)> AcceptInvitationAsync(string email, string token, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate invitation token for user
    /// </summary>
    Task<(bool Success, string? Token, string? ErrorMessage)> GenerateInvitationTokenAsync(string userId, CancellationToken cancellationToken = default);
}
