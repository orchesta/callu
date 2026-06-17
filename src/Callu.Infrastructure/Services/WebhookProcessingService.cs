using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Services.Models;
using Callu.Shared.Localization;
using Callu.Shared.Models.Incidents;
using Callu.Shared.Models.Webhooks;
using Callu.Infrastructure.Telemetry;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Handles incoming webhook processing — ingestion, template parsing, incident creation/resolution.
/// </summary>
public class WebhookProcessingService(
    IWebhookCaptureRepository captureRepo,
    IServiceRepository serviceRepository,
    IIncidentRepository incidentRepo,
    ITransactionManager transactionManager,
    IIncidentService incidentService,
    IWebhookPayloadParser payloadParser,
    IWebhookSignatureVerifier signatureVerifier,
    CalluMetrics metrics,
    ILogger<WebhookProcessingService> logger) : IWebhookProcessingService
{
    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "cookie", "set-cookie", "x-api-key", "apikey", "api-key",
        "x-webhook-secret", "x-hub-signature", "x-hub-signature-256",
        "x-slack-signature", "x-pagerduty-signature", "x-signature",
    };

    private const int MaxCapturedBodyChars = 64 * 1024;

    /// <summary>Returns a copy of the headers with secret/identity values replaced by a sentinel.</summary>
    internal static Dictionary<string, string> RedactSensitiveHeaders(
        IDictionary<string, string> headers, string? signatureHeader)
    {
        const string redacted = "***redacted***";
        var result = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers)
        {
            var sensitive = SensitiveHeaderNames.Contains(name)
                || (!string.IsNullOrEmpty(signatureHeader)
                    && name.Equals(signatureHeader, StringComparison.OrdinalIgnoreCase));
            result[name] = sensitive ? redacted : value;
        }
        return result;
    }

    /// <summary>Bounds the captured body size so a huge payload can't bloat the captures table.</summary>
    internal static string TrimForCapture(string body) =>
        body.Length <= MaxCapturedBodyChars ? body : body[..MaxCapturedBodyChars] + "\n...[truncated]";

    public async Task<WebhookProcessResult> ProcessWebhookAsync(
        string token,
        string? apiKey,
        string method,
        string? contentType,
        string body,
        IDictionary<string, string> headers,
        string? sourceIp,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var service = await serviceRepository.GetByWebhookTokenWithTemplateAsync(token, cancellationToken);

            if (service == null)
            {
                return new WebhookProcessResult
                {
                    Success = false,
                    Message = Messages.Get("webhooks.invalidToken")
                };
            }

            if (!service.WebhookEnabled)
            {
                return new WebhookProcessResult
                {
                    Success = false,
                    Message = Messages.Get("webhooks.disabled")
                };
            }

            if (string.IsNullOrEmpty(service.WebhookApiKey))
            {
                return new WebhookProcessResult
                {
                    Success = false,
                    Message = Messages.Get("webhooks.apiKeyNotConfigured")
                };
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                return new WebhookProcessResult
                {
                    Success = false,
                    Message = Messages.Get("webhooks.apiKeyRequired")
                };
            }

            var configuredBytes = Encoding.UTF8.GetBytes(service.WebhookApiKey);
            var presentedBytes = Encoding.UTF8.GetBytes(apiKey);
            if (configuredBytes.Length != presentedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(configuredBytes, presentedBytes))
            {
                return new WebhookProcessResult
                {
                    Success = false,
                    Message = Messages.Get("webhooks.invalidApiKey")
                };
            }

            if (!string.IsNullOrEmpty(service.WebhookSecret) && !string.IsNullOrEmpty(service.WebhookSignatureHeader))
            {
                if (!signatureVerifier.Verify(body, service.WebhookSecret, headers, service.WebhookSignatureHeader))
                {
                    logger.LogWarning("Invalid webhook signature for service {ServiceId}, expected header: {Header}",
                        service.Id, service.WebhookSignatureHeader);
                    return new WebhookProcessResult
                    {
                        Success = false,
                        Message = Messages.Get("webhooks.invalidSignature")
                    };
                }
            }

            service.LastWebhookReceivedAt = DateTime.UtcNow;
            service.WebhooksReceivedCount++;

            if (service.WebhookListeningMode)
            {
                var capture = new WebhookCapture
                {
                    ServiceId = service.Id,
                    CapturedAt = DateTime.UtcNow,
                    Method = method,
                    ContentType = contentType,
                    SourceIp = sourceIp,
                    Headers = System.Text.Json.JsonSerializer.Serialize(
                        RedactSensitiveHeaders(headers, service.WebhookSignatureHeader)),
                    Body = TrimForCapture(body),
                    Status = WebhookCaptureStatus.Captured
                };

                await captureRepo.AddAsync(capture, cancellationToken);

                return new WebhookProcessResult
                {
                    Success = true,
                    Message = Messages.Get("webhooks.captured"),
                    CaptureId = capture.Id,
                    WasCaptured = true
                };
            }

            if (service.WebhookTemplate != null)
            {
                var parsed = payloadParser.Parse(body, service.WebhookTemplate);
                if (!parsed.Success)
                {
                    return new WebhookProcessResult
                    {
                        Success = false,
                        Message = $"Template parsing failed: {parsed.Error}"
                    };
                }

                if (parsed.State == WebhookState.Open)
                {
                    if (!string.IsNullOrEmpty(parsed.ExternalId))
                    {
                        var existingIncident = await incidentRepo.GetQueryable()
                            .AnyAsync(i => i.ServiceId == service.Id &&
                                           i.ExternalAlertId == parsed.ExternalId &&
                                           i.Status != IncidentStatus.Resolved &&
                                           i.Status != IncidentStatus.Closed &&
                                           !i.IsDeleted, cancellationToken);

                        if (existingIncident)
                        {
                            return new WebhookProcessResult
                            {
                                Success = true,
                                Message = Messages.Get("webhooks.duplicateIncident")
                            };
                        }
                    }
                    else
                    {
                        var cutoff = DateTime.UtcNow.AddMinutes(-5);
                        var fuzzyDuplicate = await incidentRepo.GetQueryable()
                            .AnyAsync(i => i.ServiceId == service.Id &&
                                           i.Title == parsed.Title &&
                                           i.CreatedAt >= cutoff &&
                                           i.Status != IncidentStatus.Resolved &&
                                           i.Status != IncidentStatus.Closed &&
                                           !i.IsDeleted, cancellationToken);

                        if (fuzzyDuplicate)
                        {
                            return new WebhookProcessResult
                            {
                                Success = true,
                                Message = Messages.Get("webhooks.duplicateIncident")
                            };
                        }
                    }

                    var createRequest = new Callu.Shared.Models.Incidents.CreateIncidentRequest
                    {
                        Title = parsed.Title ?? "Untitled Incident",
                        Description = parsed.Description,
                        Severity = parsed.Severity.ToString(),
                        ServiceId = service.Id,
                        TeamId = service.TeamId,
                        ExternalAlertId = parsed.ExternalId,
                        DataLanguage = service.WebhookTemplate.DataLanguage
                    };

                    var created = await incidentService.CreateIncidentAsync(createRequest, cancellationToken);

                    if (created.Outcome == IncidentCreateOutcome.Suppressed)
                    {
                        return new WebhookProcessResult
                        {
                            Success = true,
                            Message = $"Incident suppressed by maintenance window: {created.Reason}",
                            IncidentId = null
                        };
                    }

                    return new WebhookProcessResult
                    {
                        Success = true,
                        Message = $"Incident created: {created.Incident!.Id}",
                        IncidentId = created.Incident.Id
                    };
                }
                else if (parsed.State == WebhookState.Resolved)
                {
                    if (!string.IsNullOrEmpty(parsed.ExternalId))
                    {
                        var incidentToResolve = await incidentRepo.GetQueryable()
                            .FirstOrDefaultAsync(i => i.ServiceId == service.Id &&
                                                    i.ExternalAlertId == parsed.ExternalId &&
                                                    i.Status != IncidentStatus.Resolved &&
                                                    i.Status != IncidentStatus.Closed &&
                                                    !i.IsDeleted, cancellationToken);
                        
                        if (incidentToResolve != null)
                        {
                            await incidentService.ResolveIncidentAsync(incidentToResolve.Id, "system-webhook", cancellationToken);
                            
                            return new WebhookProcessResult
                            {
                                Success = true,
                                Message = $"Incident {incidentToResolve.Id} resolved via webhook",
                                IncidentId = incidentToResolve.Id
                            };
                        }
                    }

                    return new WebhookProcessResult
                    {
                        Success = true,
                        Message = Messages.Get("webhooks.noMatchingIncident")
                    };
                }
            }

            return new WebhookProcessResult
            {
                Success = true,
                Message = Messages.Get("webhooks.noTemplate")
            };
        }, cancellationToken);
        }
        finally
        {
            metrics.RecordWebhookDuration(sw.Elapsed.TotalMilliseconds);
        }
    }
}
