using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Plugins;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Callu.Infrastructure.Plugins;

/// <summary>
/// Dispatches incident ACK events to external systems using inline Service configuration.
/// Every attempt is persisted as a <see cref="WebhookDelivery"/> row so retries and
/// the UI delivery panel have an authoritative source. Fix 10.P1-7.
/// </summary>
public class IncidentEventDispatcher(
    IIncidentRepository incidents,
    IRepository<WebhookDelivery> deliveries,
    IUnitOfWork unitOfWork,
    ILogger<IncidentEventDispatcher> logger,
    IHttpClientFactory httpClientFactory) : IIncidentEventDispatcher
{
    private static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6)
    ];
    private const int MaxAttempts = 6;

    public async Task SendServiceAckAsync(Guid incidentId, string ackType, CancellationToken cancellationToken = default)
    {
        try
        {
            var incident = await incidents.GetWithServiceAsync(incidentId, cancellationToken);

            if (incident?.Service == null)
            {
                logger.LogDebug("Incident {IncidentId} has no service, skipping ACK", incidentId);
                return;
            }

            var service = incident.Service;

            if (!service.AckEnabled)
            {
                logger.LogDebug("Service {ServiceId} has ACK disabled, skipping", service.Id);
                return;
            }

            if (string.IsNullOrEmpty(service.AckUrl))
            {
                logger.LogWarning("Service {ServiceId} has ACK enabled but no URL configured", service.Id);
                return;
            }

            if (!IsExternalHttpUrl(service.AckUrl, out var rejectionReason))
            {
                logger.LogWarning(
                    "Service {ServiceId} ACK URL rejected by SSRF guard: {Reason}",
                    service.Id, rejectionReason);
                return;
            }

            if (string.IsNullOrEmpty(service.AckPayloadTemplate))
            {
                logger.LogWarning("Service {ServiceId} has ACK enabled but no payload template configured", service.Id);
                return;
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
                return;
            }

            var renderedPayload = await scribanTemplate.RenderAsync(templateData);

            logger.LogInformation("Sending ACK ({AckType}) for incident {IncidentId} to {Url}. Payload: {Payload}",
                ackType, incidentId, service.AckUrl, renderedPayload[..Math.Min(200, renderedPayload.Length)]);

            var httpClient = httpClientFactory.CreateClient("WebhookDispatch");
            var method = new HttpMethod(service.AckHttpMethod ?? "POST");
            var request = new HttpRequestMessage(method, service.AckUrl)
            {
                Content = new StringContent(renderedPayload, System.Text.Encoding.UTF8, service.AckContentType ?? "application/json")
            };

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
                var response = await httpClient.SendAsync(request, cancellationToken);
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

            await RecordAttemptAsync(
                incidentId, service.Id, service.AckUrl, ackType, renderedPayload,
                httpStatus, responseSample, errorMessage, retryable, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending ACK for incident {IncidentId}", incidentId);
        }
    }

    /// <summary>
    /// Persist one row to <see cref="WebhookDelivery"/>. Status is chosen so the
    /// retry job's partial index ("Status = 'Retrying'") picks up only the rows
    /// that are eligible for re-fire — terminal Succeeded/Failed never wake the
    /// job up.
    /// </summary>
    private async Task RecordAttemptAsync(
        Guid incidentId, Guid serviceId, string url, string ackType, string requestBody,
        int? httpStatus, string? responseSample, string? error, bool retryable,
        CancellationToken cancellationToken)
    {
        try
        {
            var attemptCount = await deliveries.GetQueryable()
                .Where(d => d.IncidentId == incidentId && d.AckType == ackType)
                .CountAsync(cancellationToken) + 1;

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
                var backoffIdx = Math.Min(attemptCount - 1, RetryBackoff.Length - 1);
                nextRetryAt = DateTime.UtcNow + RetryBackoff[backoffIdx];
            }
            else
            {
                status = WebhookDeliveryStatus.Failed;
            }

            var row = new WebhookDelivery
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
                Error = error,
                AttemptCount = attemptCount,
                AttemptedAt = DateTime.UtcNow,
                NextRetryAt = nextRetryAt,
                Status = status,
                CreatedAt = DateTime.UtcNow
            };

            await deliveries.AddAsync(row, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist WebhookDelivery row for incident {IncidentId}", incidentId);
        }
    }

    /// <summary>
    /// True only for http(s) URLs whose host resolves entirely to public,
    /// non-loopback / non-private addresses. Rejects 169.254.169.254 (AWS/GCE
    /// metadata), 127.0.0.0/8, 10/8, 172.16/12, 192.168/16, fc00::/7,
    /// fe80::/10. The DNS resolution happens once here and again inside
    /// HttpClient — a DNS-rebinding attack would have to win both lookups in
    /// the same RTT window, which the underlying HttpHandler doesn't
    /// guarantee to prevent; that's why this is "defence in depth" not the
    /// only line of defence.
    /// </summary>
    private static bool IsExternalHttpUrl(string url, out string rejectionReason)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            rejectionReason = "Not a valid absolute URL.";
            return false;
        }
        if (uri.Scheme is not ("http" or "https"))
        {
            rejectionReason = $"Disallowed scheme '{uri.Scheme}'.";
            return false;
        }

        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(uri.Host);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            rejectionReason = $"Host '{uri.Host}' could not be resolved.";
            return false;
        }

        foreach (var addr in addresses)
        {
            if (IsPrivateOrLoopback(addr))
            {
                rejectionReason = $"Host '{uri.Host}' resolves to private/loopback address {addr}.";
                return false;
            }
        }

        rejectionReason = string.Empty;
        return true;
    }

    private static bool IsPrivateOrLoopback(IPAddress addr)
    {
        if (IPAddress.IsLoopback(addr)) return true;

        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = addr.GetAddressBytes();
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;
            if (b[0] == 0) return true;
            return false;
        }

        if (addr.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (addr.IsIPv6LinkLocal) return true;
            if (addr.IsIPv6SiteLocal) return true;
            var bytes = addr.GetAddressBytes();
            if ((bytes[0] & 0xFE) == 0xFC) return true;
            if (addr.IsIPv4MappedToIPv6)
                return IsPrivateOrLoopback(addr.MapToIPv4());
            return false;
        }

        return false;
    }
}
