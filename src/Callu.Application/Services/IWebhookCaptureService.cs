using Callu.Shared.Models.Webhooks;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for Webhook Capture operations
/// </summary>
public interface IWebhookCaptureService
{
    /// <summary>
    /// Get captures for a service
    /// </summary>
    Task<IEnumerable<WebhookCaptureDto>> GetCapturesAsync(Guid serviceId, int page, int pageSize, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get capture by ID
    /// </summary>
    Task<WebhookCaptureDto?> GetCaptureByIdAsync(Guid captureId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Mark capture as reviewed
    /// </summary>
    Task<bool> MarkAsReviewedAsync(Guid captureId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Mark capture as ignored
    /// </summary>
    Task<bool> MarkAsIgnoredAsync(Guid captureId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete a capture
    /// </summary>
    Task<bool> DeleteCaptureAsync(Guid captureId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete all captures for a service
    /// </summary>
    Task<int> DeleteAllCapturesAsync(Guid serviceId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get capture count for a service
    /// </summary>
    Task<int> GetCaptureCountAsync(Guid serviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get captures for an integration
    /// </summary>
    Task<IEnumerable<WebhookCaptureDto>> GetCapturesByIntegrationAsync(Guid integrationId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get capture count for an integration
    /// </summary>
    Task<int> GetCaptureCountByIntegrationAsync(Guid integrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all captures for an integration
    /// </summary>
    Task<int> DeleteAllCapturesByIntegrationAsync(Guid integrationId, CancellationToken cancellationToken = default);
}
