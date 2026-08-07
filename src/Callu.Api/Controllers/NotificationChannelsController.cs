using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Domain.Entities;
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
    /// Delivery attempts for one channel, newest first
    /// </summary>
    [HttpGet("{id:guid}/deliveries")]
    public async Task<IActionResult> GetDeliveries(
        Guid id,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var channel = await channelService.GetByIdAsync(id, ct);
        if (channel == null) return NotFound();

        return Ok(await channelService.GetDeliveriesAsync(id, page, pageSize, ct));
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
                description = "Posts plain text to one channel through an Incoming Webhook. Choose which incident events it fires on below.",
                notice = new
                {
                    level = "info",
                    text = "The channel, the posting name and the icon are fixed when the webhook is created in Slack — Slack ignores any override sent with the message. To post to a second channel, create a second webhook and add it here as another channel.",
                },
                samplePayload = channelService.BuildSamplePayloadJson(NotificationChannelType.Slack),
                fields = new object[]
                {
                    new { key = "webhookUrl", label = "Incoming webhook URL", input = "url", required = true, placeholder = "https://hooks.slack.com/services/…", helpUrl = "https://api.slack.com/messaging/webhooks" },
                },
            },
            new
            {
                value = "MicrosoftTeams",
                label = "Microsoft Teams",
                icon = "👥",
                description = "Posts a MessageCard to one channel through an Incoming Webhook. Choose which incident events it fires on below.",
                notice = new
                {
                    level = "warning",
                    text = "This uses the Office 365 connector webhook, which Microsoft is retiring. Check that your tenant still accepts it; a custom webhook channel is the fallback if it stops working.",
                },
                samplePayload = channelService.BuildSamplePayloadJson(NotificationChannelType.MicrosoftTeams),
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
                samplePayload = channelService.BuildSamplePayloadJson(NotificationChannelType.Email),
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
                description = "POST or PUT JSON to your endpoint. Optional X-Webhook-Secret header. Choose which incident events it fires on below.",
                samplePayload = channelService.BuildSamplePayloadJson(NotificationChannelType.Webhook),
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
