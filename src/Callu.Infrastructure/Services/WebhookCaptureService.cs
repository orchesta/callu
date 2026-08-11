using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Webhooks;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Webhook capture service implementation
/// </summary>
public class WebhookCaptureService(
    IWebhookCaptureRepository captureRepo,
    ITransactionManager transactionManager) : IWebhookCaptureService
{
    public async Task<IEnumerable<WebhookCaptureDto>> GetCapturesAsync(Guid serviceId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (safePage, safeSize) = ClampPage(page, pageSize);
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var captures = await captureRepo.GetByServiceAsync(serviceId, safePage, safeSize, cancellationToken);
            return captures.Select(c => c.Adapt<WebhookCaptureDto>());
        }, cancellationToken);
    }

    public async Task<WebhookCaptureDto?> GetCaptureByIdAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var capture = await captureRepo.FindSingleAsync(c => c.Id == captureId && !c.IsDeleted, cancellationToken);
            if (capture == null) return null;

            return capture.Adapt<WebhookCaptureDto>();
        }, cancellationToken);
    }

    public async Task<bool> MarkAsReviewedAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var capture = await captureRepo.FindSingleAsync(c => c.Id == captureId && !c.IsDeleted, cancellationToken);
            if (capture == null) return false;
            capture.Status = WebhookCaptureStatus.Reviewed;
            return true;
        }, cancellationToken);
    }

    public async Task<bool> MarkAsIgnoredAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var capture = await captureRepo.FindSingleAsync(c => c.Id == captureId && !c.IsDeleted, cancellationToken);
            if (capture == null) return false;
            capture.Status = WebhookCaptureStatus.Ignored;
            return true;
        }, cancellationToken);
    }

    public async Task<bool> DeleteCaptureAsync(Guid captureId, CancellationToken cancellationToken = default)
    {
        return await captureRepo.HardDeleteAsync(captureId, cancellationToken);
    }

    public async Task<int> DeleteAllCapturesAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        // No wrapping transaction: the repository deletes in batches that each commit on their own.
        return await captureRepo.HardDeleteScopeAsync(serviceId, null, cancellationToken);
    }

    public async Task<int> GetCaptureCountAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            return await captureRepo.GetCountByServiceAsync(serviceId, cancellationToken);
        }, cancellationToken);
    }

    public async Task<IEnumerable<WebhookCaptureDto>> GetCapturesByIntegrationAsync(Guid integrationId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (safePage, safeSize) = ClampPage(page, pageSize);
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var captures = await captureRepo.GetByIntegrationAsync(integrationId, safePage, safeSize, cancellationToken);
            return captures.Select(c => c.Adapt<WebhookCaptureDto>());
        }, cancellationToken);
    }

    public async Task<int> GetCaptureCountByIntegrationAsync(Guid integrationId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            return await captureRepo.GetCountByIntegrationAsync(integrationId, cancellationToken);
        }, cancellationToken);
    }

    public async Task<int> DeleteAllCapturesByIntegrationAsync(Guid integrationId, CancellationToken cancellationToken = default)
    {
        return await captureRepo.HardDeleteScopeAsync(null, integrationId, cancellationToken);
    }

    private static (int Page, int PageSize) ClampPage(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, WebhookCapture.MaxPageSize));
}
