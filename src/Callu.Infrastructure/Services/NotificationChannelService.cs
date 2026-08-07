using System.Text;
using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Providers;
using Callu.Infrastructure.Utilities;
using Callu.Shared.Extensions;
using Callu.Shared;
using Callu.Shared.Models.Notifications;
using Callu.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Callu.Infrastructure.Services;

public class NotificationChannelService(
    IRepository<NotificationChannel> repo,
    IRepository<NotificationChannelDelivery> deliveryRepo,
    IUnitOfWork unitOfWork,
    IHttpClientFactory httpClientFactory,
    IEmailService emailService,
    ProviderSecretProtector secretProtector,
    IOrganizationSettingsService organizationSettings,
    IOptions<CommunicationSettingsOptions> communicationOptions,
    IAuditLogService auditLogService,
    ILogger<NotificationChannelService> logger) : INotificationChannelService
{
    private const string ChannelEntity = "NotificationChannel";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly JsonSerializerOptions SampleJsonOpts =
        new(JsonOpts) { WriteIndented = true };

    private bool AllowPrivateWebhook => communicationOptions.Value.AllowPrivateWebhookEndpoint;

    private const int MaxRetryAttempts = 5;

    internal const int DefaultDeliveryPageSize = 20;

    private static readonly string[] SlackTeamsSecretKeys = ["webhookUrl"];
    private static readonly string[] WebhookSecretKeys = ["secret"];

    private static string[] SecretKeysFor(NotificationChannelType type) => type switch
    {
        NotificationChannelType.Slack => SlackTeamsSecretKeys,
        NotificationChannelType.MicrosoftTeams => SlackTeamsSecretKeys,
        NotificationChannelType.Webhook => WebhookSecretKeys,
        _ => [],
    };

    private static string MaskSecret(string? plaintext) =>
        string.IsNullOrEmpty(plaintext) ? "" : "••••" + plaintext[^Math.Min(4, plaintext.Length)..];

    private Dictionary<string, string> ProtectSecrets(NotificationChannelType type, IDictionary<string, string> config)
    {
        var result = new Dictionary<string, string>(config);
        foreach (var key in SecretKeysFor(type))
        {
            if (result.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
                result[key] = secretProtector.Protect(v);
        }
        return result;
    }

    private Dictionary<string, string> UnprotectSecrets(NotificationChannelType type, Dictionary<string, string> config)
    {
        foreach (var key in SecretKeysFor(type))
        {
            if (config.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
                config[key] = secretProtector.Unprotect(v);
        }
        return config;
    }

    private Dictionary<string, string> MaskSecrets(NotificationChannelType type, Dictionary<string, string> config)
    {
        foreach (var key in SecretKeysFor(type))
        {
            if (config.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
                config[key] = MaskSecret(secretProtector.Unprotect(v));
        }
        return config;
    }

    private Dictionary<string, string> ReconcileSecrets(
        NotificationChannelType type,
        IReadOnlyDictionary<string, string> incoming,
        Dictionary<string, string> storedEncrypted)
    {
        var result = new Dictionary<string, string>(incoming);
        foreach (var key in SecretKeysFor(type))
        {
            var existingPlain = secretProtector.Unprotect(storedEncrypted.GetValueOrDefault(key));
            var inc = result.GetValueOrDefault(key);
            if (string.IsNullOrWhiteSpace(inc) || inc == MaskSecret(existingPlain))
            {
                if (string.IsNullOrEmpty(existingPlain)) result.Remove(key);
                else result[key] = existingPlain;
            }
        }
        return result;
    }

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
        // The latest attempt per channel comes back with the channels rather than one query each:
        // this list is the settings screen, and a request per channel is what it would otherwise be.
        var rows = await repo.GetQueryable()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Name)
            .Select(c => new
            {
                Channel = c,
                Last = deliveryRepo.GetQueryable()
                    .Where(d => d.ChannelId == c.Id)
                    .OrderByDescending(d => d.AttemptedAt)
                    .Select(d => new { d.Status, d.AttemptedAt })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var dto = MapToDto(r.Channel);
            if (r.Last is not null)
            {
                dto.LastDeliveryStatus = r.Last.Status.ToString();
                dto.LastDeliveryAt = r.Last.AttemptedAt;
            }
            return dto;
        }).ToList();
    }

    public async Task<PagedResult<NotificationChannelDeliveryDto>> GetDeliveriesAsync(
        Guid channelId,
        int page = 1,
        int pageSize = DefaultDeliveryPageSize,
        CancellationToken ct = default)
    {
        // Clamped here rather than at the controller: this is a read a Viewer can reach, and an
        // unbounded page over a table that grows per incident is an outage, not a slow response.
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, AppConstants.Pagination.MaxPageSize);

        var query = deliveryRepo.GetQueryable().Where(d => d.ChannelId == channelId);
        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(d => d.AttemptedAt)
            .ThenByDescending(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new NotificationChannelDeliveryDto
            {
                Id = d.Id,
                IncidentId = d.IncidentId,
                EventKey = d.EventKey,
                Title = d.Title,
                Severity = d.Severity,
                MessageText = d.MessageText,
                Status = d.Status.ToString(),
                HttpStatus = d.HttpStatus,
                Error = d.Error,
                AttemptCount = d.AttemptCount,
                AttemptedAt = d.AttemptedAt,
                NextRetryAt = d.NextRetryAt,
            })
            .ToListAsync(ct);

        return new PagedResult<NotificationChannelDeliveryDto>(items, total, page, pageSize);
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
            request.NotifyOnIncidentResolved,
            request.NotifyOnIncidentClosed,
            request.NotifyOnIncidentReopened,
            AllowPrivateWebhook);

        var entity = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            Name = request.Name.NormalizedTrim(),
            ChannelType = channelType,
            ConfigurationJson = JsonSerializer.Serialize(ProtectSecrets(channelType, request.Configuration), JsonOpts),
            MinimumSeverity = request.MinimumSeverity,
            ServiceFilterJson = JsonSerializer.Serialize(request.ServiceFilter, JsonOpts),
            NotifyOnIncidentCreated = request.NotifyOnIncidentCreated,
            NotifyOnIncidentAcknowledged = request.NotifyOnIncidentAcknowledged,
            NotifyOnIncidentResolved = request.NotifyOnIncidentResolved,
            NotifyOnIncidentClosed = request.NotifyOnIncidentClosed,
            NotifyOnIncidentReopened = request.NotifyOnIncidentReopened,
            CreatedAt = DateTime.UtcNow,
        };

        await repo.AddAsync(entity, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // The webhook URL is a credential, so the row names the channel and never its configuration.
        await auditLogService.LogAsync(
            null, AuditAction.SettingsChanged, ChannelEntity, entity.Id.ToString(),
            newValues: $"name={entity.Name}; type={entity.ChannelType}; enabled={entity.IsEnabled}",
            description: "Channel created",
            cancellationToken: ct);

        logger.LogInformation("Notification channel created: {Name} ({Type})", entity.Name, entity.ChannelType);
        return MapToDto(entity);
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateNotificationChannelRequest request, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, ct);
        if (entity == null) return false;

        var storedEncrypted = DeserializeConfig(entity.ConfigurationJson);
        var effectiveConfig = ReconcileSecrets(entity.ChannelType, request.Configuration, storedEncrypted);

        NotificationChannelConfigurationGuard.EnsureValid(
            entity.ChannelType,
            effectiveConfig,
            request.NotifyOnIncidentCreated,
            request.NotifyOnIncidentAcknowledged,
            request.NotifyOnIncidentResolved,
            request.NotifyOnIncidentClosed,
            request.NotifyOnIncidentReopened,
            AllowPrivateWebhook);

        var before = $"name={entity.Name}; enabled={entity.IsEnabled}; severity={entity.MinimumSeverity}";
        var secretsReplaced = SecretKeysFor(entity.ChannelType)
            .Any(k => request.Configuration.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v));

        entity.Name = request.Name.NormalizedTrim();
        entity.ConfigurationJson = JsonSerializer.Serialize(ProtectSecrets(entity.ChannelType, effectiveConfig), JsonOpts);
        entity.IsEnabled = request.IsEnabled;
        entity.MinimumSeverity = request.MinimumSeverity;
        entity.ServiceFilterJson = JsonSerializer.Serialize(request.ServiceFilter, JsonOpts);
        entity.NotifyOnIncidentCreated = request.NotifyOnIncidentCreated;
        entity.NotifyOnIncidentAcknowledged = request.NotifyOnIncidentAcknowledged;
        entity.NotifyOnIncidentResolved = request.NotifyOnIncidentResolved;
        entity.NotifyOnIncidentClosed = request.NotifyOnIncidentClosed;
        entity.NotifyOnIncidentReopened = request.NotifyOnIncidentReopened;
        entity.UpdatedAt = DateTime.UtcNow;

        repo.Update(entity);
        await unitOfWork.SaveChangesAsync(ct);

        await auditLogService.LogAsync(
            null, AuditAction.SettingsChanged, ChannelEntity, entity.Id.ToString(),
            oldValues: before,
            newValues: $"name={entity.Name}; enabled={entity.IsEnabled}; severity={entity.MinimumSeverity}",
            description: secretsReplaced ? "Credentials replaced" : "Credentials unchanged",
            cancellationToken: ct);

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

        await auditLogService.LogAsync(
            null, AuditAction.Deleted, ChannelEntity, entity.Id.ToString(),
            oldValues: $"name={entity.Name}; type={entity.ChannelType}",
            cancellationToken: ct);

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

        await auditLogService.LogAsync(
            null, AuditAction.SettingsChanged, ChannelEntity, entity.Id.ToString(),
            newValues: $"enabled={entity.IsEnabled}",
            description: entity.IsEnabled ? "Channel enabled" : "Channel disabled",
            cancellationToken: ct);

        return true;
    }

    public async Task<bool> TestAsync(Guid id, string message, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, ct);
        if (entity == null) return false;

        var config = UnprotectSecrets(entity.ChannelType, DeserializeConfig(entity.ConfigurationJson));

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

        // Read once per dispatch rather than per channel; a settings lookup must not cost one round
        // trip per configured channel on the paging path.
        var incidentUrl = BuildIncidentUrl(await ReadBaseUrlAsync(ct), incidentId);

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
            if (!ChannelServesIncident(serviceFilter, serviceId))
                continue;

            var eventKey = EventKey(dispatchEvent);
            var messageText = BuildHumanMessage(dispatchEvent, severity, title, incidentId, incidentUrl);
            var envelope = new IncidentDispatchEnvelope(eventKey, incidentId, title, severity, serviceId, messageText);

            SendResult result;
            try
            {
                var config = UnprotectSecrets(channel.ChannelType, DeserializeConfig(channel.ConfigurationJson));
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

    /// <summary>Re-fires channel deliveries that hit a transient failure and whose backoff has elapsed.</summary>
    // Each row is claimed and its outcome committed on its own: one batch commit at the end would, on failure, roll back every
    // attempt counter after the messages had gone out, so the next tick re-sends the same batch forever.
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
                delivery.UpdatedAt = DateTime.UtcNow;
                deliveryRepo.Update(delivery);

                if (!await CommitRetryRowAsync(delivery, "close-out", ct)) return;
                continue;
            }

            if (!await ClaimForRetryAsync(delivery, ct)) return;

            var envelope = new IncidentDispatchEnvelope(
                delivery.EventKey, delivery.IncidentId, delivery.Title, delivery.Severity, delivery.ServiceId, delivery.MessageText);

            SendResult result;
            try
            {
                var config = UnprotectSecrets(channel.ChannelType, DeserializeConfig(channel.ConfigurationJson));
                result = await SendNotificationAsync(channel.ChannelType, config, envelope, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = SendResult.Transient(null, ex.Message);
            }

            ApplyResult(delivery, result);
            deliveryRepo.Update(delivery);

            if (result.Status == ChannelSendStatus.Delivered)
            {
                channel.LastNotifiedAt = DateTime.UtcNow;
                channel.NotificationCount++;
                repo.Update(channel);
            }

            if (!await CommitRetryRowAsync(delivery, "outcome", ct)) return;
        }
    }

    /// <summary>Takes ownership of a due row before any provider I/O, advancing the attempt counter and backoff.</summary>
    // A crash between this commit and the outcome write costs at most one duplicate send, and the attempt limit still moves.
    private async Task<bool> ClaimForRetryAsync(NotificationChannelDelivery delivery, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        delivery.AttemptCount++;
        delivery.AttemptedAt = now;
        delivery.NextRetryAt = now.Add(BackoffFor(delivery.AttemptCount) ?? TimeSpan.FromMinutes(5));
        deliveryRepo.Update(delivery);

        return await CommitRetryRowAsync(delivery, "claim", ct);
    }

    /// <summary>Commits one row of the retry sweep.</summary>
    // A failure leaves the shared DbContext holding a modified entity the next row's commit would replay, so the tick is abandoned.
    private async Task<bool> CommitRetryRowAsync(NotificationChannelDelivery delivery, string stage, CancellationToken ct)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Notification-channel retry sweep abandoned at the {Stage} commit for delivery {DeliveryId}",
                stage, delivery.Id);
            return false;
        }
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

    internal static bool ChannelServesIncident(IReadOnlyCollection<Guid> serviceFilter, Guid? serviceId)
    {
        if (serviceFilter.Count == 0)
            return true;

        return serviceId is { } id && serviceFilter.Contains(id);
    }

    private static bool MatchesDispatchTrigger(NotificationChannel channel, NotificationChannelDispatchEvent ev) =>
        ev switch
        {
            NotificationChannelDispatchEvent.IncidentCreated => channel.NotifyOnIncidentCreated,
            NotificationChannelDispatchEvent.IncidentAcknowledged => channel.NotifyOnIncidentAcknowledged,
            NotificationChannelDispatchEvent.IncidentResolved => channel.NotifyOnIncidentResolved,
            NotificationChannelDispatchEvent.IncidentClosed => channel.NotifyOnIncidentClosed,
            NotificationChannelDispatchEvent.IncidentReopened => channel.NotifyOnIncidentReopened,
            _ => false,
        };

    private static string EventKey(NotificationChannelDispatchEvent ev) =>
        ev switch
        {
            NotificationChannelDispatchEvent.IncidentCreated => "incident.created",
            NotificationChannelDispatchEvent.IncidentAcknowledged => "incident.acknowledged",
            NotificationChannelDispatchEvent.IncidentResolved => "incident.resolved",
            NotificationChannelDispatchEvent.IncidentClosed => "incident.closed",
            NotificationChannelDispatchEvent.IncidentReopened => "incident.reopened",
            _ => "incident.created",
        };

    /// <summary>Reads the configured base URL; a failure costs the link, never the notification.</summary>
    private async Task<string?> ReadBaseUrlAsync(CancellationToken ct)
    {
        try
        {
            return await organizationSettings.GetPublicBaseUrlAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the public base URL; channel messages will carry the incident id only");
            return null;
        }
    }

    /// <summary>The exact bytes a channel type would send, for the editor to show before anything is saved.</summary>
    public string BuildSamplePayloadJson(NotificationChannelType channelType)
    {
        var incidentId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
        const string title = "Payment API is returning 5xx";
        const string severity = "Critical";

        var message = BuildHumanMessage(
            NotificationChannelDispatchEvent.IncidentCreated, severity, title, incidentId,
            $"https://callu.example.com/incidents/{incidentId}");

        object payload = channelType switch
        {
            NotificationChannelType.Slack => (object)BuildSlackPayload([], message),
            NotificationChannelType.MicrosoftTeams => BuildTeamsPayload(message),
            NotificationChannelType.Webhook => BuildWebhookPayload(
                EventKey(NotificationChannelDispatchEvent.IncidentCreated), incidentId, title, severity,
                Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa7"), message,
                new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc)),
            _ => new Dictionary<string, object> { ["body"] = message },
        };

        return JsonSerializer.Serialize(payload, SampleJsonOpts);
    }

    /// <summary>The text every channel sends, in one place so the preview cannot drift from the delivery.</summary>
    // The id stays even when a link is available: a misconfigured base URL must not leave the
    // operator without any way to identify the incident.
    internal static string BuildHumanMessage(
        NotificationChannelDispatchEvent ev,
        string severity,
        string title,
        Guid incidentId,
        string? incidentUrl = null)
    {
        var headline = ev switch
        {
            NotificationChannelDispatchEvent.IncidentCreated => $"🚨 [{severity}] Incident: {title}",
            NotificationChannelDispatchEvent.IncidentAcknowledged => $"✅ Acknowledged: [{severity}] {title}",
            NotificationChannelDispatchEvent.IncidentResolved => $"✅ Resolved: [{severity}] {title}",
            NotificationChannelDispatchEvent.IncidentClosed => $"🔒 Closed: [{severity}] {title}",
            NotificationChannelDispatchEvent.IncidentReopened => $"🔁 Reopened: [{severity}] {title}",
            _ => $"[{severity}] {title}",
        };

        return string.IsNullOrWhiteSpace(incidentUrl)
            ? $"{headline}\nID: {incidentId}"
            : $"{headline}\nID: {incidentId}\n{incidentUrl}";
    }

    // channel/username/iconEmoji are no longer offered in the editor: Slack ignores them for webhooks
    // created through an app. They are still sent when a stored config carries them, because a legacy
    // custom-integration webhook does honour them and dropping the fields would move its messages.
    internal static Dictionary<string, object> BuildSlackPayload(Dictionary<string, string> config, string message)
    {
        var payload = new Dictionary<string, object> { ["text"] = message };
        if (config.TryGetValue("channel", out var ch) && !string.IsNullOrWhiteSpace(ch))
            payload["channel"] = ch;
        if (config.TryGetValue("username", out var usr) && !string.IsNullOrWhiteSpace(usr))
            payload["username"] = usr;
        if (config.TryGetValue("iconEmoji", out var ico) && !string.IsNullOrWhiteSpace(ico))
            payload["icon_emoji"] = ico;
        return payload;
    }

    internal static Dictionary<string, object> BuildTeamsPayload(string message) => new()
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

    internal static Dictionary<string, object?> BuildWebhookPayload(
        string eventKey,
        Guid? incidentId,
        string? title,
        string? severity,
        Guid? serviceId,
        string message,
        DateTime timestamp) => new()
    {
        ["source"] = "CalluApp",
        ["timestamp"] = timestamp,
        ["event"] = eventKey,
        ["incidentId"] = incidentId,
        ["title"] = title,
        ["severity"] = severity,
        ["serviceId"] = serviceId,
        ["message"] = message,
    };

    /// <summary>Builds the incident link, or null when no usable base URL is configured.</summary>
    internal static string? BuildIncidentUrl(string? baseUrl, Guid incidentId) =>
        Uri.TryCreate(baseUrl?.TrimEnd('/'), UriKind.Absolute, out var parsed)
        && parsed.Scheme is "http" or "https"
        && !string.IsNullOrWhiteSpace(parsed.Host)
            ? $"{parsed.GetLeftPart(UriPartial.Path).TrimEnd('/')}/incidents/{incidentId}"
            : null;

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
        if (!UrlSanitizer.IsValidHealthCheckUrl(webhookUrl, AllowPrivateWebhook))
            return SendResult.Permanent(null, $"Blocked outbound URL: {UrlSanitizer.GetBlockedReason(webhookUrl)}");

        var payload = BuildSlackPayload(config, message);

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

    /// <summary>Sends to a Microsoft Teams incoming webhook as a MessageCard.</summary>
    // MessageCard rather than Adaptive Card for compatibility; requires a webhookUrl in the channel config.
    private async Task<SendResult> SendTeamsAsync(Dictionary<string, string> config, string message, CancellationToken ct)
    {
        if (!config.TryGetValue("webhookUrl", out var webhookUrl) || string.IsNullOrWhiteSpace(webhookUrl))
            return SendResult.Permanent(null, "Missing 'webhookUrl' in channel configuration");
        if (!UrlSanitizer.IsValidHealthCheckUrl(webhookUrl, AllowPrivateWebhook))
            return SendResult.Permanent(null, $"Blocked outbound URL: {UrlSanitizer.GetBlockedReason(webhookUrl)}");

        var payload = BuildTeamsPayload(message);

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
        if (!UrlSanitizer.IsValidHealthCheckUrl(url, AllowPrivateWebhook))
            return SendResult.Permanent(null, $"Blocked outbound URL: {UrlSanitizer.GetBlockedReason(url)}");

        var payload = BuildWebhookPayload(
            envelope.EventKey, envelope.IncidentId, envelope.Title, envelope.Severity,
            envelope.ServiceId, envelope.MessageText, DateTime.UtcNow);

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

    private NotificationChannelDto MapToDto(NotificationChannel c)
    {
        return new NotificationChannelDto
        {
            Id = c.Id,
            Name = c.Name,
            ChannelType = c.ChannelType.ToString(),
            Configuration = MaskSecrets(c.ChannelType, DeserializeConfig(c.ConfigurationJson)),
            IsEnabled = c.IsEnabled,
            MinimumSeverity = c.MinimumSeverity,
            ServiceFilter = DeserializeServiceFilter(c.ServiceFilterJson),
            NotifyOnIncidentCreated = c.NotifyOnIncidentCreated,
            NotifyOnIncidentAcknowledged = c.NotifyOnIncidentAcknowledged,
            NotifyOnIncidentResolved = c.NotifyOnIncidentResolved,
            NotifyOnIncidentClosed = c.NotifyOnIncidentClosed,
            NotifyOnIncidentReopened = c.NotifyOnIncidentReopened,
            LastNotifiedAt = c.LastNotifiedAt,
            NotificationCount = c.NotificationCount,
            CreatedAt = c.CreatedAt,
        };
    }
}
