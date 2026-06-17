using System.Text;
using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Utilities;
using Callu.Shared.Extensions;
using Callu.Shared.Models.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Services;

public class NotificationChannelService(
    IRepository<NotificationChannel> repo,
    IRepository<NotificationChannelDelivery> deliveryRepo,
    IUnitOfWork unitOfWork,
    IHttpClientFactory httpClientFactory,
    IEmailService emailService,
    ILogger<NotificationChannelService> logger) : INotificationChannelService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const int MaxRetryAttempts = 5;

    private enum ChannelSendStatus { Delivered, Transient, Permanent }

    private readonly record struct SendResult(ChannelSendStatus Status, int? HttpStatus, string? Error)
    {
        public static readonly SendResult Ok = new(ChannelSendStatus.Delivered, 200, null);
        public static SendResult Transient(int? status, string error) => new(ChannelSendStatus.Transient, status, error);
        public static SendResult Permanent(int? status, string error) => new(ChannelSendStatus.Permanent, status, error);
    }

    /// <summary>5xx / 408 / 429 are worth retrying; other 4xx are caller/config errors that won't fix themselves.</summary>
    private static SendResult ClassifyHttp(int status, string body)
    {
        var retryable = status >= 500 || status == 408 || status == 429;
        var error = $"HTTP {status}: {(body.Length > 300 ? body[..300] : body)}";
        return retryable ? SendResult.Transient(status, error) : SendResult.Permanent(status, error);
    }

    /// <summary>Backoff ladder per attempt; null = give up (terminal Failed) after <see cref="MaxRetryAttempts"/>.</summary>
    private static TimeSpan? BackoffFor(int attemptCount) => attemptCount switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        4 => TimeSpan.FromHours(1),
        5 => TimeSpan.FromHours(6),
        _ => null,
    };

    private static void ApplyResult(NotificationChannelDelivery delivery, SendResult result)
    {
        delivery.HttpStatus = result.HttpStatus;
        delivery.Error = result.Error;

        switch (result.Status)
        {
            case ChannelSendStatus.Delivered:
                delivery.Status = NotificationChannelDeliveryStatus.Succeeded;
                delivery.NextRetryAt = null;
                break;
            case ChannelSendStatus.Permanent:
                delivery.Status = NotificationChannelDeliveryStatus.Failed;
                delivery.NextRetryAt = null;
                break;
            default:
                var delay = BackoffFor(delivery.AttemptCount);
                if (delay is null)
                {
                    delivery.Status = NotificationChannelDeliveryStatus.Failed;
                    delivery.NextRetryAt = null;
                }
                else
                {
                    delivery.Status = NotificationChannelDeliveryStatus.Retrying;
                    delivery.NextRetryAt = DateTime.UtcNow.Add(delay.Value);
                }
                break;
        }
    }

    public async Task<List<NotificationChannelDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await repo.GetQueryable()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return items.Select(MapToDto).ToList();
    }

    public async Task<NotificationChannelDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var item = await repo.GetByIdAsync(id, ct);
        return item == null ? null : MapToDto(item);
    }

    public async Task<NotificationChannelDto> CreateAsync(CreateNotificationChannelRequest request, CancellationToken ct = default)
    {
        var channelType = Enum.TryParse<NotificationChannelType>(request.ChannelType, ignoreCase: true, out var parsedType)
            ? parsedType
            : NotificationChannelType.Slack;

        NotificationChannelConfigurationGuard.EnsureValid(
            channelType,
            request.Configuration,
            request.NotifyOnIncidentCreated,
            request.NotifyOnIncidentAcknowledged,
            request.NotifyOnIncidentResolved);

        var entity = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            Name = request.Name.NormalizedTrim(),
            ChannelType = channelType,
            ConfigurationJson = JsonSerializer.Serialize(request.Configuration, JsonOpts),
            MinimumSeverity = request.MinimumSeverity,
            ServiceFilterJson = JsonSerializer.Serialize(request.ServiceFilter, JsonOpts),
            NotifyOnIncidentCreated = request.NotifyOnIncidentCreated,
            NotifyOnIncidentAcknowledged = request.NotifyOnIncidentAcknowledged,
            NotifyOnIncidentResolved = request.NotifyOnIncidentResolved,
            CreatedAt = DateTime.UtcNow,
        };

        await repo.AddAsync(entity, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Notification channel created: {Name} ({Type})", entity.Name, entity.ChannelType);
        return MapToDto(entity);
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateNotificationChannelRequest request, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, ct);
        if (entity == null) return false;

        NotificationChannelConfigurationGuard.EnsureValid(
            entity.ChannelType,
            request.Configuration,
            request.NotifyOnIncidentCreated,
            request.NotifyOnIncidentAcknowledged,
            request.NotifyOnIncidentResolved);

        entity.Name = request.Name.NormalizedTrim();
        entity.ConfigurationJson = JsonSerializer.Serialize(request.Configuration, JsonOpts);
        entity.IsEnabled = request.IsEnabled;
        entity.MinimumSeverity = request.MinimumSeverity;
        entity.ServiceFilterJson = JsonSerializer.Serialize(request.ServiceFilter, JsonOpts);
        entity.NotifyOnIncidentCreated = request.NotifyOnIncidentCreated;
        entity.NotifyOnIncidentAcknowledged = request.NotifyOnIncidentAcknowledged;
        entity.NotifyOnIncidentResolved = request.NotifyOnIncidentResolved;
        entity.UpdatedAt = DateTime.UtcNow;

        repo.Update(entity);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, ct);
        if (entity == null) return false;

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        repo.Update(entity);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ToggleAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, ct);
        if (entity == null) return false;

        entity.IsEnabled = !entity.IsEnabled;
        entity.UpdatedAt = DateTime.UtcNow;
        repo.Update(entity);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> TestAsync(Guid id, string message, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, ct);
        if (entity == null) return false;

        var config = DeserializeConfig(entity.ConfigurationJson);
        
        try
        {
            if (!IsChannelConfigurationValid(entity.ChannelType, config))
            {
                logger.LogWarning("Test skipped — invalid configuration for {Channel}: {Name}", entity.ChannelType, entity.Name);
                return false;
            }

            var testEnvelope = new IncidentDispatchEnvelope(
                "channel.test",
                null,
                null,
                null,
                null,
                $"🧪 Test: {message}");
            var result = await SendNotificationAsync(entity.ChannelType, config, testEnvelope, ct);
            if (result.Status == ChannelSendStatus.Delivered)
            {
                logger.LogInformation("Test notification sent via {Channel}: {Name}", entity.ChannelType, entity.Name);
                return true;
            }

            logger.LogWarning("Test notification not delivered via {Channel}: {Name} — {Error}",
                entity.ChannelType, entity.Name, result.Error);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send test notification via {Channel}: {Name}", entity.ChannelType, entity.Name);
            return false;
        }
    }

    public async Task DispatchIncidentNotificationAsync(
        Guid incidentId,
        string title,
        string severity,
        Guid? serviceId,
        NotificationChannelDispatchEvent dispatchEvent = NotificationChannelDispatchEvent.IncidentCreated,
        CancellationToken ct = default)
    {
        var channels = await repo.GetQueryable()
            .Where(c => c.IsEnabled && !c.IsDeleted)
            .ToListAsync(ct);

        var severityOrder = new[] { "Low", "Medium", "High", "Critical" };
        var incidentSeverityIndex = SeverityRank(severity, severityOrder);

        foreach (var channel in channels)
        {
            if (!MatchesDispatchTrigger(channel, dispatchEvent))
                continue;

            if (!string.IsNullOrEmpty(channel.MinimumSeverity))
            {
                var minIndex = SeverityRank(channel.MinimumSeverity, severityOrder);
                if (incidentSeverityIndex < minIndex) continue;
            }

            var serviceFilter = DeserializeServiceFilter(channel.ServiceFilterJson);
            if (serviceFilter.Count > 0 && serviceId.HasValue && !serviceFilter.Contains(serviceId.Value))
                continue;

            var eventKey = EventKey(dispatchEvent);
            var messageText = BuildHumanMessage(dispatchEvent, severity, title, incidentId);
            var envelope = new IncidentDispatchEnvelope(eventKey, incidentId, title, severity, serviceId, messageText);

            SendResult result;
            try
            {
                var config = DeserializeConfig(channel.ConfigurationJson);
                result = await SendNotificationAsync(channel.ChannelType, config, envelope, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Unexpected error dispatching via channel {Name}", channel.Name);
                result = SendResult.Transient(null, ex.Message);
            }

            var delivery = new NotificationChannelDelivery
            {
                Id = Guid.NewGuid(),
                ChannelId = channel.Id,
                IncidentId = incidentId,
                ServiceId = serviceId,
                EventKey = eventKey,
                Title = title.Length > 500 ? title[..500] : title,
                Severity = severity,
                MessageText = messageText.Length > 2000 ? messageText[..2000] : messageText,
                AttemptCount = 1,
                AttemptedAt = DateTime.UtcNow,
            };
            ApplyResult(delivery, result);
            await deliveryRepo.AddAsync(delivery, ct);

            if (result.Status == ChannelSendStatus.Delivered)
            {
                channel.LastNotifiedAt = DateTime.UtcNow;
                channel.NotificationCount++;
                repo.Update(channel);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Re-fires notification-channel deliveries that previously hit a transient failure and
    /// whose backoff has elapsed. Called by NotificationChannelDeliveryRetryQuartzJob.
    /// </summary>
    public async Task ProcessDueRetriesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = await deliveryRepo.GetQueryable()
            .Where(d => d.Status == NotificationChannelDeliveryStatus.Retrying && d.NextRetryAt != null && d.NextRetryAt <= now)
            .OrderBy(d => d.NextRetryAt)
            .Take(50)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        foreach (var delivery in due)
        {
            var channel = await repo.GetByIdAsync(delivery.ChannelId, ct);
            if (channel is null || channel.IsDeleted || !channel.IsEnabled)
            {
                delivery.Status = NotificationChannelDeliveryStatus.Failed;
                delivery.Error = "Channel removed or disabled before retry";
                delivery.NextRetryAt = null;
                delivery.UpdatedAt = now;
                deliveryRepo.Update(delivery);
                continue;
            }

            var envelope = new IncidentDispatchEnvelope(
                delivery.EventKey, delivery.IncidentId, delivery.Title, delivery.Severity, delivery.ServiceId, delivery.MessageText);

            SendResult result;
            try
            {
                var config = DeserializeConfig(channel.ConfigurationJson);
                result = await SendNotificationAsync(channel.ChannelType, config, envelope, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = SendResult.Transient(null, ex.Message);
            }

            delivery.AttemptCount++;
            delivery.AttemptedAt = now;
            ApplyResult(delivery, result);
            deliveryRepo.Update(delivery);

            if (result.Status == ChannelSendStatus.Delivered)
            {
                channel.LastNotifiedAt = now;
                channel.NotificationCount++;
                repo.Update(channel);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static bool IsChannelConfigurationValid(NotificationChannelType type, Dictionary<string, string> config) =>
        type switch
        {
            NotificationChannelType.Slack => config.TryGetValue("webhookUrl", out var u) && !string.IsNullOrWhiteSpace(u),
            NotificationChannelType.MicrosoftTeams => config.TryGetValue("webhookUrl", out var u) && !string.IsNullOrWhiteSpace(u),
            NotificationChannelType.Webhook => config.TryGetValue("url", out var u) && !string.IsNullOrWhiteSpace(u),
            NotificationChannelType.Email => config.TryGetValue("to", out var t) && !string.IsNullOrWhiteSpace(t) && t.Contains('@'),
            _ => false,
        };

    private readonly record struct IncidentDispatchEnvelope(
        string EventKey,
        Guid? IncidentId,
        string? Title,
        string? Severity,
        Guid? ServiceId,
        string MessageText);

    private static bool MatchesDispatchTrigger(NotificationChannel channel, NotificationChannelDispatchEvent ev) =>
        ev switch
        {
            NotificationChannelDispatchEvent.IncidentCreated => channel.NotifyOnIncidentCreated,
            NotificationChannelDispatchEvent.IncidentAcknowledged => channel.NotifyOnIncidentAcknowledged,
            NotificationChannelDispatchEvent.IncidentResolved => channel.NotifyOnIncidentResolved,
            _ => false,
        };

    private static string EventKey(NotificationChannelDispatchEvent ev) =>
        ev switch
        {
            NotificationChannelDispatchEvent.IncidentCreated => "incident.created",
            NotificationChannelDispatchEvent.IncidentAcknowledged => "incident.acknowledged",
            NotificationChannelDispatchEvent.IncidentResolved => "incident.resolved",
            _ => "incident.created",
        };

    private static string BuildHumanMessage(
        NotificationChannelDispatchEvent ev,
        string severity,
        string title,
        Guid incidentId) =>
        ev switch
        {
            NotificationChannelDispatchEvent.IncidentCreated => $"🚨 [{severity}] Incident: {title}\nID: {incidentId}",
            NotificationChannelDispatchEvent.IncidentAcknowledged => $"✅ Acknowledged: [{severity}] {title}\nID: {incidentId}",
            NotificationChannelDispatchEvent.IncidentResolved => $"✅ Resolved: [{severity}] {title}\nID: {incidentId}",
            _ => $"[{severity}] {title}\nID: {incidentId}",
        };

    private static int SeverityRank(string severity, string[] order)
    {
        var idx = Array.FindIndex(order, s => s.Equals(severity, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? idx : 0;
    }

    private async Task<SendResult> SendNotificationAsync(
        NotificationChannelType type,
        Dictionary<string, string> config,
        IncidentDispatchEnvelope envelope,
        CancellationToken ct)
    {
        return type switch
        {
            NotificationChannelType.Slack => await SendSlackAsync(config, envelope.MessageText, ct),
            NotificationChannelType.MicrosoftTeams => await SendTeamsAsync(config, envelope.MessageText, ct),
            NotificationChannelType.Webhook => await SendGenericWebhookAsync(config, envelope, ct),
            NotificationChannelType.Email => await SendEmailChannelAsync(config, envelope.MessageText, ct),
            _ => SendResult.Permanent(null, $"Unsupported channel type: {type}"),
        };
    }

    /// <summary>
    /// Slack Incoming Webhook — POST JSON { "text": "..." }
    /// Config: webhookUrl (required), channel (optional), username (optional), iconEmoji (optional)
    /// </summary>
    private async Task<SendResult> SendSlackAsync(Dictionary<string, string> config, string message, CancellationToken ct)
    {
        if (!config.TryGetValue("webhookUrl", out var webhookUrl) || string.IsNullOrWhiteSpace(webhookUrl))
            return SendResult.Permanent(null, "Missing 'webhookUrl' in channel configuration");
        if (!UrlSanitizer.IsValidHealthCheckUrl(webhookUrl))
            return SendResult.Permanent(null, $"Blocked outbound URL: {UrlSanitizer.GetBlockedReason(webhookUrl)}");

        var payload = new Dictionary<string, object> { ["text"] = message };
        if (config.TryGetValue("channel", out var ch) && !string.IsNullOrWhiteSpace(ch))
            payload["channel"] = ch;
        if (config.TryGetValue("username", out var usr) && !string.IsNullOrWhiteSpace(usr))
            payload["username"] = usr;
        if (config.TryGetValue("iconEmoji", out var ico) && !string.IsNullOrWhiteSpace(ico))
            payload["icon_emoji"] = ico;

        var client = httpClientFactory.CreateClient("WebhookDispatch");
        using var content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(webhookUrl, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("[SLACK] Webhook {Url} returned {Status}", webhookUrl, response.StatusCode);
                return ClassifyHttp((int)response.StatusCode, body);
            }
            logger.LogDebug("[SLACK] Notification sent to {Url}", webhookUrl);
            return SendResult.Ok;
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[SLACK] Transport error to {Url}", webhookUrl);
            return SendResult.Transient(null, ex.Message);
        }
    }

    /// <summary>
    /// Microsoft Teams Incoming Webhook — simple text-based MessageCard.
    /// Uses MessageCard format for maximum compatibility; can be upgraded to Adaptive Card later.
    /// Config: webhookUrl (required)
    /// </summary>
    private async Task<SendResult> SendTeamsAsync(Dictionary<string, string> config, string message, CancellationToken ct)
    {
        if (!config.TryGetValue("webhookUrl", out var webhookUrl) || string.IsNullOrWhiteSpace(webhookUrl))
            return SendResult.Permanent(null, "Missing 'webhookUrl' in channel configuration");
        if (!UrlSanitizer.IsValidHealthCheckUrl(webhookUrl))
            return SendResult.Permanent(null, $"Blocked outbound URL: {UrlSanitizer.GetBlockedReason(webhookUrl)}");

        var payload = new Dictionary<string, object>
        {
            ["@type"] = "MessageCard",
            ["@context"] = "https://schema.org/extensions",
            ["themeColor"] = "d63384",
            ["summary"] = "CalluApp Alert",
            ["sections"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["activityTitle"] = "CalluApp Alert",
                    ["text"] = message,
                },
            },
        };

        var client = httpClientFactory.CreateClient("WebhookDispatch");
        using var content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(webhookUrl, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("[TEAMS] Webhook {Url} returned {Status}", webhookUrl, response.StatusCode);
                return ClassifyHttp((int)response.StatusCode, body);
            }
            logger.LogDebug("[TEAMS] Notification sent to {Url}", webhookUrl);
            return SendResult.Ok;
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[TEAMS] Transport error to {Url}", webhookUrl);
            return SendResult.Transient(null, ex.Message);
        }
    }

    /// <summary>
    /// Generic Webhook — POST/PUT JSON payload with optional X-Webhook-Secret header.
    /// Config: url (required), secret (optional), method (optional — defaults to POST)
    /// </summary>
    private async Task<SendResult> SendGenericWebhookAsync(Dictionary<string, string> config, IncidentDispatchEnvelope envelope, CancellationToken ct)
    {
        if (!config.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
            return SendResult.Permanent(null, "Missing 'url' in channel configuration");
        if (!UrlSanitizer.IsValidHealthCheckUrl(url))
            return SendResult.Permanent(null, $"Blocked outbound URL: {UrlSanitizer.GetBlockedReason(url)}");

        var payload = new Dictionary<string, object?>
        {
            ["source"] = "CalluApp",
            ["timestamp"] = DateTime.UtcNow,
            ["event"] = envelope.EventKey,
            ["incidentId"] = envelope.IncidentId,
            ["title"] = envelope.Title,
            ["severity"] = envelope.Severity,
            ["serviceId"] = envelope.ServiceId,
            ["message"] = envelope.MessageText,
        };

        var method = config.TryGetValue("method", out var m) && m.Equals("PUT", StringComparison.OrdinalIgnoreCase)
            ? HttpMethod.Put
            : HttpMethod.Post;

        var client = httpClientFactory.CreateClient("WebhookDispatch");
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json")
        };

        if (config.TryGetValue("secret", out var secret) && !string.IsNullOrWhiteSpace(secret))
            request.Headers.TryAddWithoutValidation("X-Webhook-Secret", secret);

        try
        {
            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("[WEBHOOK] {Url} returned {Status}", url, response.StatusCode);
                return ClassifyHttp((int)response.StatusCode, body);
            }
            logger.LogDebug("[WEBHOOK] Notification sent to {Url}", url);
            return SendResult.Ok;
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[WEBHOOK] Transport error to {Url}", url);
            return SendResult.Transient(null, ex.Message);
        }
    }

    /// <summary>
    /// Email channel — delegates to existing SMTP service for ops mailing list notifications.
    /// Config: to (required — must contain '@'), subject (optional)
    /// </summary>
    private async Task<SendResult> SendEmailChannelAsync(Dictionary<string, string> config, string message, CancellationToken ct)
    {
        if (!config.TryGetValue("to", out var to) || string.IsNullOrWhiteSpace(to))
            return SendResult.Permanent(null, "Missing 'to' in channel configuration");

        if (!to.Contains('@'))
            return SendResult.Permanent(null, $"Invalid email format: {to}");

        var subject = config.TryGetValue("subject", out var subj) && !string.IsNullOrWhiteSpace(subj)
            ? subj
            : "CalluApp Alert";

        var htmlBody = $"<div style=\"font-family:sans-serif;max-width:600px;\"><p>{System.Net.WebUtility.HtmlEncode(message).Replace("\n", "<br/>")}</p></div>";

        var sent = await emailService.SendAsync(to, subject, htmlBody, ct);
        if (sent)
        {
            logger.LogDebug("[CHANNEL-EMAIL] Notification sent to {To}", to);
            return SendResult.Ok;
        }

        logger.LogWarning("[CHANNEL-EMAIL] Email service returned false for {To}", to);
        return SendResult.Transient(null, "Email service returned false");
    }

    private static Dictionary<string, string> DeserializeConfig(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts) ?? []; }
        catch { return []; }
    }

    private static List<Guid> DeserializeServiceFilter(string json)
    {
        try { return JsonSerializer.Deserialize<List<Guid>>(json, JsonOpts) ?? []; }
        catch { return []; }
    }

    private static NotificationChannelDto MapToDto(NotificationChannel c)
    {
        return new NotificationChannelDto
        {
            Id = c.Id,
            Name = c.Name,
            ChannelType = c.ChannelType.ToString(),
            Configuration = DeserializeConfig(c.ConfigurationJson),
            IsEnabled = c.IsEnabled,
            MinimumSeverity = c.MinimumSeverity,
            ServiceFilter = DeserializeServiceFilter(c.ServiceFilterJson),
            NotifyOnIncidentCreated = c.NotifyOnIncidentCreated,
            NotifyOnIncidentAcknowledged = c.NotifyOnIncidentAcknowledged,
            NotifyOnIncidentResolved = c.NotifyOnIncidentResolved,
            LastNotifiedAt = c.LastNotifiedAt,
            NotificationCount = c.NotificationCount,
            CreatedAt = c.CreatedAt,
        };
    }
}
