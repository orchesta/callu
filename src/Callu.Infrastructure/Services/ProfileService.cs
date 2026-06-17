using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Identity;
using Callu.Shared.Models.Auth;
using Callu.Shared.Models.Notifications;
using Callu.Shared.Localization;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Profile service implementation
/// </summary>
public class ProfileService(
    UserManager<ApplicationUser> userManager,
    INotificationPreferenceRepository notifPrefRepo,
    IRefreshTokenRepository refreshTokenRepo,
    ITransactionManager transactionManager,
    ILogger<ProfileService> logger) : IProfileService
{
    public async Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return null;
        
        return new UserProfileDto
        {
            UserId = user.Id,
            FirstName = user.FirstName ?? "",
            LastName = user.LastName ?? "",
            Email = user.Email ?? "",
            PhoneNumber = user.PhoneNumber,
            Timezone = user.Timezone,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<bool> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null) return false;
        
        if (request.FirstName != null) user.FirstName = request.FirstName;
        if (request.LastName != null) user.LastName = request.LastName;
        if (request.PhoneNumber != null) user.PhoneNumber = request.PhoneNumber;
        if (request.Timezone != null) user.Timezone = request.Timezone;
        
        user.DisplayName = $"{user.FirstName} {user.LastName}".Trim();
        user.Initials = GetInitials(user.DisplayName);
        user.UpdatedAt = DateTime.UtcNow;
        
        var result = await userManager.UpdateAsync(user);
        logger.LogInformation("Updated profile for user {UserId}", userId);
        return result.Succeeded;
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return new PasswordChangeResult { Success = false, ErrorMessage = Messages.Get("profile.userNotFound") };

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                logger.LogWarning("Password change failed for user {UserId}: {Errors}", userId, errors);
                return new PasswordChangeResult { Success = false, ErrorMessage = errors };
            }

            await userManager.UpdateSecurityStampAsync(user);
            await refreshTokenRepo.RevokeAllActiveForUserAsync(userId, "password-change", cancellationToken);

            logger.LogInformation("Password changed for user {UserId}; security stamp bumped and refresh tokens revoked", userId);
            return new PasswordChangeResult { Success = true };
        }, cancellationToken);
    }

    public async Task<bool> UpdateNotificationPreferencesAsync(string userId, NotificationPreferences preferences, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existing = await notifPrefRepo.GetByUserAsync(userId, cancellationToken);
            
            if (existing == null)
            {
                var newPrefs = new NotificationPreference
                {
                    UserId = userId,
                    EmailEnabled = preferences.EmailNotifications,
                    SmsEnabled = preferences.SmsNotifications,
                    VoiceEnabled = preferences.VoiceNotifications,
                    PushEnabled = preferences.PushNotifications,
                    QuietHoursStart = preferences.QuietHoursStart,
                    QuietHoursEnd = preferences.QuietHoursEnd,
                    Timezone = preferences.Timezone,
                    CreatedAt = DateTime.UtcNow
                };
                await notifPrefRepo.AddAsync(newPrefs, cancellationToken);
            }
            else
            {
                existing.EmailEnabled = preferences.EmailNotifications;
                existing.SmsEnabled = preferences.SmsNotifications;
                existing.VoiceEnabled = preferences.VoiceNotifications;
                existing.PushEnabled = preferences.PushNotifications;
                existing.QuietHoursStart = preferences.QuietHoursStart;
                existing.QuietHoursEnd = preferences.QuietHoursEnd;
                existing.Timezone = preferences.Timezone;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            
            logger.LogInformation("Updated notification preferences for user {UserId}", userId);
            return true;
        }, cancellationToken);
    }
    
    public async Task<NotificationPreferences> GetNotificationPreferencesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var existing = await notifPrefRepo.GetByUserAsync(userId, cancellationToken);
        
        if (existing == null)
        {
            return new NotificationPreferences
            {
                EmailNotifications = true,
                SmsNotifications = false,
                VoiceNotifications = false,
                PushNotifications = true,
                QuietHoursStart = null,
                QuietHoursEnd = null,
                Timezone = "UTC"
            };
        }

        return new NotificationPreferences
        {
            EmailNotifications = existing.EmailEnabled,
            SmsNotifications = existing.SmsEnabled,
            VoiceNotifications = existing.VoiceEnabled,
            PushNotifications = existing.PushEnabled,
            QuietHoursStart = existing.QuietHoursStart,
            QuietHoursEnd = existing.QuietHoursEnd,
            Timezone = existing.Timezone
        };
    }
    
    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "U";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return name.Length >= 2 ? name[..2].ToUpper() : name.ToUpper();
    }
}
