using Asp.Versioning;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Results;
using Callu.Shared.Models.Auth;
using Callu.Shared.Models.Notifications;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/profile")]
[Authorize]
public class ProfileController(IProfileService profileService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var profile = await profileService.GetProfileAsync(userId, ct);
        if (profile == null) return NotFound();
        return Ok(profile);
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var success = await profileService.UpdateProfileAsync(userId, request, ct);
        if (!success) return BadRequest(ApiResponse.Fail(Messages.Get("profile.updateFailed")));
        return Ok(new { message = Messages.Get("profile.updated") });
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var result = await profileService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, ct);
        if (!result.Success) return BadRequest(ApiResponse.Fail(result.ErrorMessage ?? "Password change failed"));
        return Ok(new { message = Messages.Get("profile.passwordChanged") });
    }

    [HttpGet("notification-preferences")]
    public async Task<IActionResult> GetNotificationPreferences(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var prefs = await profileService.GetNotificationPreferencesAsync(userId, ct);

        return Ok(new NotificationPreferencesDto
        {
            EmailEnabled = prefs.EmailNotifications,
            SmsEnabled = prefs.SmsNotifications,
            VoiceEnabled = prefs.VoiceNotifications,
            PushEnabled = prefs.PushNotifications,
            QuietHoursStart = prefs.QuietHoursStart,
            QuietHoursEnd = prefs.QuietHoursEnd,
            Timezone = prefs.Timezone
        });
    }

    [HttpPut("notification-preferences")]
    public async Task<IActionResult> UpdateNotificationPreferences(
        [FromBody] NotificationPreferencesDto request, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var prefs = new NotificationPreferences
        {
            EmailNotifications = request.EmailEnabled,
            SmsNotifications = request.SmsEnabled,
            VoiceNotifications = request.VoiceEnabled,
            PushNotifications = request.PushEnabled,
            QuietHoursStart = request.QuietHoursStart,
            QuietHoursEnd = request.QuietHoursEnd,
            Timezone = request.Timezone ?? "UTC"
        };

        var success = await profileService.UpdateNotificationPreferencesAsync(userId, prefs, ct);
        if (!success) return BadRequest(ApiResponse.Fail("Failed to update preferences"));
        return Ok(new { message = "Notification preferences updated" });
    }
}

