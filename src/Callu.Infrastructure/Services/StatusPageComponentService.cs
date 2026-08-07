using Microsoft.Extensions.Options;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Infrastructure.Configuration;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Constants;
using Callu.Shared.Models.StatusPages;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Manages status page components — CRUD, health check configuration, and overall status recalculation.
/// Split from the original monolithic StatusPageService for SRP.
/// </summary>
public class StatusPageComponentService(
    IStatusPageRepository statusPageRepo,
    IRepository<StatusPageComponent> componentRepo,
    IServiceRepository serviceRepo,
    IOptions<CommunicationSettingsOptions> communicationOptions,
    ITransactionManager transactionManager) : IStatusPageComponentService
{
    private bool AllowPrivateHealthCheck => communicationOptions.Value.AllowPrivateHealthCheckEndpoint;

    public async Task<bool> AddComponentAsync(Guid pageId, AddComponentRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var pageExists = await statusPageRepo.ExistsAsync(p => p.Id == pageId && !p.IsDeleted, cancellationToken);
            if (!pageExists) return false;

            if (request.HealthCheckEnabled && !string.IsNullOrEmpty(request.HealthCheckUrl))
            {
                if (!Utilities.UrlSanitizer.IsValidHealthCheckUrl(request.HealthCheckUrl, AllowPrivateHealthCheck))
                    throw new ArgumentException(Utilities.UrlSanitizer.GetBlockedReason(request.HealthCheckUrl));
            }

            var httpMethod = request.HealthCheckHttpMethod?.ToUpperInvariant();
            if (httpMethod != null && !ComponentStatuses.AllowedHttpMethods.Contains(httpMethod))
                httpMethod = "GET";

            var components = await componentRepo.FindAsync(c => c.StatusPageId == pageId && !c.IsDeleted, cancellationToken);
            var maxOrder = components.Any() ? components.Max(c => c.DisplayOrder) : 0;

            var initialStatus = ComponentStatuses.Operational;
            if (request.ServiceId is { } linkedServiceId && !request.HealthCheckEnabled)
            {
                var linked = await serviceRepo.FindSingleAsync(
                    s => s.Id == linkedServiceId && !s.IsDeleted, cancellationToken);
                if (linked != null)
                    initialStatus = ComponentStatuses.FromServiceStatus(linked.Status);
            }

            var component = new StatusPageComponent
            {
                Name = request.Name,
                Description = request.Description,
                ServiceId = request.ServiceId,
                StatusPageId = pageId,
                Status = initialStatus,
                DisplayOrder = maxOrder + 1,
                HealthCheckEnabled = request.HealthCheckEnabled,
                HealthCheckUrl = request.HealthCheckUrl,
                HealthCheckHttpMethod = httpMethod,
                HealthCheckIntervalSeconds = request.HealthCheckIntervalSeconds,
                HealthCheckTimeoutSeconds = request.HealthCheckTimeoutSeconds,
                HealthCheckHeaders = request.HealthCheckHeaders,
                HealthCheckBody = request.HealthCheckBody,
                HealthCheckContentType = request.HealthCheckContentType,
                HealthCheckFieldMappings = request.HealthCheckFieldMappings,
                HealthCheckStateMapping = request.HealthCheckStateMapping,
            };

            await componentRepo.AddAsync(component, cancellationToken);
            await RecalculateOverallStatusAsync(pageId, cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> UpdateComponentAsync(Guid componentId, UpdateComponentRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var component = await componentRepo.FindSingleAsync(c => c.Id == componentId && !c.IsDeleted, cancellationToken);
            if (component == null) return false;

            if (request.Name != null) component.Name = request.Name;
            if (request.Status != null) component.Status = request.Status;
            if (request.DisplayOrder.HasValue) component.DisplayOrder = request.DisplayOrder.Value;

            if ((request.HealthCheckEnabled ?? component.HealthCheckEnabled)
                && !string.IsNullOrEmpty(request.HealthCheckUrl ?? component.HealthCheckUrl))
            {
                var urlToCheck = request.HealthCheckUrl ?? component.HealthCheckUrl;
                if (!Utilities.UrlSanitizer.IsValidHealthCheckUrl(urlToCheck, AllowPrivateHealthCheck))
                    throw new ArgumentException(Utilities.UrlSanitizer.GetBlockedReason(urlToCheck));
            }

            if (request.HealthCheckHttpMethod != null)
            {
                var method = request.HealthCheckHttpMethod.ToUpperInvariant();
                component.HealthCheckHttpMethod = ComponentStatuses.AllowedHttpMethods.Contains(method) ? method : "GET";
            }

            if (request.HealthCheckEnabled.HasValue) component.HealthCheckEnabled = request.HealthCheckEnabled.Value;
            if (request.HealthCheckUrl != null) component.HealthCheckUrl = request.HealthCheckUrl;
            if (request.HealthCheckIntervalSeconds.HasValue) component.HealthCheckIntervalSeconds = request.HealthCheckIntervalSeconds.Value;
            if (request.HealthCheckTimeoutSeconds.HasValue) component.HealthCheckTimeoutSeconds = request.HealthCheckTimeoutSeconds.Value;
            if (request.HealthCheckHeaders != null) component.HealthCheckHeaders = request.HealthCheckHeaders;
            if (request.HealthCheckBody != null) component.HealthCheckBody = request.HealthCheckBody;
            if (request.HealthCheckContentType != null) component.HealthCheckContentType = request.HealthCheckContentType;
            if (request.HealthCheckFieldMappings != null) component.HealthCheckFieldMappings = request.HealthCheckFieldMappings;
            if (request.HealthCheckStateMapping != null) component.HealthCheckStateMapping = request.HealthCheckStateMapping;

            await RecalculateOverallStatusAsync(component.StatusPageId, cancellationToken);

            return true;
        }, cancellationToken);
    }

    public async Task<bool> RemoveComponentAsync(Guid componentId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var component = await componentRepo.FindSingleAsync(c => c.Id == componentId && !c.IsDeleted, cancellationToken);
            if (component == null) return false;

            component.IsDeleted = true;
            await RecalculateOverallStatusAsync(component.StatusPageId, cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task SyncFromServiceStatusesAsync(
        IReadOnlyList<(Guid ServiceId, ServiceStatus Status)> updates,
        CancellationToken cancellationToken = default)
    {
        if (updates.Count == 0) return;

        var byService = updates
            .GroupBy(u => u.ServiceId)
            .ToDictionary(g => g.Key, g => g.Last().Status);

        var serviceIds = byService.Keys.ToList();
        var components = (await componentRepo.FindAsync(
            c => !c.IsDeleted
                 && c.ServiceId.HasValue
                 && serviceIds.Contains(c.ServiceId.Value)
                 && !c.HealthCheckEnabled,
            cancellationToken)).ToList();

        if (components.Count == 0) return;

        var affectedPageIds = new HashSet<Guid>();
        foreach (var component in components)
        {
            var mapped = ComponentStatuses.FromServiceStatus(byService[component.ServiceId!.Value]);
            if (component.Status == mapped) continue;
            component.Status = mapped;
            affectedPageIds.Add(component.StatusPageId);
        }

        foreach (var pageId in affectedPageIds)
            await RecalculateOverallStatusAsync(pageId, cancellationToken);
    }

    private async Task RecalculateOverallStatusAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var page = await statusPageRepo.FindSingleAsync(p => p.Id == pageId && !p.IsDeleted, cancellationToken);
        if (page == null) return;

        var components = await componentRepo.FindAsync(c => c.StatusPageId == pageId && !c.IsDeleted, cancellationToken);
        page.OverallStatus = ComponentStatuses.AggregateOverallStatus(components.Select(c => c.Status));
    }
}
