using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Models.Notifications;
using Callu.Shared.Results;

namespace Callu.Api.Controllers;

/// <summary>
/// Notification channels — Slack, Teams, Email, Webhook integrations
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/notification-channels")]
[Authorize(Policy = Policies.CanManageSettings)]
public class NotificationChannelsController(
    INotificationChannelService channelService,
    INotificationPushService pushService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await channelService.GetAllAsync(ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var item = await channelService.GetByIdAsync(id, ct);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateNotificationChannelRequest request, CancellationToken ct)
    {
        var item = await channelService.CreateAsync(request, ct);
        await pushService.BroadcastSettingsUpdatedAsync("notification-channels", ct);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateNotificationChannelRequest request, CancellationToken ct)
    {
        var success = await channelService.UpdateAsync(id, request, ct);
        if (!success) return NotFound();
        await pushService.BroadcastSettingsUpdatedAsync("notification-channels", ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id, CancellationToken ct)
    {
        var success = await channelService.ToggleAsync(id, ct);
        if (!success) return NotFound();
        await pushService.BroadcastSettingsUpdatedAsync("notification-channels", ct);
        return Ok(new { message = Messages.Get("channels.toggled") });
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> Test(Guid id, [FromBody] TestNotificationRequest request, CancellationToken ct)
    {
        var success = await channelService.TestAsync(id, request.Message, ct);
        if (!success) return BadRequest(ApiResponse.Fail(Messages.Get("channels.testFailed")));
        return Ok(new { message = Messages.Get("channels.testSent") });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await channelService.DeleteAsync(id, ct);
        if (!success) return NotFound();
        await pushService.BroadcastSettingsUpdatedAsync("notification-channels", ct);
        return NoContent();
    }

    /// <summary>
    /// Get supported channel type definitions
    /// </summary>
    [HttpGet("types")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult GetChannelTypes()
    {
        return Ok(new object[]
        {
            new
            {
                value = "Slack",
                label = "Slack",
                icon = "💬",
                description = "Incoming Webhook. A message is posted when a new incident is created in Callu.",
                fields = new object[]
                {
                    new { key = "webhookUrl", label = "Incoming webhook URL", input = "url", required = true, placeholder = "https://hooks.slack.com/services/…", helpUrl = "https://api.slack.com/messaging/webhooks" },
                    new { key = "channel", label = "Channel override (optional)", input = "text", required = false, placeholder = "#incidents" },
                    new { key = "username", label = "Bot username (optional)", input = "text", required = false, placeholder = "Callu" },
                    new { key = "iconEmoji", label = "Icon emoji (optional)", input = "text", required = false, placeholder = ":rotating_light:" },
                },
            },
            new
            {
                value = "MicrosoftTeams",
                label = "Microsoft Teams",
                icon = "👥",
                description = "Office 365 / Teams Incoming Webhook (MessageCard). Fires on new incident creation.",
                fields = new object[]
                {
                    new { key = "webhookUrl", label = "Incoming webhook URL", input = "url", required = true, placeholder = "https://outlook.office.com/webhook/…", helpUrl = "https://learn.microsoft.com/microsoftteams/platform/webhooks-and-connectors/how-to/add-incoming-webhook" },
                },
            },
            new
            {
                value = "Email",
                label = "Email",
                icon = "📧",
                description = "Sends to a shared address (e.g. ops mailing list). Separate from per-user escalation email.",
                fields = new object[]
                {
                    new { key = "to", label = "To address", input = "email", required = true, placeholder = "ops@company.com" },
                    new { key = "subject", label = "Subject prefix (optional)", input = "text", required = false, placeholder = "Callu alert" },
                },
            },
            new
            {
                value = "Webhook",
                label = "Custom Webhook",
                icon = "🔗",
                description = "POST or PUT JSON to your endpoint. Optional X-Webhook-Secret header.",
                fields = new object[]
                {
                    new { key = "url", label = "Endpoint URL", input = "url", required = true, placeholder = "https://api.example.com/callu/incidents" },
                    new
                    {
                        key = "method",
                        label = "HTTP method",
                        input = "select",
                        required = false,
                        options = new object[] { new { value = "POST", label = "POST" }, new { value = "PUT", label = "PUT" } },
                    },
                    new { key = "secret", label = "Shared secret (optional)", input = "password", required = false, placeholder = "Sent as X-Webhook-Secret" },
                },
            },
        });
    }

    /// <summary>
    /// Get severity options for channel filtering
    /// </summary>
    [HttpGet("severity-options")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult GetSeverityOptions()
    {
        return Ok(new[] { "Low", "Medium", "High", "Critical" });
    }
}
