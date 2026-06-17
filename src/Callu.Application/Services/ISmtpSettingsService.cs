using Callu.Shared.Models.Settings;

namespace Callu.Application.Services;

/// <summary>
/// Service interface for SMTP settings management
/// </summary>
public interface ISmtpSettingsService
{
    /// <summary>
    /// Get current SMTP settings
    /// </summary>
    Task<SmtpSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save SMTP settings
    /// </summary>
    Task<bool> SaveSettingsAsync(UpdateSmtpSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Test SMTP connection with current settings
    /// </summary>
    Task<EmailTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a test email to verify configuration
    /// </summary>
    Task<EmailTestResult> SendTestEmailAsync(string recipientEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a test email with a specific template type
    /// </summary>
    /// <param name="recipientEmail">Recipient email address</param>
    /// <param name="templateType">Template type: test, invitation, password_reset, oncall_notification</param>
    Task<EmailTestResult> SendTestEmailAsync(string recipientEmail, string templateType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a preview HTML of a specific email template type.
    /// </summary>
    string GetTemplatePreviewHtml(string templateType);
}
