using Callu.Shared.Models.Auth;
using Callu.Shared.Models.Notifications;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for user profile management
/// </summary>
public interface IProfileService
{
    /// <summary>
    /// Get current user's profile
    /// </summary>
    Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update user's profile information
    /// </summary>
    Task<bool> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Change user's password
    /// </summary>
    Task<PasswordChangeResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update notification preferences
    /// </summary>
    Task<bool> UpdateNotificationPreferencesAsync(string userId, NotificationPreferences preferences, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get notification preferences
    /// </summary>
    Task<NotificationPreferences> GetNotificationPreferencesAsync(string userId, CancellationToken cancellationToken = default);
}