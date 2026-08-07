using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Plugins;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Callu.Infrastructure.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Callu.Infrastructure.Plugins;

/// <summary>
/// Dispatches incident ACK events to external systems, persisting every attempt as a
/// <see cref="WebhookDelivery"/> row.
/// </summary>
public class IncidentEventDispatcher(
    IIncidentRepository incidents,
    IRepository<WebhookDelivery> deliveries,
    IUnitOfWork unitOfWork,
    ApplicationDbContext dbContext,
    ILogger<IncidentEventDispatcher> logger,
    IHttpClientFactory httpClientFactory,
    IOptions<Configuration.CommunicationSettingsOptions> communicationSettings) : IIncidentEventDispatcher
{
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6)
    ];
    internal const int MaxAttempts = 6;

    /// <summary>Must match WebhookDelivery.Error's [StringLength]; a longer value fails the INSERT.</summary>
    internal const int MaxErrorLength = 1000;

    /// <summary>Backoff for the next attempt after <paramref name="attemptCount"/> attempts, clamped to the ladder's last rung.</summary>
    internal static TimeSpan BackoffFor(int attemptCount) =>
        RetryBackoff[Math.Clamp(attemptCount - 1, 0, RetryBackoff.Length - 1)];

    /// <summary>Clamp any error text to what <see cref="WebhookDelivery.Error"/> can actually store.</summary>
    internal static string? ClampError(string? error) =>
        error is { Length: > MaxErrorLength } ? error[..MaxErrorLength] : error;

    public async Task<AckDispatchOutcome> SendServiceAckAsync(Guid incidentId, string ackType, CancellationToken cancellationToken = default)
    {
        try
        {
            var incident = await incidents.GetWithServiceAsync(incidentId, cancellationToken);

            if (incident?.Service == null)
            {
                logger.LogDebug("Incident {IncidentId} has no service, skipping ACK", incidentId);
                return AckDispatchOutcome.Skipped;
            }

            var service = incident.Service;

            if (!service.AckEnabled)
            {
                logger.LogDebug("Service {ServiceId} has ACK disabled, skipping", service.Id);
                return AckDispatchOutcome.Skipped;
            }

            if (string.IsNullOrEmpty(service.AckUrl))
            {
                logger.LogWarning("Service {ServiceId} has ACK enabled but no URL configured", service.Id);
                return AckDispatchOutcome.Skipped;
            }

            if (string.IsNullOrEmpty(service.AckPayloadTemplate))
            {
                logger.LogWarning("Service {ServiceId} has ACK enabled but no payload template configured", service.Id);
                return AckDispatchOutcome.Skipped;
            }

            var templateData = new Dictionary<string, object>
            {
                ["incident"] = new Dictionary<string, object?>
                {
                    ["id"] = incident.Id.ToString(),
                    ["title"] = incident.Title,
                    ["description"] = incident.Description,
                    ["severity"] = incident.Severity.ToString(),
                    ["status"] = incident.Status.ToString(),
                    ["external_id"] = incident.ExternalAlertId,
                    ["started_at"] = incident.CreatedAt.ToString("o"),
                    ["resolved_at"] = incident.ResolvedAt?.ToString("o"),
                },
                ["ack_type"] = ackType,
                ["service"] = new Dictionary<string, object?>
                {
                    ["id"] = service.Id.ToString(),
                    ["name"] = service.Name,
                },
            };

            var scribanTemplate = Scriban.Template.Parse(service.AckPayloadTemplate);
            if (scribanTemplate.HasErrors)
            {
                logger.LogError("ACK template for service {ServiceId} has errors: {Errors}",
                    service.Id, string.Join("; ", scribanTemplate.Messages.Select(m => m.Message)));
                return AckDispatchOutcome.Skipped;
            }

            var renderedPayload = await scribanTemplate.RenderAsync(templateData);

            // allowPrivate is honoured here too, so an internal ACK URL the operator opted into is
            // not refused by this validator after the HttpClient already accepted it.
            var allowPrivate = communicationSettings.Value.AllowPrivateWebhookEndpoint;
            var urlCheck = CheckAckUrl(service.AckUrl, allowPrivate, out var urlRejection);
            if (urlCheck == AckUrlCheck.Rejected)
            {
                logger.LogWarning(
                    "Service {ServiceId} ACK URL rejected by SSRF guard: {Reason}",
                    service.Id, urlRejection);
                // A configuration rejection is a Failed delivery, not silence: the operator
                // looks at the incident's delivery panel, not at container logs.
                return await RecordAttemptAsync(
                    incidentId, service.Id, service.AckUrl, ackType, renderedPayload,
                    null, null, urlRejection, retryable: false, cancellationToken);
            }

            if (urlCheck == AckUrlCheck.Unresolvable)
            {
                logger.LogWarning(
                    "Service {ServiceId} ACK URL could not be resolved: {Reason}",
                    service.Id, urlRejection);
                return await RecordAttemptAsync(
                    incidentId, service.Id, service.AckUrl, ackType, renderedPayload,
                    null, null, urlRejection, retryable: true, cancellationToken);
            }

            logger.LogInformation("Sending ACK ({AckType}) for incident {IncidentId} to {Url}. Payload: {Payload}",
                ackType, incidentId, service.AckUrl, renderedPayload[..Math.Min(200, renderedPayload.Length)]);

            var httpClient = httpClientFactory.CreateClient("WebhookDispatch");
            var method = new HttpMethod(service.AckHttpMethod ?? "POST");
            using var request = new HttpRequestMessage(method, service.AckUrl)
            {
                Content = new StringContent(renderedPayload, System.Text.Encoding.UTF8, service.AckContentType ?? "application/json")
            };

            // Stable across every retry and distinct per (incident, ack type), so a receiver can
            // tell a retry from a new event when the body is byte-identical.
            request.Headers.TryAddWithoutValidation(DeliveryKeyHeader, $"{incidentId:D}:{ackType}");

            if (!string.IsNullOrEmpty(service.AckHeaders))
            {
                try
                {
                    var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(service.AckHeaders);
                    if (headers != null)
                    {
                        var applied = 0;
                        foreach (var (key, value) in headers)
                        {
                            if (applied >= 20) break;
                            if (string.IsNullOrWhiteSpace(key) || value is null || value.Length > 2048) continue;
                            if (key.ToLowerInvariant() is "host" or "content-length" or "transfer-encoding"
                                or "connection" or "proxy-authorization" or "proxy-connection") continue;
                            if (key.Equals(DeliveryKeyHeader, StringComparison.OrdinalIgnoreCase)) continue;
                            request.Headers.TryAddWithoutValidation(key, value);
                            applied++;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Failed to parse ACK headers for service {ServiceId}", service.Id);
                }
            }

            if (!string.IsNullOrEmpty(service.WebhookSecret))
            {
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(service.WebhookSecret));
                var signature = "sha256=" + Convert.ToHexString(
                    hmac.ComputeHash(Encoding.UTF8.GetBytes(renderedPayload))).ToLowerInvariant();
                var sigHeader = string.IsNullOrWhiteSpace(service.WebhookSignatureHeader)
                    ? "X-Callu-Signature"
                    : service.WebhookSignatureHeader;
                request.Headers.TryAddWithoutValidation(sigHeader, signature);
            }

            int? httpStatus = null;
            string? responseSample = null;
            string? errorMessage = null;
            bool retryable = false;

            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken);
                httpStatus = (int)response.StatusCode;
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                responseSample = body.Length > 1024 ? body[..1024] : body;

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("ACK sent successfully for incident {IncidentId}", incidentId);
                }
                else
                {
                    errorMessage = $"HTTP {httpStatus}";
                    retryable = httpStatus is >= 500 and < 600;
                    logger.LogError("Failed to send ACK for incident {IncidentId}. Status: {Status}, Error: {Error}",
                        incidentId, response.StatusCode, body);
                }
            }
            catch (HttpRequestException ex)
            {
                errorMessage = ex.Message;
                retryable = true;
                logger.LogError(ex, "Error sending ACK for incident {IncidentId}", incidentId);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                errorMessage = "Connect/read timeout";
                retryable = true;
                logger.LogError(ex, "ACK timeout for incident {IncidentId}", incidentId);
            }

            return await RecordAttemptAsync(
                incidentId, service.Id, service.AckUrl, ackType, renderedPayload,
                httpStatus, responseSample, errorMessage, retryable, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending ACK for incident {IncidentId}", incidentId);
            return AckDispatchOutcome.NotRecorded;
        }
    }

    /// <summary>Persists one <see cref="WebhookDelivery"/> row, whose status decides whether the retry
    /// job's partial index will pick it up again.</summary>
    private async Task<AckDispatchOutcome> RecordAttemptAsync(
        Guid incidentId, Guid serviceId, string url, string ackType, string requestBody,
        int? httpStatus, string? responseSample, string? error, bool retryable,
        CancellationToken cancellationToken)
    {
        WebhookDelivery? row = null;
        try
        {
            // Attempt ordinal for this chain. The row count alone would forget an attempt whose row was
            // never persisted and hand its budget back, so the highest ordinal on record is the floor.
            var chain = deliveries.GetQueryable()
                .Where(d => d.IncidentId == incidentId && d.AckType == ackType);

            var rowCount = await chain.CountAsync(cancellationToken);
            var highestOrdinal = await chain.MaxAsync(d => (int?)d.AttemptCount, cancellationToken) ?? 0;
            var attemptCount = Math.Max(highestOrdinal, rowCount + 1);

            var succeeded = error is null;
            WebhookDeliveryStatus status;
            DateTime? nextRetryAt = null;
            if (succeeded)
            {
                status = WebhookDeliveryStatus.Succeeded;
            }
            else if (retryable && attemptCount < MaxAttempts)
            {
                status = WebhookDeliveryStatus.Retrying;
                nextRetryAt = DateTime.UtcNow + BackoffFor(attemptCount);
            }
            else
            {
                status = WebhookDeliveryStatus.Failed;
            }

            row = new WebhookDelivery
            {
                Id = Guid.NewGuid(),
                IncidentId = incidentId,
                ServiceId = serviceId,
                Direction = "Outbound",
                Url = url[..Math.Min(500, url.Length)],
                AckType = ackType,
                HttpStatus = httpStatus,
                RequestBodySample = requestBody.Length > 1024 ? requestBody[..1024] : requestBody,
                ResponseBodySample = responseSample,
                Error = ClampError(error),
                AttemptCount = attemptCount,
                AttemptedAt = DateTime.UtcNow,
                NextRetryAt = nextRetryAt,
                Status = status,
                CreatedAt = DateTime.UtcNow
            };

            await deliveries.AddAsync(row, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return AckDispatchOutcome.Recorded;
        }
        catch (Exception ex)
        {
            if (row is not null)
                dbContext.Entry(row).State = EntityState.Detached;

            logger.LogWarning(ex, "Failed to persist WebhookDelivery row for incident {IncidentId}", incidentId);
            return AckDispatchOutcome.NotRecorded;
        }
    }

    private enum AckUrlCheck
    {
        /// <summary>Safe to send to.</summary>
        Ok,

        /// <summary>Configuration is wrong (bad scheme, private/loopback target). Retrying changes nothing.</summary>
        Rejected,

        /// <summary>DNS did not answer. Possibly a transient resolver outage, so this is treated as a retryable send failure.</summary>
        Unresolvable
    }

    /// <summary>Idempotency key for the receiver; reserved, so operator headers cannot shadow it.</summary>
    private const string DeliveryKeyHeader = "X-Callu-Delivery-Key";

    private static AckUrlCheck CheckAckUrl(string url, bool allowPrivate, out string rejectionReason)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            rejectionReason = "Not a valid absolute URL.";
            return AckUrlCheck.Rejected;
        }
        if (uri.Scheme is not ("http" or "https"))
        {
            rejectionReason = $"Disallowed scheme '{uri.Scheme}'.";
            return AckUrlCheck.Rejected;
        }

        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(uri.Host);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            rejectionReason = $"Host '{uri.Host}' could not be resolved.";
            return AckUrlCheck.Unresolvable;
        }

        foreach (var addr in addresses)
        {
            // One allowlist for every outbound target, so the opt-out means the same
            // thing here as in the transport pin (and metadata IPs stay blocked in
            // both modes).
            if (!UrlSanitizer.IsAllowedTargetIp(addr, allowPrivate))
            {
                rejectionReason = $"Host '{uri.Host}' resolves to disallowed address {addr}.";
                return AckUrlCheck.Rejected;
            }
        }

        rejectionReason = string.Empty;
        return AckUrlCheck.Ok;
    }
}
