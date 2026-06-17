using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Callu.Application.Services;
using Callu.Shared.Localization;
using Callu.Shared.Models.StatusPages;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/status-pages")]
[Authorize(Policy = Policies.CanManageSettings)]
public class StatusPagesController(
    IStatusPageService statusPageService,
    IStatusPageComponentService componentService,
    IHealthCheckExecutor healthCheckExecutor) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var pages = await statusPageService.GetAllAsync(ct);
        return Ok(pages);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var page = await statusPageService.GetByIdAsync(id, ct);
        if (page == null) return NotFound();
        return Ok(page);
    }

    [HttpGet("slug/{slug}")]
    [AllowAnonymous]
    [EnableRateLimiting("statuspage_public")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var page = await statusPageService.GetBySlugPublicAsync(slug, ct);
        if (page == null) return NotFound();
        return Ok(page);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStatusPageRequest request, CancellationToken ct)
    {
        var page = await statusPageService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = page.Id }, page);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStatusPageRequest request, CancellationToken ct)
    {
        var success = await statusPageService.UpdateAsync(id, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await statusPageService.DeleteAsync(id, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpPost("{pageId:guid}/components")]
    public async Task<IActionResult> AddComponent(Guid pageId, [FromBody] AddComponentRequest request, CancellationToken ct)
    {
        var success = await componentService.AddComponentAsync(pageId, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpPut("components/{componentId:guid}")]
    public async Task<IActionResult> UpdateComponent(Guid componentId, [FromBody] UpdateComponentRequest request, CancellationToken ct)
    {
        var success = await componentService.UpdateComponentAsync(componentId, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpDelete("components/{componentId:guid}")]
    public async Task<IActionResult> RemoveComponent(Guid componentId, CancellationToken ct)
    {
        var success = await componentService.RemoveComponentAsync(componentId, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Execute a single health check for a component (manual test)
    /// </summary>
    [HttpPost("components/{componentId:guid}/health-check/test")]
    public async Task<IActionResult> TestHealthCheck(Guid componentId, CancellationToken ct)
    {
        var result = await healthCheckExecutor.ExecuteSingleCheckAsync(componentId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Sniffer mode: send request to health check URL and capture the response
    /// </summary>
    [HttpPost("components/{componentId:guid}/health-check/sniff")]
    public async Task<IActionResult> SniffHealthCheck(Guid componentId, CancellationToken ct)
    {
        var result = await healthCheckExecutor.SniffAsync(componentId, ct);
        return Ok(result);
    }

    [HttpPost("{pageId:guid}/incidents")]
    public async Task<IActionResult> CreateIncident(Guid pageId, [FromBody] CreateStatusIncidentRequest request, CancellationToken ct)
    {
        var incident = await statusPageService.CreateIncidentAsync(pageId, request, ct);
        if (incident == null) return NotFound();
        return Ok(incident);
    }

    [HttpPost("incidents/{incidentId:guid}/updates")]
    public async Task<IActionResult> AddIncidentUpdate(Guid incidentId, [FromBody] AddIncidentUpdateRequest request, CancellationToken ct)
    {
        var success = await statusPageService.AddIncidentUpdateAsync(incidentId, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpGet("{pageId:guid}/stats")]
    public async Task<IActionResult> GetStats(Guid pageId, CancellationToken ct)
    {
        var stats = await statusPageService.GetStatsAsync(pageId, ct);
        return Ok(stats);
    }

    [HttpGet("{pageId:guid}/uptime")]
    [AllowAnonymous]
    [EnableRateLimiting("statuspage_public")]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetUptime(Guid pageId, [FromQuery] int days = 30, CancellationToken ct = default)
    {
        var uptime = await statusPageService.GetUptimeAsync(pageId, days, ct);
        return Ok(uptime);
    }

    [HttpPost("{pageId:guid}/view")]
    [AllowAnonymous]
    [EnableRateLimiting("statuspage_view")]
    public async Task<IActionResult> RecordView(Guid pageId, CancellationToken ct)
    {
        var visitorHash = HashVisitor(HttpContext.Connection.RemoteIpAddress?.ToString());
        await statusPageService.RecordPageViewAsync(pageId, visitorHash, ct);
        return NoContent();
    }

    [HttpPost("{pageId:guid}/subscribe")]
    [AllowAnonymous]
    [EnableRateLimiting("statuspage_subscribe")]
    public async Task<IActionResult> Subscribe(Guid pageId, [FromBody] SubscribeRequest request, CancellationToken ct)
    {
        var success = await statusPageService.SubscribeAsync(pageId, request.Email, ct);
        if (!success) return NotFound();
        return Ok(new { message = Messages.Get("statusPages.subscribed") });
    }

    [HttpDelete("{pageId:guid}/subscribe")]
    public async Task<IActionResult> Unsubscribe(Guid pageId, [FromQuery] string email, CancellationToken ct)
    {
        var success = await statusPageService.UnsubscribeAsync(pageId, email, ct);
        if (!success) return NotFound();
        return Ok(new { message = Messages.Get("statusPages.unsubscribed") });
    }

    /// <summary>
    /// Double opt-in confirmation. Anonymous; the token in the URL is the proof of
    /// email ownership. Idempotent — confirming an already-confirmed or unknown
    /// token returns the same 200 to avoid revealing subscription state.
    /// </summary>
    [HttpGet("subscriptions/confirm")]
    [AllowAnonymous]
    [EnableRateLimiting("statuspage_subscribe")]
    public async Task<IActionResult> ConfirmSubscription([FromQuery] string token, CancellationToken ct)
    {
        await statusPageService.ConfirmSubscriptionAsync(token, ct);
        return Ok(new { message = Messages.Get("statusPages.subscriptionConfirmed") });
    }

    /// <summary>
    /// One-click unsubscribe — RFC 8058 compliant target for List-Unsubscribe headers.
    /// Anonymous; the token is the auth.
    /// </summary>
    [HttpGet("subscriptions/unsubscribe")]
    [AllowAnonymous]
    [EnableRateLimiting("statuspage_subscribe")]
    public async Task<IActionResult> UnsubscribeByToken([FromQuery] string token, CancellationToken ct)
    {
        await statusPageService.UnsubscribeByTokenAsync(token, ct);
        return Ok(new { message = Messages.Get("statusPages.unsubscribed") });
    }

    /// <summary>
    /// One-way visitor identifier — SHA-256 truncated to 16 hex chars. Replaces the
    /// previous GetHashCode()-based hash which is not cryptographic, varies between
    /// process restarts on .NET, and reveals patterns about visitor IPs. Combined
    /// with a per-process salt so the hash isn't trivially rainbow-table reversible
    /// to a known IP set.
    /// </summary>
    private static readonly string _visitorHashSalt = Guid.NewGuid().ToString("N");
    private static string? HashVisitor(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return null;
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(_visitorHashSalt + "|" + ip));
        return Convert.ToHexString(bytes, 0, 8);
    }

    [HttpGet("{pageId:guid}/subscribers")]
    public async Task<IActionResult> GetSubscribers(Guid pageId, CancellationToken ct)
    {
        var subscribers = await statusPageService.GetSubscribersAsync(pageId, ct);
        return Ok(subscribers);
    }

    [HttpDelete("{pageId:guid}/subscribers/{email}")]
    public async Task<IActionResult> RemoveSubscriber(Guid pageId, string email, CancellationToken ct)
    {
        var success = await statusPageService.UnsubscribeAsync(pageId, Uri.UnescapeDataString(email), ct);
        if (!success) return NotFound();
        return NoContent();
    }
}
