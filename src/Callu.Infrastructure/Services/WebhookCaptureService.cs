using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
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
    public async Task<IEnumerable<WebhookCaptureDto>> GetCapturesAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var captures = await captureRepo.GetByServiceAsync(serviceId, cancellationToken);
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
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var capture = await captureRepo.FindSingleAsync(c => c.Id == captureId && !c.IsDeleted, cancellationToken);
            if (capture == null) return false;
            capture.IsDeleted = true;
            return true;
        }, cancellationToken);
    }

    public async Task<int> DeleteAllCapturesAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var captures = await captureRepo.FindAsync(c => c.ServiceId == serviceId && !c.IsDeleted, cancellationToken);
            var captureList = captures.ToList();
            foreach (var capture in captureList)
            {
                capture.IsDeleted = true;
            }
            return captureList.Count;
        }, cancellationToken);
    }

    public async Task<int> GetCaptureCountAsync(Guid serviceId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            return await captureRepo.GetCountByServiceAsync(serviceId, cancellationToken);
        }, cancellationToken);
    }
}
