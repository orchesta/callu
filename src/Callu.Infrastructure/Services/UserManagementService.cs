using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Infrastructure.Identity;
using Callu.Shared.Models.Auth;
using FluentValidation;
using FluentValidation.Results;

namespace Callu.Infrastructure.Services;

/// <summary>
/// User management service implementation
/// </summary>
public class UserManagementService(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IEmailService emailService,
    ITransactionManager transactionManager,
    ITenantUserReadRepository tenantUserRead,
    IOrganizationSettingsService organizationSettingsService,
    IRefreshTokenRepository refreshTokenRepo,
    ILogger<UserManagementService> logger) : IUserManagementService
{
    public async Task<IEnumerable<UserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        var rows = await tenantUserRead.GetDirectoryUsersAsync(cancellationToken);
        return rows.Select(MapDirectoryRowToDto).ToList();
    }

    public async Task<UserDto?> GetUserByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return null;

        var roles = await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? "Member";
        return MapToDto(user, role);
    }

    public async Task<(bool Success, string? ErrorMessage, UserDto? User)> CreateUserAsync(
        string email,
        string password,
        string displayName,
        string role,
        CancellationToken cancellationToken = default)
    {
        var (defaultTz, defaultCulture) = await ResolveOrganizationDefaultsAsync(cancellationToken);

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existingUser = await userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                return (false, "Email already exists", (UserDto?)null);
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = displayName,
                FirstName = displayName.Split(' ').FirstOrDefault() ?? displayName,
                LastName = displayName.Split(' ').Skip(1).FirstOrDefault(),
                EmailConfirmed = true,
                Timezone = defaultTz,
                Culture = defaultCulture,
                CreatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                var failures = result.Errors.Select(e => new ValidationFailure(
                    e.Code.Contains("Email") || e.Code.Contains("UserName") ? "Email" :
                    e.Code.Contains("Password") ? "Password" :
                    e.Code.Contains("User") || e.Code.Contains("Name") ? "DisplayName" : "General",
                    e.Description));
                throw new ValidationException(failures);
            }

            await userManager.AddToRoleAsync(user, role);

            logger.LogInformation("Created user {Email} with role {Role}", email, role);

            return ((bool, string?, UserDto?))(true, null, MapToDto(user, role));
        }, cancellationToken);
    }

    public async Task<(bool Success, string? ErrorMessage)> InviteUserAsync(
        string email,
        string role,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = await organizationSettingsService.GetPublicBaseUrlAsync(cancellationToken);
        var (defaultTz, defaultCulture) = await ResolveOrganizationDefaultsAsync(cancellationToken);

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existingUser = await userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                throw new ValidationException(new[] { new ValidationFailure("Email", "Email already exists") });
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = email.Split('@').First(),
                EmailConfirmed = false,
                Timezone = defaultTz,
                Culture = defaultCulture,
                CreatedAt = DateTime.UtcNow
            };

            var tempPassword = GenerateTemporaryPassword();
            var result = await userManager.CreateAsync(user, tempPassword);
            if (!result.Succeeded)
            {
                var failures = result.Errors.Select(e => new ValidationFailure(
                    e.Code.Contains("Email") || e.Code.Contains("UserName") ? "Email" : "General",
                    e.Description));
                throw new ValidationException(failures);
            }

            await userManager.AddToRoleAsync(user, role);

            var token = await userManager.GeneratePasswordResetTokenAsync(user);

            var encodedToken = EncodeToken(token);
            var encodedEmail = Uri.EscapeDataString(email);
            var inviteLink = $"{baseUrl}/auth/accept-invitation?email={encodedEmail}&token={encodedToken}";

            var userName = user.DisplayName ?? email.Split('@').First();
            await emailService.SendInvitationAsync(email, userName, inviteLink, cancellationToken);

            logger.LogInformation("Invited user {Email} with role {Role}", email, role);

            return ((bool, string?))(true, null);
        }, cancellationToken);
    }

    public async Task<bool> ChangeUserRoleAsync(string userId, string newRole, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return false;

        if (!await roleManager.RoleExistsAsync(newRole))
        {
            logger.LogWarning("Refusing role change for {UserId}: role '{Role}' does not exist", userId, newRole);
            return false;
        }

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var currentRoles = await userManager.GetRolesAsync(user);
            await userManager.RemoveFromRolesAsync(user, currentRoles);
            var result = await userManager.AddToRoleAsync(user, newRole);

            if (result.Succeeded)
            {
                await userManager.UpdateSecurityStampAsync(user);
                await refreshTokenRepo.RevokeAllActiveForUserAsync(userId, "role-change", cancellationToken);
            }

            logger.LogInformation("Changed user {UserId} role to {Role}", userId, newRole);
            return result.Succeeded;
        }, cancellationToken);
    }

    public async Task<bool> ResendInvitationAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return false;

        if (user.EmailConfirmed)
        {
            logger.LogWarning("Cannot resend invitation to user {UserId} - email already confirmed", userId);
            return false;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        var baseUrl = await organizationSettingsService.GetPublicBaseUrlAsync(cancellationToken);
        var encodedToken = EncodeToken(token);
        var encodedEmail = Uri.EscapeDataString(user.Email!);
        var inviteLink = $"{baseUrl}/auth/accept-invitation?email={encodedEmail}&token={encodedToken}";

        var userName = user.DisplayName ?? user.Email!.Split('@').First();
        await emailService.SendInvitationAsync(user.Email!, userName, inviteLink, cancellationToken);

        logger.LogInformation("Resent invitation to user {UserId} ({Email})", userId, user.Email);
        return true;
    }

    public async Task<bool> RemoveUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return false;

        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            user.IsDeleted = true;
            user.UpdatedAt = DateTime.UtcNow;
            var result = await userManager.UpdateAsync(user);

            if (result.Succeeded)
            {
                await userManager.UpdateSecurityStampAsync(user);
                await refreshTokenRepo.RevokeAllActiveForUserAsync(userId, "user-removed", cancellationToken);
            }

            logger.LogInformation("Removed user {UserId}; security stamp bumped and refresh tokens revoked", userId);
            return result.Succeeded;
        }, cancellationToken);
    }

    public async Task<bool> UpdateUserAsync(
        string userId,
        string? firstName,
        string? lastName,
        string? phoneNumber,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return false;

        if (firstName != null) user.FirstName = firstName.Trim();
        if (lastName != null) user.LastName = lastName.Trim();
        if (firstName != null || lastName != null)
        {
            var full = $"{user.FirstName} {user.LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(full)) user.DisplayName = full;
        }
        if (phoneNumber != null)
            user.PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var failures = result.Errors.Select(e => new ValidationFailure(
                e.Code.Contains("Phone") ? "PhoneNumber" : "General",
                e.Description));
            throw new ValidationException(failures);
        }

        logger.LogInformation("Admin updated user {UserId} (name/phone)", userId);
        return true;
    }

    private static UserDto MapDirectoryRowToDto(TenantUserDirectoryRow row) => new()
    {
        Id = row.Id,
        Email = row.Email,
        DisplayName = row.DisplayName,
        FirstName = row.FirstName,
        LastName = row.LastName,
        PhoneNumber = row.PhoneNumber,
        Timezone = row.Timezone,
        Role = row.PrimaryRole,
        IsActive = !row.IsDeleted,
        EmailConfirmed = row.EmailConfirmed,
        CreatedAt = row.CreatedAt,
        LastLoginAt = row.LastLoginAt,
        Initials = ComputeInitials(row.DisplayName, row.Email)
    };

    private static string? ComputeInitials(string? displayName, string email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
            if (displayName.Length >= 2)
                return displayName[..2].ToUpperInvariant();
            return char.ToUpperInvariant(displayName[0]).ToString();
        }
        if (email.Length >= 2)
            return email[..2].ToUpperInvariant();
        return email.Length >= 1 ? email[..1].ToUpperInvariant() : null;
    }

    private static UserDto MapToDto(ApplicationUser user, string? role = null)
    {
        var dto = user.Adapt<UserDto>();
        dto.Role = role ?? "Member";
        return dto;
    }

    private static string GenerateTemporaryPassword()
    {
        return $"Temp{Guid.NewGuid():N}!";
    }

    private static string EncodeToken(string token) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string DecodeToken(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }
        catch
        {
            return value;
        }
    }

    public async Task SendPasswordResetEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);

        if (user is null || user.IsDeleted || !user.EmailConfirmed)
        {
            logger.LogInformation("Password reset requested for unknown/inactive email {Email}", email);
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var baseUrl = await organizationSettingsService.GetPublicBaseUrlAsync(cancellationToken);
        var resetLink = $"{baseUrl.TrimEnd('/')}/auth/reset-password" +
                        $"?email={Uri.EscapeDataString(email)}" +
                        $"&token={EncodeToken(token)}";

        var sent = await emailService.SendPasswordResetAsync(email, resetLink, cancellationToken);
        if (sent)
            logger.LogInformation("Password reset email sent to {Email}", email);
        else
            logger.LogWarning("Password reset email send returned false for {Email} (SMTP unconfigured or send failed)", email);
    }

    public async Task<(bool Success, string? ErrorMessage)> ResetPasswordAsync(
        string email, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null || user.IsDeleted)
            {
                return ((bool, string?))(false, "Invalid or expired reset link.");
            }

            if (!user.EmailConfirmed)
            {
                logger.LogInformation("Reset password rejected for unconfirmed account {Email}; use accept-invitation", email);
                return ((bool, string?))(false, "Account is pending invitation acceptance.");
            }

            var result = await userManager.ResetPasswordAsync(user, DecodeToken(token), newPassword);
            if (!result.Succeeded)
            {
                var error = string.Join(", ", result.Errors.Select(e => e.Description));
                return ((bool, string?))(false, error);
            }

            await userManager.UpdateSecurityStampAsync(user);
            await refreshTokenRepo.RevokeAllActiveForUserAsync(user.Id, "password-reset", cancellationToken);

            logger.LogInformation("Password reset successful for user {Email}; sessions revoked", email);
            return ((bool, string?))(true, null);
        }, cancellationToken);
    }

    public async Task<(bool Success, string? Token, string? ErrorMessage)> GenerateInvitationTokenAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null || user.IsDeleted)
        {
            return (false, null, "User not found.");
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        logger.LogInformation("Generated invitation token for user {UserId}", userId);

        return (true, token, null);
    }

    public async Task<(bool Success, string? ErrorMessage)> AcceptInvitationAsync(
        string email, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null || user.IsDeleted)
            {
                return ((bool, string?))(false, "Invalid or expired invitation.");
            }

            if (user.EmailConfirmed)
            {
                logger.LogInformation("Invitation rejected for already-confirmed account {Email}", email);
                return ((bool, string?))(false, "Invalid or expired invitation.");
            }

            var result = await userManager.ResetPasswordAsync(user, DecodeToken(token), newPassword);
            if (!result.Succeeded)
            {
                var error = string.Join(", ", result.Errors.Select(e => e.Description));
                return ((bool, string?))(false, error);
            }

            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);

            logger.LogInformation("Invitation accepted for {Email}", email);
            return ((bool, string?))(true, null);
        }, cancellationToken);
    }

    /// <summary>
    /// Read the organisation's default timezone + culture so newly-created or invited
    /// users inherit the installation's locale instead of the hard-coded "UTC"/"en-US"
    /// ApplicationUser defaults. Falls back to those defaults when the settings row
    /// hasn't been initialised yet.
    /// </summary>
    private async Task<(string Timezone, string Culture)> ResolveOrganizationDefaultsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await organizationSettingsService.GetSettingsAsync(cancellationToken);
            var tz = string.IsNullOrWhiteSpace(settings.DefaultTimezone) ? "UTC" : settings.DefaultTimezone;
            var culture = string.IsNullOrWhiteSpace(settings.DefaultCulture) ? "en-US" : settings.DefaultCulture;
            return (tz, culture);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve organisation default timezone/culture; using UTC/en-US");
            return ("UTC", "en-US");
        }
    }
}
