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
/// Tokens resolve against Service first, then Integration (same URL shape).
/// </summary>
public class WebhookProcessingService(
    IWebhookCaptureRepository captureRepo,
    IServiceRepository serviceRepository,
    IIntegrationRepository integrationRepository,
    IIncidentRepository incidentRepo,
    ITransactionManager transactionManager,
    IIncidentService incidentService,
    IWebhookPayloadParser payloadParser,
    IWebhookSignatureVerifier signatureVerifier,
    CalluMetrics metrics,
    ILogger<WebhookProcessingService> logger) : IWebhookProcessingService
{
    /// <summary>Who an inbound webhook acts as, in the same shape as the other system actors.</summary>
    private const string WebhookActor = "system:webhook";

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "cookie", "set-cookie", "x-callu-api-key", "x-api-key", "apikey", "api-key",
        "x-webhook-secret", "x-hub-signature", "x-hub-signature-256",
        "x-slack-signature", "x-pagerduty-signature", "x-signature",
    };

    private const int MaxCapturedBodyChars = 64 * 1024;

    /// <summary>How many over-cap capture rows one request may delete; a larger backlog drains across later requests.</summary>
    private const int MaxCapTrimRowsPerRequest = 2000;

    /// <summary>Window for the title-based duplicate check on incoming open-webhooks.</summary>
    // A constant rather than per-service config: this is only flood mitigation, not the fingerprint-based grouping engine.
    private const int FuzzyDedupeWindowMinutes = 5;

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
        body.Length <= MaxCapturedBodyChars ? body : body[..MaxCapturedBodyChars] + WebhookCapture.TruncationSuffix;

    /// <summary>
    /// Bounds one ingested field to its column. No ellipsis (the value is a dedupe key) and the cut
    /// never splits a surrogate pair.
    /// </summary>
    internal static string? Clip(string? value, int max)
    {
        if (value is null || value.Length <= max)
            return value;

        var cut = max;
        if (char.IsHighSurrogate(value[cut - 1]))
            cut--;

        return value[..cut];
    }

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
                if (service is not null)
                    return await ProcessForServiceAsync(service, apiKey, method, contentType, body, headers, sourceIp, cancellationToken);

                var integration = await integrationRepository.GetByWebhookTokenWithTemplateAsync(token, cancellationToken);
                if (integration is not null)
                    return await ProcessForIntegrationAsync(integration, apiKey, method, contentType, body, headers, sourceIp, cancellationToken);

                return Fail(Messages.Get("webhooks.invalidToken"));
            }, cancellationToken);
        }
        finally
        {
            metrics.RecordWebhookDuration(sw.Elapsed.TotalMilliseconds);
        }
    }

    private async Task<WebhookProcessResult> ProcessForServiceAsync(
        Service service,
        string? apiKey,
        string method,
        string? contentType,
        string body,
        IDictionary<string, string> headers,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        if (!service.WebhookEnabled)
            return Fail(Messages.Get("webhooks.disabled"));

        var auth = Authenticate(apiKey, service.WebhookApiKey, service.WebhookSecret, service.WebhookSignatureHeader, body, headers, service.Id);
        if (auth is not null) return auth;

        service.LastWebhookReceivedAt = DateTime.UtcNow;
        service.WebhooksReceivedCount++;

        if (service.WebhookListeningMode)
        {
            return await CaptureRequestAsync(
                service.Id, null, service.WebhookSignatureHeader,
                method, contentType, body, headers, sourceIp, cancellationToken);
        }

        return await ProcessParsedAsync(
            body,
            service.WebhookTemplate,
            serviceId: service.Id,
            teamId: service.TeamId,
            sourceIntegrationId: null,
            dataLanguage: service.WebhookTemplate?.DataLanguage,
            cancellationToken);
    }

    private async Task<WebhookProcessResult> ProcessForIntegrationAsync(
        Integration integration,
        string? apiKey,
        string method,
        string? contentType,
        string body,
        IDictionary<string, string> headers,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        if (!integration.IsActive || !integration.WebhookEnabled)
            return Fail(Messages.Get("webhooks.disabled"));

        var auth = Authenticate(apiKey, integration.ApiKey, integration.WebhookSecret, integration.WebhookSignatureHeader, body, headers, integration.Id);
        if (auth is not null) return auth;

        integration.LastWebhookReceivedAt = DateTime.UtcNow;
        integration.WebhooksReceivedCount++;

        var boundService = integration.Service is { IsDeleted: false } ? integration.Service : null;

        if (boundService is null && integration.ServiceId.HasValue)
            logger.LogWarning(
                "Integration {IntegrationId} points at service {ServiceId}, which no longer exists; its "
                + "alerts are being captured instead of creating incidents until it is re-bound",
                integration.Id, integration.ServiceId);

        // An alarm with nowhere to go is stored, never dropped: an unbound endpoint captures even
        // with listening off, because the sender will not retry a rejection.
        if (integration.ListeningMode || boundService is null)
        {
            return await CaptureRequestAsync(
                boundService?.Id, integration.Id, integration.WebhookSignatureHeader,
                method, contentType, body, headers, sourceIp, cancellationToken);
        }

        var teamId = integration.TeamId ?? boundService.TeamId;

        return await ProcessParsedAsync(
            body,
            integration.WebhookTemplate,
            serviceId: boundService.Id,
            teamId: teamId,
            sourceIntegrationId: integration.Id,
            dataLanguage: integration.WebhookTemplate?.DataLanguage,
            cancellationToken);
    }

    private async Task<WebhookProcessResult> CaptureRequestAsync(
        Guid? serviceId,
        Guid? integrationId,
        string? signatureHeader,
        string method,
        string? contentType,
        string body,
        IDictionary<string, string> headers,
        string? sourceIp,
        CancellationToken cancellationToken)
    {
        var capture = new WebhookCapture
        {
            ServiceId = serviceId,
            IntegrationId = integrationId,
            CapturedAt = DateTime.UtcNow,
            Method = method,
            ContentType = contentType,
            SourceIp = sourceIp,
            Headers = System.Text.Json.JsonSerializer.Serialize(
                RedactSensitiveHeaders(headers, signatureHeader)),
            Body = TrimForCapture(body),
            Status = WebhookCaptureStatus.Captured
        };

        await captureRepo.TrimScopeForPendingInsertAsync(
            serviceId, integrationId, WebhookCapture.MaxPerScope, MaxCapTrimRowsPerRequest, cancellationToken);
        await captureRepo.AddAsync(capture, cancellationToken);

        return new WebhookProcessResult
        {
            Success = true,
            Message = Messages.Get("webhooks.captured"),
            CaptureId = capture.Id,
            WasCaptured = true
        };
    }

    private WebhookProcessResult? Authenticate(
        string? presentedApiKey,
        string? configuredApiKey,
        string? webhookSecret,
        string? signatureHeader,
        string body,
        IDictionary<string, string> headers,
        Guid logScopeId)
    {
        if (string.IsNullOrEmpty(configuredApiKey))
            return Fail(Messages.Get("webhooks.apiKeyNotConfigured"));

        if (string.IsNullOrEmpty(presentedApiKey))
            return Fail(Messages.Get("webhooks.apiKeyRequired"));

        var configuredBytes = Encoding.UTF8.GetBytes(configuredApiKey);
        var presentedBytes = Encoding.UTF8.GetBytes(presentedApiKey);
        if (configuredBytes.Length != presentedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(configuredBytes, presentedBytes))
        {
            return Fail(Messages.Get("webhooks.invalidApiKey"));
        }

        if (!string.IsNullOrEmpty(webhookSecret) && !string.IsNullOrEmpty(signatureHeader))
        {
            if (!signatureVerifier.Verify(body, webhookSecret, headers, signatureHeader))
            {
                logger.LogWarning("Invalid webhook signature for scope {ScopeId}, expected header: {Header}",
                    logScopeId, signatureHeader);
                return Fail(Messages.Get("webhooks.invalidSignature"));
            }
        }

        return null;
    }

    private async Task<WebhookProcessResult> ProcessParsedAsync(
        string body,
        WebhookTemplate? template,
        Guid serviceId,
        Guid? teamId,
        Guid? sourceIntegrationId,
        string? dataLanguage,
        CancellationToken cancellationToken)
    {
        if (template is null)
        {
            return new WebhookProcessResult
            {
                Success = true,
                Message = Messages.Get("webhooks.noTemplate")
            };
        }

        var parsed = payloadParser.Parse(body, template);
        if (!parsed.Success)
        {
            return Fail($"Template parsing failed: {parsed.Error}");
        }

        // Clip, do not reject — a 4xx here loses the alert outright. Clipped BEFORE the dedupe
        // reads below, which compare against stored (clipped) values.
        parsed.Title = Clip(parsed.Title, Incident.MaxTitleLength);
        parsed.ExternalId = Clip(parsed.ExternalId, Incident.MaxExternalAlertIdLength);
        parsed.Description = Clip(parsed.Description, Incident.MaxDescriptionLength);

        if (parsed.State == WebhookState.Open)
        {
            if (!string.IsNullOrEmpty(parsed.ExternalId))
            {
                var existingIncident = await incidentRepo.GetQueryable()
                    .AnyAsync(i => i.ServiceId == serviceId &&
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

            // Fuzzy title dedupe runs regardless of whether an external id was present: many
            // monitors mint a fresh event id per firing, so id equality alone lets a flapping
            // alarm open (and page!) a new incident every time.
            if (!string.IsNullOrEmpty(parsed.Title))
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-FuzzyDedupeWindowMinutes);
                var fuzzyDuplicate = await incidentRepo.GetQueryable()
                    .Where(i => i.ServiceId == serviceId &&
                                i.Title == parsed.Title &&
                                i.CreatedAt >= cutoff &&
                                i.Status != IncidentStatus.Resolved &&
                                i.Status != IncidentStatus.Closed &&
                                !i.IsDeleted)
                    // Several incidents can match the window; the oldest is the one the others are
                    // duplicates of, and picking it has to be repeatable across deliveries.
                    .OrderBy(i => i.StartedAt)
                    .ThenBy(i => i.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (fuzzyDuplicate is not null)
                {
                    // Adopt the NEWEST external id: a monitor that mints a fresh id per firing sends its eventual
                    // "resolved" webhook with the latest one, so keeping the stale id would break auto-resolve.
                    // Reaching here means no live incident on this service holds the id: the check
                    // above returns "duplicate" if one does. The write joins the ingest transaction.
                    if (!string.IsNullOrEmpty(parsed.ExternalId) &&
                        fuzzyDuplicate.ExternalAlertId != parsed.ExternalId)
                    {
                        fuzzyDuplicate.ExternalAlertId = parsed.ExternalId;
                        fuzzyDuplicate.UpdatedAt = DateTime.UtcNow;
                        incidentRepo.Update(fuzzyDuplicate);
                    }

                    logger.LogInformation(
                        "Webhook fuzzy-deduped for service {ServiceId} against incident {IncidentId} (title match within {Window} min)",
                        serviceId, fuzzyDuplicate.Id, FuzzyDedupeWindowMinutes);
                    return new WebhookProcessResult
                    {
                        Success = true,
                        Message = Messages.Get("webhooks.duplicateIncident")
                    };
                }
            }

            var createRequest = new CreateIncidentRequest
            {
                Title = parsed.Title ?? "Untitled Incident",
                Description = parsed.Description,
                Severity = parsed.Severity.ToString(),
                ServiceId = serviceId,
                TeamId = teamId,
                SourceIntegrationId = sourceIntegrationId,
                ExternalAlertId = parsed.ExternalId,
                DataLanguage = dataLanguage
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

        if (parsed.State == WebhookState.Resolved)
        {
            if (!string.IsNullOrEmpty(parsed.ExternalId))
            {
                // Creation guards the duplicate with a read-then-write, so two active
                // incidents can share an external id; resolve the oldest rather than
                // whichever row Postgres happens to hand back.
                var incidentToResolve = await incidentRepo.GetQueryable()
                    .Where(i => i.ServiceId == serviceId &&
                                i.ExternalAlertId == parsed.ExternalId &&
                                i.Status != IncidentStatus.Resolved &&
                                i.Status != IncidentStatus.Closed &&
                                !i.IsDeleted)
                    .OrderBy(i => i.StartedAt).ThenBy(i => i.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (incidentToResolve != null)
                {
                    await incidentService.ResolveIncidentAsync(incidentToResolve.Id, WebhookActor, cancellationToken);

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

        return new WebhookProcessResult
        {
            Success = true,
            Message = Messages.Get("webhooks.noTemplate")
        };
    }

    private static WebhookProcessResult Fail(string message) => new()
    {
        Success = false,
        Message = message
    };
}
