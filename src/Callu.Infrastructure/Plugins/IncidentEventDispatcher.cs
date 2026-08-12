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
    IOptions<Configuration.CommunicationSettingsOptions> communicationSettings,
    Telemetry.CalluMetrics? metrics = null,
    Callu.Application.Services.IAuditLogService? auditLog = null,
    IRepository<ServiceAction>? serviceActions = null) : IIncidentEventDispatcher
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

    /// <summary>Whether the service's event selection covers this ack type; a null selection means acknowledge and resolve.</summary>
    internal static bool AckEventSelected(ServiceAckEvents? events, string ackType)
    {
        if (events is null)
            return ackType is "acknowledge" or "resolve";

        var flag = ackType switch
        {
            "created" => ServiceAckEvents.Created,
            "acknowledge" => ServiceAckEvents.Acknowledged,
            "resolve" => ServiceAckEvents.Resolved,
            "closed" => ServiceAckEvents.Closed,
            "reopened" => ServiceAckEvents.Reopened,
            _ => ServiceAckEvents.None,
        };
        return flag != ServiceAckEvents.None && events.Value.HasFlag(flag);
    }

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

            if (!service.AckEnabled || !AckEventSelected(service.AckEvents, ackType))
            {
                logger.LogDebug("Service {ServiceId} has ACK disabled or event '{AckType}' unselected, skipping", service.Id, ackType);
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
                var templateErrors = string.Join("; ", scribanTemplate.Messages.Select(m => m.Message));
                logger.LogError("ACK template for service {ServiceId} has errors: {Errors}",
                    service.Id, templateErrors);
                // A configuration rejection is a Failed delivery, not silence — same rule as the
                // SSRF guard below: the operator looks at the delivery panel, not container logs.
                return await RecordAttemptAsync(
                    incidentId, service.Id, service.AckUrl, ackType, service.AckPayloadTemplate,
                    null, null, $"Template parse error: {templateErrors}", retryable: false, cancellationToken);
            }

            string renderedPayload;
            try
            {
                renderedPayload = await scribanTemplate.RenderAsync(templateData);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ACK template for service {ServiceId} failed to render", service.Id);
                return await RecordAttemptAsync(
                    incidentId, service.Id, service.AckUrl, ackType, service.AckPayloadTemplate,
                    null, null, $"Template render error: {ex.Message}", retryable: false, cancellationToken);
            }

            if (!IsBareMediaType(service.AckContentType ?? "application/json"))
            {
                return await RecordAttemptAsync(
                    incidentId, service.Id, service.AckUrl, ackType, renderedPayload,
                    null, null, $"Invalid content type '{service.AckContentType}'", retryable: false, cancellationToken);
            }

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

            // Stable across every retry and distinct per (incident, ack type). A reopened incident
            // starts a new episode, so its repeat of an already-delivered event gets a fresh key.
            var episode = await deliveries.GetQueryable().CountAsync(
                d => d.IncidentId == incidentId && d.AckType == ackType && d.Status == WebhookDeliveryStatus.Succeeded,
                cancellationToken);
            request.Headers.TryAddWithoutValidation(DeliveryKeyHeader,
                episode == 0 ? $"{incidentId:D}:{ackType}" : $"{incidentId:D}:{ackType}:e{episode}");

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

            // A dedicated outbound secret wins; the inbound WebhookSecret stays as the fallback so
            // configs predating AckSecret keep signing exactly as before.
            var signingSecret = string.IsNullOrEmpty(service.AckSecret) ? service.WebhookSecret : service.AckSecret;
            if (!string.IsNullOrEmpty(signingSecret))
            {
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
                var signature = "sha256=" + Convert.ToHexString(
                    hmac.ComputeHash(Encoding.UTF8.GetBytes(renderedPayload))).ToLowerInvariant();
                var sigHeader = !string.IsNullOrWhiteSpace(service.AckSignatureHeader)
                    ? service.AckSignatureHeader
                    : !string.IsNullOrWhiteSpace(service.WebhookSignatureHeader)
                        ? service.WebhookSignatureHeader
                        : "X-Callu-Signature";
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

    public async Task<Callu.Shared.Models.Services.ServiceActionExecutionResult> ExecuteManualActionAsync(
        Guid incidentId, Guid actionId, string actorUserId, CancellationToken cancellationToken = default)
    {
        if (serviceActions is null)
            throw new InvalidOperationException("Service action repository is not available.");

        var incident = await incidents.GetWithServiceAsync(incidentId, cancellationToken)
            ?? throw new Callu.Shared.Exceptions.NotFoundException("Incident", incidentId);
        if (incident.Service is null)
            throw new Callu.Shared.Exceptions.NotFoundException("ServiceAction", actionId);

        var action = await serviceActions.GetQueryable()
            .FirstOrDefaultAsync(a => a.Id == actionId && !a.IsDeleted, cancellationToken);
        if (action is null || !action.IsEnabled || action.ServiceId != incident.Service.Id)
            throw new Callu.Shared.Exceptions.NotFoundException("ServiceAction", actionId);

        var chainKey = $"manual:{actionId:D}";
        await GuardAgainstDoubleClickAsync(incidentId, chainKey, cancellationToken);

        string renderedPayload = string.Empty;
        if (!string.IsNullOrEmpty(action.PayloadTemplate) && action.HttpMethod != "GET")
        {
            var template = Scriban.Template.Parse(action.PayloadTemplate);
            if (template.HasErrors)
            {
                var parseError = "Template parse error: " +
                    string.Join("; ", template.Messages.Select(m => m.Message));
                return await FinishManualAsync(incident, action, chainKey, actorUserId,
                    requestBody: action.PayloadTemplate, null, null, parseError, cancellationToken);
            }

            try
            {
                renderedPayload = await template.RenderAsync(ManualTemplateData(incident, incident.Service, action));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return await FinishManualAsync(incident, action, chainKey, actorUserId,
                    requestBody: action.PayloadTemplate, null, null,
                    $"Template render error: {ex.Message}", cancellationToken);
            }
        }

        if (action.HttpMethod != "GET" && !IsBareMediaType(action.ContentType))
        {
            return await FinishManualAsync(incident, action, chainKey, actorUserId,
                renderedPayload, null, null, $"Invalid content type '{action.ContentType}'", cancellationToken);
        }

        var allowPrivate = communicationSettings.Value.AllowPrivateWebhookEndpoint;
        // Unresolvable is terminal here, unlike the event path: a manual run gets exactly one attempt.
        if (CheckAckUrl(action.Url, allowPrivate, out var urlRejection) != AckUrlCheck.Ok)
        {
            return await FinishManualAsync(incident, action, chainKey, actorUserId,
                renderedPayload, null, null, urlRejection, cancellationToken);
        }

        var pending = await WriteManualRowAsync(incident, action, chainKey, renderedPayload,
            WebhookDeliveryStatus.Pending, null, null, null, cancellationToken);

        int? httpStatus = null;
        string? responseSample = null;
        string? errorMessage = null;

        try
        {
            var httpClient = httpClientFactory.CreateClient("WebhookDispatch");
            using var request = new HttpRequestMessage(new HttpMethod(action.HttpMethod), action.Url);
            if (action.HttpMethod != "GET")
                request.Content = new StringContent(renderedPayload, Encoding.UTF8, action.ContentType);

            // Per-execution unique: every click is a distinct intended execution, never a retry.
            request.Headers.TryAddWithoutValidation(DeliveryKeyHeader,
                $"{incidentId:D}:{chainKey}:{(pending?.Id ?? Guid.Empty):D}");

            ApplyOperatorHeaders(request, action.HeadersJson, action.ServiceId);

            // Only the action's own secret signs; a manual target is not the alert source, so the
            // service's inbound webhook secret is never borrowed here.
            if (!string.IsNullOrEmpty(action.Secret))
            {
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(action.Secret));
                var signature = "sha256=" + Convert.ToHexString(
                    hmac.ComputeHash(Encoding.UTF8.GetBytes(renderedPayload))).ToLowerInvariant();
                var sigHeader = string.IsNullOrWhiteSpace(action.SignatureHeader)
                    ? "X-Callu-Signature"
                    : action.SignatureHeader;
                request.Headers.TryAddWithoutValidation(sigHeader, signature);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);
            httpStatus = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            responseSample = body.Length > 1024 ? body[..1024] : body;
            if (!response.IsSuccessStatusCode)
                errorMessage = $"HTTP {httpStatus}";
        }
        catch (HttpRequestException ex)
        {
            errorMessage = ex.Message;
        }
        catch (FormatException ex)
        {
            errorMessage = $"Invalid request configuration: {ex.Message}";
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            errorMessage = "Connect/read timeout";
        }

        return await FinishManualAsync(incident, action, chainKey, actorUserId,
            renderedPayload, httpStatus, responseSample, errorMessage, cancellationToken, pending);
    }

    /// <summary>One recent terminal row means a double-click; a fresh Pending row means a send is still in flight.</summary>
    private async Task GuardAgainstDoubleClickAsync(Guid incidentId, string chainKey, CancellationToken cancellationToken)
    {
        var last = await deliveries.GetQueryable()
            .Where(d => d.IncidentId == incidentId && d.AckType == chainKey)
            .OrderByDescending(d => d.AttemptedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (last is null) return;

        if (last.Status == WebhookDeliveryStatus.Pending)
        {
            if (last.AttemptedAt > DateTime.UtcNow.AddMinutes(-2))
                throw new Callu.Shared.Exceptions.ConflictException(
                    "This action is already executing for this incident.");

            // Stranded by a crash mid-send: closed out, never re-sent.
            last.Status = WebhookDeliveryStatus.Failed;
            last.Error = ClampError("Interrupted: the process stopped before a result was recorded.");
            last.NextRetryAt = null;
            last.UpdatedAt = DateTime.UtcNow;
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another request closed it in the same instant; treat the race as a double click.
                dbContext.Entry(last).State = EntityState.Detached;
                throw new Callu.Shared.Exceptions.ConflictException(
                    "This action is already executing for this incident.");
            }
            return;
        }

        if (last.AttemptedAt > DateTime.UtcNow.AddSeconds(-10))
            throw new Callu.Shared.Exceptions.ConflictException(
                "This action was just executed for this incident; wait a moment before running it again.");
    }

    /// <summary>Writes or finalizes the manual delivery row, then the operator-facing trail; returns the synchronous result.</summary>
    private async Task<Callu.Shared.Models.Services.ServiceActionExecutionResult> FinishManualAsync(
        Incident incident, ServiceAction action, string chainKey, string actorUserId,
        string requestBody, int? httpStatus, string? responseSample, string? error,
        CancellationToken cancellationToken, WebhookDelivery? pendingRow = null)
    {
        var status = error is null ? WebhookDeliveryStatus.Succeeded : WebhookDeliveryStatus.Failed;

        WebhookDelivery? row = pendingRow;
        try
        {
            if (row is not null)
            {
                row.Status = status;
                row.HttpStatus = httpStatus;
                row.ResponseBodySample = responseSample;
                row.Error = ClampError(error);
                row.UpdatedAt = DateTime.UtcNow;
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            else
            {
                row = await WriteManualRowAsync(incident, action, chainKey, requestBody,
                    status, httpStatus, responseSample, error, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (row is not null)
                dbContext.Entry(row).State = EntityState.Detached;
            logger.LogWarning(ex,
                "Could not finalize the delivery row for manual action {ActionId} on incident {IncidentId}",
                action.Id, incident.Id);
        }

        metrics?.ServiceActionExecution("manual", error is null ? "succeeded" : "failed");

        var trail = new IncidentTimelineEvent
        {
            IncidentId = incident.Id,
            EventType = error is null ? TimelineEventType.ActionExecuted : TimelineEventType.ActionFailed,
            Title = error is null ? $"Action '{action.Name}' executed" : $"Action '{action.Name}' failed",
            Description = error is null
                ? $"{action.HttpMethod} {action.Url} → HTTP {httpStatus}"
                : $"{action.HttpMethod} {action.Url} → {error}",
            ActorUserId = actorUserId,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            dbContext.Set<IncidentTimelineEvent>().Add(trail);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (auditLog is not null)
                await auditLog.LogAsync(
                    actorUserId,
                    error is null ? AuditAction.ServiceActionExecuted : AuditAction.ServiceActionFailed,
                    "ServiceAction", action.Id.ToString(),
                    null, error is null ? $"HTTP {httpStatus}" : ClampError(error),
                    description: $"Manual action '{action.Name}' on incident {incident.Id}",
                    cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            dbContext.Entry(trail).State = EntityState.Detached;
            logger.LogWarning(ex,
                "Could not write the trail for manual action {ActionId} on incident {IncidentId}; the delivery row is the record",
                action.Id, incident.Id);
        }

        return new Callu.Shared.Models.Services.ServiceActionExecutionResult
        {
            Outcome = error is null ? "succeeded" : "failed",
            DeliveryId = row?.Id,
            HttpStatus = httpStatus,
            Error = ClampError(error),
            ResponseBodySample = responseSample,
        };
    }

    private async Task<WebhookDelivery?> WriteManualRowAsync(
        Incident incident, ServiceAction action, string chainKey, string requestBody,
        WebhookDeliveryStatus status, int? httpStatus, string? responseSample, string? error,
        CancellationToken cancellationToken)
    {
        WebhookDelivery? row = null;
        try
        {
            var chain = deliveries.GetQueryable()
                .Where(d => d.IncidentId == incident.Id && d.AckType == chainKey);
            var rowCount = await chain.CountAsync(cancellationToken);
            var highestOrdinal = await chain.MaxAsync(d => (int?)d.AttemptCount, cancellationToken) ?? 0;

            row = new WebhookDelivery
            {
                Id = Guid.NewGuid(),
                IncidentId = incident.Id,
                ServiceId = action.ServiceId,
                Direction = "Outbound",
                Url = action.Url[..Math.Min(500, action.Url.Length)],
                AckType = chainKey,
                ActionId = action.Id,
                ActionName = action.Name,
                HttpStatus = httpStatus,
                RequestBodySample = requestBody.Length > 1024 ? requestBody[..1024] : requestBody,
                ResponseBodySample = responseSample,
                Error = ClampError(error),
                AttemptCount = Math.Max(highestOrdinal, rowCount) + 1,
                AttemptedAt = DateTime.UtcNow,
                NextRetryAt = null,
                Status = status,
                CreatedAt = DateTime.UtcNow
            };

            await deliveries.AddAsync(row, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return row;
        }
        catch (DbUpdateException ex) when (
            status == WebhookDeliveryStatus.Pending &&
            ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // The in-flight unique index says another request claimed this chain first.
            if (row is not null)
                dbContext.Entry(row).State = EntityState.Detached;
            throw new Callu.Shared.Exceptions.ConflictException(
                "This action is already executing for this incident.");
        }
        catch (Exception ex)
        {
            if (row is not null)
                dbContext.Entry(row).State = EntityState.Detached;
            logger.LogWarning(ex,
                "Could not persist the delivery row for manual action {ActionId} on incident {IncidentId}",
                action.Id, incident.Id);
            return null;
        }
    }

    private static Dictionary<string, object> ManualTemplateData(Incident incident, Service service, ServiceAction action) => new()
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
        ["service"] = new Dictionary<string, object?>
        {
            ["id"] = service.Id.ToString(),
            ["name"] = service.Name,
        },
        ["action"] = new Dictionary<string, object?>
        {
            ["id"] = action.Id.ToString(),
            ["name"] = action.Name,
        },
    };

    /// <summary>Applies operator-configured headers with the same caps and hop-by-hop blocklist as the event path.</summary>
    private void ApplyOperatorHeaders(HttpRequestMessage request, string? headersJson, Guid serviceId)
    {
        if (string.IsNullOrEmpty(headersJson)) return;
        try
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
            if (headers is null) return;

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
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse action headers for service {ServiceId}", serviceId);
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

            var isManual = ackType.StartsWith("manual:", StringComparison.Ordinal);
            metrics?.ServiceActionExecution(isManual ? "manual" : "event", status switch
            {
                WebhookDeliveryStatus.Succeeded => "succeeded",
                WebhookDeliveryStatus.Retrying => "retrying",
                _ => "failed",
            });

            // A dead event chain leaves a mark where the operator looks; manual runs write their
            // own trail with the real actor at the call site.
            if (status == WebhookDeliveryStatus.Failed && !isManual)
                await WriteChainFailedTraceAsync(incidentId, ackType, url, row.Error, attemptCount, cancellationToken);

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

    /// <summary>Timeline and audit trace for an event chain that will never be retried again; never throws.</summary>
    private async Task WriteChainFailedTraceAsync(
        Guid incidentId, string ackType, string url, string? error, int attemptCount, CancellationToken cancellationToken)
    {
        var trace = new IncidentTimelineEvent
        {
            IncidentId = incidentId,
            EventType = TimelineEventType.ActionFailed,
            Title = "ACK callback failed",
            Description = $"Callback '{ackType}' to {url} failed permanently after {attemptCount} attempt(s): {error}",
            ActorUserId = "system:action",
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            dbContext.Set<IncidentTimelineEvent>().Add(trace);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (auditLog is not null)
                await auditLog.LogAsync(
                    "system:action", AuditAction.ServiceActionFailed, "Incident", incidentId.ToString(),
                    null, ClampError(error),
                    description: $"ACK callback '{ackType}' failed permanently",
                    cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            dbContext.Entry(trace).State = EntityState.Detached;
            logger.LogWarning(ex,
                "Could not write the failure trace for ACK chain '{AckType}' on incident {IncidentId}; the delivery row itself was persisted",
                ackType, incidentId);
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

    /// <summary>A bare type/subtype media type, the only shape StringContent accepts without throwing.</summary>
    internal static bool IsBareMediaType(string value) =>
        System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(value, out var parsed)
        && string.Equals(parsed.MediaType, value, StringComparison.OrdinalIgnoreCase);

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
