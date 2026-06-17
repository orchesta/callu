using Callu.Shared.Models.Webhooks;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for Webhook Template operations
/// </summary>
public interface IWebhookTemplateService
{
    /// <summary>
    /// Get all templates (built-in and custom)
    /// </summary>
    Task<IEnumerable<WebhookTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get template by ID
    /// </summary>
    Task<WebhookTemplateDto?> GetTemplateByIdAsync(Guid templateId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Create a new template
    /// </summary>
    Task<WebhookTemplateDto> CreateTemplateAsync(CreateWebhookTemplateRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Create template from a captured request
    /// </summary>
    Task<WebhookTemplateDto> CreateTemplateFromCaptureAsync(Guid captureId, CreateWebhookTemplateRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update a template
    /// </summary>
    Task<bool> UpdateTemplateAsync(Guid templateId, UpdateWebhookTemplateRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete a template
    /// </summary>
    Task<bool> DeleteTemplateAsync(Guid templateId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Test template with sample payload
    /// </summary>
    Task<WebhookTemplateTestResult> TestTemplateAsync(Guid templateId, string samplePayload, CancellationToken cancellationToken = default);
}
