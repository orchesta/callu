using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Domain.Entities;
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
    ITransactionManager transactionManager) : IStatusPageComponentService
{
    public async Task<bool> AddComponentAsync(Guid pageId, AddComponentRequest request, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var pageExists = await statusPageRepo.ExistsAsync(p => p.Id == pageId && !p.IsDeleted, cancellationToken);
            if (!pageExists) return false;

            if (request.HealthCheckEnabled && !string.IsNullOrEmpty(request.HealthCheckUrl))
            {
                if (!Utilities.UrlSanitizer.IsValidHealthCheckUrl(request.HealthCheckUrl))
                    throw new ArgumentException(Utilities.UrlSanitizer.GetBlockedReason(request.HealthCheckUrl));
            }

            var httpMethod = request.HealthCheckHttpMethod?.ToUpperInvariant();
            if (httpMethod != null && !Shared.Constants.ComponentStatuses.AllowedHttpMethods.Contains(httpMethod))
                httpMethod = "GET";

            var components = await componentRepo.FindAsync(c => c.StatusPageId == pageId && !c.IsDeleted, cancellationToken);
            var maxOrder = components.Any() ? components.Max(c => c.DisplayOrder) : 0;

            var component = new StatusPageComponent
            {
                Name = request.Name,
                Description = request.Description,
                ServiceId = request.ServiceId,
                StatusPageId = pageId,
                Status = Shared.Constants.ComponentStatuses.Operational,
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
                if (!Utilities.UrlSanitizer.IsValidHealthCheckUrl(urlToCheck))
                    throw new ArgumentException(Utilities.UrlSanitizer.GetBlockedReason(urlToCheck));
            }

            if (request.HealthCheckHttpMethod != null)
            {
                var method = request.HealthCheckHttpMethod.ToUpperInvariant();
                component.HealthCheckHttpMethod = Shared.Constants.ComponentStatuses.AllowedHttpMethods.Contains(method) ? method : "GET";
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

    private async Task RecalculateOverallStatusAsync(Guid pageId, CancellationToken cancellationToken)
    {
        var page = await statusPageRepo.FindSingleAsync(p => p.Id == pageId && !p.IsDeleted, cancellationToken);
        if (page == null) return;

        var components = await componentRepo.FindAsync(c => c.StatusPageId == pageId && !c.IsDeleted, cancellationToken);
        page.OverallStatus = ComponentStatuses.AggregateOverallStatus(components.Select(c => c.Status));
    }
}
