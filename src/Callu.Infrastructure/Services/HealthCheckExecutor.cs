using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Infrastructure.Utilities;
using Callu.Shared.Constants;
using Callu.Shared.Models.StatusPages;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Executes health checks against component URLs.
/// Updates component status and recalculates page overall status.
/// </summary>
public class HealthCheckExecutor(
    IRepository<Domain.Entities.StatusPageComponent> componentRepo,
    IStatusPageRepository statusPageRepo,
    IHealthCheckResponseParser responseParser,
    IHttpClientFactory httpClientFactory,
    IUnitOfWork unitOfWork,
    ILogger<HealthCheckExecutor> logger) : IHealthCheckExecutor
{
    /// <summary>Max concurrent health checks per tick.</summary>
    private const int MaxConcurrency = 10;

    public async Task ExecuteAllChecksAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var components = await componentRepo.GetQueryable()
            .Where(c => c.HealthCheckEnabled
                      && !c.IsDeleted
                      && !string.IsNullOrEmpty(c.HealthCheckUrl)
                      && (c.LastHealthCheckAt == null
                          || c.LastHealthCheckAt.Value.AddSeconds(c.HealthCheckIntervalSeconds) <= now))
            .ToListAsync(cancellationToken);

        if (components.Count == 0) return;

        logger.LogDebug("Executing health checks for {Count} components", components.Count);

        var totalSw = Stopwatch.StartNew();

        var affectedPageIds = new ConcurrentBag<Guid>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrency,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(components, parallelOptions, async (component, ct) =>
        {
            await ExecuteAndUpdateComponentAsync(component, affectedPageIds, now, ct);
        });

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist health check results for {Count} components", components.Count);
            return;
        }

        var distinctPageIds = affectedPageIds.Distinct().ToList();
        if (distinctPageIds.Count > 0)
        {
            foreach (var pageId in distinctPageIds)
            {
                await RecalculatePageStatusAsync(pageId, cancellationToken);
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist page status recalculation for {Count} page(s)", distinctPageIds.Count);
            }
        }

        totalSw.Stop();
        logger.LogDebug("Executed {Count} health checks in {Ms}ms", components.Count, totalSw.ElapsedMilliseconds);
    }

    public async Task<HealthCheckResultDto> ExecuteSingleCheckAsync(Guid componentId, CancellationToken cancellationToken = default)
    {
        var component = await componentRepo.FindSingleAsync(
            c => c.Id == componentId && !c.IsDeleted, cancellationToken);

        if (component == null)
        {
            return new HealthCheckResultDto
            {
                ComponentId = componentId,
                Status = ComponentStatuses.MajorOutage,
                Message = "Component not found",
                CheckedAt = DateTime.UtcNow
            };
        }

        var result = await ExecuteCheckForComponentAsync(component, cancellationToken);

        component.Status = result.Status;
        component.LastHealthCheckAt = result.CheckedAt;
        component.LastHealthCheckResult = result.Status;
        component.LastHealthCheckResponseMs = result.ResponseMs;
        component.HealthCheckConsecutiveFailures = result.Status == ComponentStatuses.Operational ? 0
            : component.HealthCheckConsecutiveFailures + 1;

        await RecalculatePageStatusAsync(component.StatusPageId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result;
    }

    public async Task<HealthCheckSnifferResultDto> SniffAsync(Guid componentId, CancellationToken cancellationToken = default)
    {
        var component = await componentRepo.FindSingleAsync(
            c => c.Id == componentId && !c.IsDeleted, cancellationToken);

        if (component == null || string.IsNullOrEmpty(component.HealthCheckUrl))
        {
            return new HealthCheckSnifferResultDto
            {
                ComponentId = componentId,
                HttpStatusCode = 0,
                ResponseBody = "Component not found or no URL configured"
            };
        }

        if (!UrlSanitizer.IsValidHealthCheckUrl(component.HealthCheckUrl))
        {
            logger.LogWarning("Sniff URL blocked for component {ComponentId}: restricted address", componentId);
            return new HealthCheckSnifferResultDto
            {
                ComponentId = componentId,
                HttpStatusCode = 0,
                ResponseBody = $"Blocked: {UrlSanitizer.GetBlockedReason(component.HealthCheckUrl)}"
            };
        }

        var httpClient = httpClientFactory.CreateClient("HealthCheck");

        var timeoutSeconds = Math.Max(component.HealthCheckTimeoutSeconds, 1);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var sw = Stopwatch.StartNew();
        try
        {
            var method = GetValidatedHttpMethod(component.HealthCheckHttpMethod);
            var request = new HttpRequestMessage(method, component.HealthCheckUrl);

            AddCustomHeaders(request, component.HealthCheckHeaders);

            if (!string.IsNullOrEmpty(component.HealthCheckBody))
            {
                request.Content = new StringContent(
                    component.HealthCheckBody,
                    System.Text.Encoding.UTF8,
                    component.HealthCheckContentType ?? "application/json");
            }

            var response = await httpClient.SendAsync(request, cts.Token);
            sw.Stop();

            var body = await ReadBoundedResponseAsync(response.Content, cts.Token);

            component.HealthCheckSampleResponse = TruncateForStorage(body, ComponentStatuses.MaxSampleResponseLength);
            component.HealthCheckListeningMode = false;

            var responseHeaders = response.Headers
                .Concat(response.Content.Headers)
                .ToDictionary(
                    h => h.Key,
                    h => string.Join(", ", h.Value));

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return new HealthCheckSnifferResultDto
            {
                ComponentId = componentId,
                HttpStatusCode = (int)response.StatusCode,
                ResponseBody = body,
                ContentType = response.Content.Headers.ContentType?.MediaType,
                ResponseMs = (int)sw.ElapsedMilliseconds,
                ResponseHeaders = responseHeaders
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new HealthCheckSnifferResultDto
            {
                ComponentId = componentId,
                HttpStatusCode = 0,
                ResponseBody = "Request timed out",
                ResponseMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new HealthCheckSnifferResultDto
            {
                ComponentId = componentId,
                HttpStatusCode = 0,
                ResponseBody = $"Request failed: {ex.Message}",
                ResponseMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    private const int DefaultFailureThreshold = 3;

    private async Task ExecuteAndUpdateComponentAsync(
        Domain.Entities.StatusPageComponent component,
        ConcurrentBag<Guid> affectedPageIds,
        DateTime now,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await ExecuteCheckForComponentAsync(component, cancellationToken);

            var previousStatus = component.Status;
            component.LastHealthCheckAt = result.CheckedAt;
            component.LastHealthCheckResult = result.Status;
            component.LastHealthCheckResponseMs = result.ResponseMs;

            if (result.Status == ComponentStatuses.Operational)
                component.HealthCheckConsecutiveFailures = 0;
            else
                component.HealthCheckConsecutiveFailures++;

            var threshold = component.HealthCheckFailureThreshold ?? DefaultFailureThreshold;
            var newStatus = result.Status == ComponentStatuses.Operational
                ? ComponentStatuses.Operational
                : component.HealthCheckConsecutiveFailures >= threshold
                    ? result.Status
                    : component.Status;

            if (newStatus != previousStatus)
            {
                component.Status = newStatus;
                affectedPageIds.Add(component.StatusPageId);
                logger.LogInformation(
                    "Component {ComponentId} status changed: {Old} → {New} (consecutive failures: {Failures})",
                    component.Id, previousStatus, newStatus, component.HealthCheckConsecutiveFailures);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed for component {ComponentId}", component.Id);
            component.LastHealthCheckAt = now;
            component.LastHealthCheckResult = ComponentStatuses.MajorOutage;
            component.HealthCheckConsecutiveFailures++;

            var threshold = component.HealthCheckFailureThreshold ?? DefaultFailureThreshold;
            if (component.HealthCheckConsecutiveFailures >= threshold
                && component.Status != ComponentStatuses.MajorOutage)
            {
                component.Status = ComponentStatuses.MajorOutage;
                affectedPageIds.Add(component.StatusPageId);
            }
        }
    }

    private async Task<HealthCheckResultDto> ExecuteCheckForComponentAsync(
        Domain.Entities.StatusPageComponent component, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(component.HealthCheckUrl))
        {
            return new HealthCheckResultDto
            {
                ComponentId = component.Id,
                Status = ComponentStatuses.MajorOutage,
                Message = "No URL configured",
                CheckedAt = DateTime.UtcNow
            };
        }

        if (!UrlSanitizer.IsValidHealthCheckUrl(component.HealthCheckUrl))
        {
            logger.LogWarning("Health check URL blocked for component {ComponentId}: restricted address", component.Id);
            return new HealthCheckResultDto
            {
                ComponentId = component.Id,
                Status = ComponentStatuses.MajorOutage,
                Message = $"Blocked: {UrlSanitizer.GetBlockedReason(component.HealthCheckUrl)}",
                CheckedAt = DateTime.UtcNow
            };
        }

        var httpClient = httpClientFactory.CreateClient("HealthCheck");

        var timeoutSeconds = Math.Max(component.HealthCheckTimeoutSeconds, 1);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var sw = Stopwatch.StartNew();
        try
        {
            var method = GetValidatedHttpMethod(component.HealthCheckHttpMethod);
            var request = new HttpRequestMessage(method, component.HealthCheckUrl);

            AddCustomHeaders(request, component.HealthCheckHeaders);

            if (!string.IsNullOrEmpty(component.HealthCheckBody))
            {
                request.Content = new StringContent(
                    component.HealthCheckBody,
                    System.Text.Encoding.UTF8,
                    component.HealthCheckContentType ?? "application/json");
            }

            var response = await httpClient.SendAsync(request, cts.Token);
            sw.Stop();

            var body = await ReadBoundedResponseAsync(response.Content, cts.Token);

            if (component.HealthCheckListeningMode)
            {
                component.HealthCheckSampleResponse = TruncateForStorage(body, ComponentStatuses.MaxSampleResponseLength);
                component.HealthCheckListeningMode = false;
            }

            var parseResult = responseParser.Parse(
                body,
                (int)response.StatusCode,
                component.HealthCheckFieldMappings,
                component.HealthCheckStateMapping);

            return new HealthCheckResultDto
            {
                ComponentId = component.Id,
                Status = parseResult.Status,
                ResponseMs = (int)sw.ElapsedMilliseconds,
                Message = parseResult.Message ?? $"HTTP {(int)response.StatusCode}",
                CheckedAt = DateTime.UtcNow
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new HealthCheckResultDto
            {
                ComponentId = component.Id,
                Status = ComponentStatuses.MajorOutage,
                ResponseMs = (int)sw.ElapsedMilliseconds,
                Message = "Request timed out",
                CheckedAt = DateTime.UtcNow
            };
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return new HealthCheckResultDto
            {
                ComponentId = component.Id,
                Status = ComponentStatuses.MajorOutage,
                ResponseMs = (int)sw.ElapsedMilliseconds,
                Message = $"Connection failed: {ex.Message}",
                CheckedAt = DateTime.UtcNow
            };
        }
    }

    private async Task RecalculatePageStatusAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var page = await statusPageRepo.FindSingleAsync(p => p.Id == pageId && !p.IsDeleted, cancellationToken);
        if (page == null) return;

        var statuses = await componentRepo.GetQueryable()
            .Where(c => c.StatusPageId == pageId && !c.IsDeleted)
            .Select(c => c.Status)
            .ToListAsync(cancellationToken);

        page.OverallStatus = ComponentStatuses.AggregateOverallStatus(statuses);
    }

    /// <summary>
    /// Reads response body with a size limit to prevent OOM.
    /// </summary>
    private static async Task<string> ReadBoundedResponseAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var buffer = new char[ComponentStatuses.MaxResponseBodyBytes];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(0, buffer.Length), ct);

        if (read >= ComponentStatuses.MaxResponseBodyBytes)
        {
            return new string(buffer, 0, read) + "\n... [truncated at 64KB]";
        }

        return new string(buffer, 0, read);
    }

    /// <summary>
    /// Truncates a string for DB storage.
    /// </summary>
    private static string? TruncateForStorage(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>
    /// Validates and returns an HttpMethod, defaulting to GET for invalid methods.
    /// </summary>
    private static HttpMethod GetValidatedHttpMethod(string? method)
    {
        var upper = method?.ToUpperInvariant() ?? "GET";
        if (!ComponentStatuses.AllowedHttpMethods.Contains(upper))
        {
            upper = "GET";
        }
        return new HttpMethod(upper);
    }

    /// <summary>
    /// Adds custom headers from JSON, handling Content-Type separately.
    /// </summary>
    private static void AddCustomHeaders(HttpRequestMessage request, string? headersJson)
    {
        if (string.IsNullOrEmpty(headersJson)) return;

        try
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
            if (headers == null) return;

            foreach (var (key, value) in headers)
            {
                if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    if (request.Content != null)
                    {
                        request.Content.Headers.ContentType =
                            new System.Net.Http.Headers.MediaTypeHeaderValue(value);
                    }
                    continue;
                }

                request.Headers.TryAddWithoutValidation(key, value);
            }
        }
        catch (JsonException)
        {
        }
    }
}
