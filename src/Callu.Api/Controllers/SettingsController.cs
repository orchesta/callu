using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Results;
using Callu.Shared.Models.Settings;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/settings")]
[Authorize(Policy = Policies.CanManageSettings)]
public class SettingsController(
    ISmtpSettingsService smtpSettingsService,
    IOrganizationSettingsService organizationSettingsService,
    ILocalizationService localizationService,
    INotificationPushService pushService) : ControllerBase
{
    [HttpGet("organization")]
    public async Task<IActionResult> GetOrganizationSettings(CancellationToken ct)
    {
        var settings = await organizationSettingsService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [HttpPut("organization")]
    public async Task<IActionResult> UpdateOrganizationSettings(
        [FromBody] UpdateOrganizationSettingsRequest request,
        CancellationToken ct)
    {
        var settings = await organizationSettingsService.SaveSettingsAsync(request, ct);
        await pushService.BroadcastSettingsUpdatedAsync("organization", ct);
        return Ok(settings);
    }

    [HttpGet("smtp")]
    public async Task<IActionResult> GetSmtpSettings(CancellationToken ct)
    {
        var settings = await smtpSettingsService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [HttpPut("smtp")]
    public async Task<IActionResult> SaveSmtpSettings([FromBody] UpdateSmtpSettingsRequest request, CancellationToken ct)
    {
        var success = await smtpSettingsService.SaveSettingsAsync(request, ct);
        if (!success) return BadRequest(ApiResponse.Fail(Messages.Get("settings.smtpSaveFailed")));
        await pushService.BroadcastSettingsUpdatedAsync("smtp", ct);
        return Ok(new { message = Messages.Get("settings.smtpSaved") });
    }

    [HttpPost("smtp/test-connection")]
    public async Task<IActionResult> TestSmtpConnection(CancellationToken ct)
    {
        var result = await smtpSettingsService.TestConnectionAsync(ct);
        return Ok(result);
    }

    [HttpPost("smtp/test-email")]
    public async Task<IActionResult> SendTestEmail([FromBody] TestEmailRequest request, CancellationToken ct)
    {
        var result = await smtpSettingsService.SendTestEmailAsync(request.RecipientEmail, ct);
        return Ok(result);
    }

    [HttpGet("localization/timezones")]
    [AllowAnonymous]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult GetTimeZones()
    {
        var timeZones = localizationService.GetTimezones();
        return Ok(timeZones);
    }
}
