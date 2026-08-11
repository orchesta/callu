using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Infrastructure.Identity;
using Callu.Shared.Extensions;
using Callu.Shared.Models.Auth;
using Callu.Shared.Validation;
using FluentValidation;
using FluentValidation.Results;
using Callu.Domain.Enums;
using Callu.Shared.Logging;

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
    ITeamMemberRepository teamMemberRepo,
    IScheduleMaterializer materializer,
    ApplicationDbContext db,
    HybridCache cache,
    IAuditLogService auditLogService,
    ICurrentUserService currentUser,
    ILogger<UserManagementService> logger) : IUserManagementService
{
    private const string AdminRole = "Admin";
    private string? Actor() => currentUser.UserId;

    private async Task EnsureNotTheLastAdminAsync(ApplicationUser user, string operation)
    {
        if (!await userManager.IsInRoleAsync(user, AdminRole))
            return;

        var admins = await userManager.GetUsersInRoleAsync(AdminRole);
        var othersRemain = admins.Any(other => other.Id != user.Id && !other.IsDeleted);

        if (othersRemain)
            return;

        logger.LogWarning(
            "Refusing to leave the installation without an administrator: user {UserId} cannot be {Operation}",
            user.Id, operation);

        throw new Callu.Shared.Exceptions.BusinessRuleException(
            $"This is the only administrator, so the account cannot be {operation}. " +
            "Grant the Admin role to another user first.");
    }

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

            await auditLogService.LogAsync(
                Actor(), AuditAction.Created, "User", user.Id,
                oldValues: null,
                newValues: $"role={role}",
                description: "User created",
                cancellationToken: cancellationToken);

            logger.LogInformation("Created user {Email} with role {Role}", PiiRedactor.Email(email), role);

            return ((bool, string?, UserDto?))(true, null, MapToDto(user, role));
        }, cancellationToken);
    }

    public async Task<(bool Success, string? ErrorMessage, bool EmailSent, string? InviteLink)> InviteUserAsync(
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
                // A 256-character email's local part outruns DisplayName's varchar(100).
                DisplayName = email.Split('@').First().ClampTo(IdentityFieldLengths.DisplayName),
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
            var emailSent = await emailService.SendInvitationAsync(email, userName, inviteLink, cancellationToken);

            await auditLogService.LogAsync(
                Actor(), AuditAction.Created, "User", user.Id,
                oldValues: null,
                newValues: $"role={role}; emailSent={emailSent}",
                description: "User invited",
                cancellationToken: cancellationToken);

            if (!emailSent)
            {
                logger.LogWarning(
                    "Invited user {Email} with role {Role}, but the invitation email could not be sent; "
                    + "the link has to be delivered by hand", PiiRedactor.Email(email), role);
            }
            else
            {
                logger.LogInformation("Invited user {Email} with role {Role}", PiiRedactor.Email(email), role);
            }

            // The link goes back only when the email did not: it is a password-reset token, and an
            // account that cannot be reached is worse than one whose link the admin has to carry.
            return (true, (string?)null, emailSent, emailSent ? null : inviteLink);
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

        if (!string.Equals(newRole, AdminRole, StringComparison.Ordinal))
            await EnsureNotTheLastAdminAsync(user, "demoted");

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

            if (result.Succeeded)
            {
                await auditLogService.LogAsync(
                    Actor(), AuditAction.RoleAssigned, "User", userId,
                    oldValues: string.Join(", ", currentRoles),
                    newValues: newRole,
                    cancellationToken: cancellationToken);
            }

            logger.LogInformation("Changed user {UserId} role to {Role}", userId, newRole);
            return result.Succeeded;
        }, cancellationToken);
    }

    public async Task<(bool Found, bool EmailSent, string? InviteLink)> ResendInvitationAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null || user.IsDeleted)
            return (false, false, null);

        if (user.EmailConfirmed)
        {
            logger.LogWarning("Cannot resend invitation to user {UserId} - email already confirmed", userId);
            return (false, false, null);
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);

        var baseUrl = await organizationSettingsService.GetPublicBaseUrlAsync(cancellationToken);
        var encodedToken = EncodeToken(token);
        var encodedEmail = Uri.EscapeDataString(user.Email!);
        var inviteLink = $"{baseUrl}/auth/accept-invitation?email={encodedEmail}&token={encodedToken}";

        var userName = user.DisplayName ?? user.Email!.Split('@').First();
        var emailSent = await emailService.SendInvitationAsync(user.Email!, userName, inviteLink, cancellationToken);

        await auditLogService.LogAsync(
            Actor(), AuditAction.Updated, "User", userId,
            oldValues: null,
            newValues: $"emailSent={emailSent}",
            description: "Invitation resent",
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Resent invitation to user {UserId} ({Email}); email sent: {EmailSent}", userId, PiiRedactor.Email(user.Email), emailSent);
        return (true, emailSent, emailSent ? null : inviteLink);
    }

    /// <summary>Soft-deletes the account and everything that could still page it.</summary>
    // The on-call half runs through OnCallMembershipCascade, and is re-entrant: an already-removed account whose
    // occurrences still name it counts as unfinished work and is repaired on the next call.
    public async Task<bool> RemoveUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
            return false;

        if (!user.IsDeleted)
            await EnsureNotTheLastAdminAsync(user, "removed");

        var detachment = default(OnCallDetachment);

        var hasWork = await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var now = DateTime.UtcNow;
            var wasActive = !user.IsDeleted;

            if (wasActive)
            {
                user.IsDeleted = true;
                user.UpdatedAt = now;
                var result = await userManager.UpdateAsync(user);

                if (!result.Succeeded)
                    return false;

                await userManager.UpdateSecurityStampAsync(user);
                await refreshTokenRepo.RevokeAllActiveForUserAsync(userId, "user-removed", cancellationToken);
            }

            var memberships = await teamMemberRepo.GetQueryable()
                .Where(m => m.UserId == userId && !m.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var membership in memberships)
            {
                membership.IsDeleted = true;
                membership.UpdatedAt = now;
            }

            // teamId: null — the account is gone, so it comes off EVERY team's rota, not just one.
            detachment = await OnCallMembershipCascade.DetachAsync(db, userId, teamId: null, now, cancellationToken);

            logger.LogInformation(
                "Removed user {UserId} (already flagged: {AlreadyRemoved}); {Memberships} memberships and {Rotations} rotations soft-deleted, {Targets} escalation targets dropped, {Schedules} schedules to rematerialize",
                userId, !wasActive, memberships.Count, detachment.RotationsRemoved,
                detachment.EscalationTargetsRemoved, detachment.AffectedScheduleIds.Count);

            if (wasActive)
            {
                await auditLogService.LogAsync(
                    Actor(), AuditAction.Deleted, "User", userId,
                    oldValues: null,
                    newValues: null,
                    description: "User removed",
                    cancellationToken: cancellationToken);
            }

            return wasActive || memberships.Count > 0 || detachment.TouchedAnything;
        }, cancellationToken);

        if (!hasWork)
            return false;

        await OnCallMembershipCascade.RepairAsync(
            materializer, cache, logger, userId, detachment.AffectedScheduleIds, cancellationToken);

        return true;
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
            if (!string.IsNullOrWhiteSpace(full))
                user.DisplayName = full.ClampTo(IdentityFieldLengths.DisplayName);
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
            logger.LogInformation("Password reset requested for unknown/inactive email {Email}", PiiRedactor.Email(email));
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var baseUrl = await organizationSettingsService.GetPublicBaseUrlAsync(cancellationToken);
        var resetLink = $"{baseUrl.TrimEnd('/')}/auth/reset-password" +
                        $"?email={Uri.EscapeDataString(email)}" +
                        $"&token={EncodeToken(token)}";

        var sent = await emailService.SendPasswordResetAsync(email, resetLink, cancellationToken);
        if (sent)
            logger.LogInformation("Password reset email sent to {Email}", PiiRedactor.Email(email));
        else
            logger.LogWarning("Password reset email send returned false for {Email} (SMTP unconfigured or send failed)", PiiRedactor.Email(email));
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
                logger.LogInformation("Reset password rejected for unconfirmed account {Email}; use accept-invitation", PiiRedactor.Email(email));
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

            logger.LogInformation("Password reset successful for user {Email}; sessions revoked", PiiRedactor.Email(email));
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
                logger.LogInformation("Invitation rejected for already-confirmed account {Email}", PiiRedactor.Email(email));
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

            logger.LogInformation("Invitation accepted for {Email}", PiiRedactor.Email(email));
            return ((bool, string?))(true, null);
        }, cancellationToken);
    }

    /// <summary>Reads the organisation's default timezone and culture, so new or invited users inherit the installation's locale.</summary>
    // Falls back to the ApplicationUser defaults when the settings row has not been initialised yet.
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
