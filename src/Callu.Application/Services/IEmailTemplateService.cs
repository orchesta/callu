using Callu.Shared.Models.Email;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for Email Template operations
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Get all email templates
    /// </summary>
    Task<IEnumerable<EmailTemplateDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get email template by ID (with full body)
    /// </summary>
    Task<EmailTemplateDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new email template
    /// </summary>
    Task<EmailTemplateDto> CreateAsync(CreateEmailTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing email template
    /// </summary>
    Task<EmailTemplateDetailDto?> UpdateAsync(Guid id, UpdateEmailTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an email template (system templates cannot be deleted)
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Preview template with sample variables replaced
    /// </summary>
    Task<string> PreviewAsync(Guid id, Dictionary<string, string> variables, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a test email using the template
    /// </summary>
    Task<(bool Success, string? ErrorMessage)> SendTestAsync(Guid id, string recipientEmail, CancellationToken cancellationToken = default);
}
