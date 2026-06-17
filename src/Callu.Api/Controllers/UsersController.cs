using Asp.Versioning;
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
[Route("api/v{version:apiVersion}/users")]
[Authorize(Policy = Policies.CanManageUsers)]
public class UsersController(
    IUserManagementService userService,
    IProfileService profileService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var users = await userService.GetUsersAsync(ct);
        return Ok(users);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var user = await userService.GetUserByIdAsync(id, ct);
        if (user == null) return NotFound();
        return Ok(user);
    }

    [HttpPost("invite")]
    public async Task<IActionResult> Invite([FromBody] InviteUserRequest request, CancellationToken ct)
    {
        var (success, error) = await userService.InviteUserAsync(request.Email, request.Role, ct);
        if (!success) return BadRequest(ApiResponse.Fail(error ?? "Operation failed"));
        return Ok(new { message = Messages.Get("users.invitationSent") });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] AdminUpdateUserRequest request, CancellationToken ct)
    {
        var success = await userService.UpdateUserAsync(id, request.FirstName, request.LastName, request.PhoneNumber, ct);
        if (!success) return NotFound();
        return Ok(new { message = Messages.Get("users.userUpdated") });
    }

    [HttpPut("{id}/role")]
    public async Task<IActionResult> ChangeRole(string id, [FromBody] ChangeRoleRequest request, CancellationToken ct)
    {
        var success = await userService.ChangeUserRoleAsync(id, request.Role, ct);
        if (!success) return NotFound();
        return Ok(new { message = Messages.Get("users.roleUpdated") });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(string id, CancellationToken ct)
    {
        var success = await userService.RemoveUserAsync(id, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpPost("{id}/resend-invitation")]
    public async Task<IActionResult> ResendInvitation(string id, CancellationToken ct)
    {
        var success = await userService.ResendInvitationAsync(id, ct);
        if (!success) return NotFound();
        return Ok(new { message = Messages.Get("users.invitationResent") });
    }

    [HttpGet("{id}/notification-preferences")]
    public async Task<IActionResult> GetNotificationPreferences(string id, CancellationToken ct)
    {
        var prefs = await profileService.GetNotificationPreferencesAsync(id, ct);
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

    [HttpPut("{id}/notification-preferences")]
    public async Task<IActionResult> UpdateNotificationPreferences(
        string id, [FromBody] NotificationPreferencesDto request, CancellationToken ct)
    {
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

        var success = await profileService.UpdateNotificationPreferencesAsync(id, prefs, ct);
        if (!success) return BadRequest(ApiResponse.Fail("Failed to update preferences"));
        return Ok(new { message = "Notification preferences updated" });
    }
}

