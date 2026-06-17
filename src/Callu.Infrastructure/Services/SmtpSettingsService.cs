using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Email;
using Callu.Shared.Localization;
using Callu.Shared.Models.Settings;

namespace Callu.Infrastructure.Services;

/// <summary>
/// SMTP settings management service
/// </summary>
public class SmtpSettingsService : ISmtpSettingsService
{
    private readonly ISmtpSettingsRepository _settingsRepo;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<SmtpSettingsService> _logger;
    private readonly HybridCache _cache;
    private readonly SmtpPasswordProtector _passwordProtector;

    public SmtpSettingsService(
        ISmtpSettingsRepository settingsRepo,
        ITransactionManager transactionManager,
        HybridCache cache,
        SmtpPasswordProtector passwordProtector,
        ILogger<SmtpSettingsService> logger)
    {
        _settingsRepo = settingsRepo;
        _transactionManager = transactionManager;
        _logger = logger;
        _cache = cache;
        _passwordProtector = passwordProtector;
    }

    private const string CacheKey = "smtp-settings";

    public async Task<SmtpSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return await _cache.GetOrCreateAsync(CacheKey, async ct =>
        {
            var settings = await _settingsRepo.GetSettingsAsync(ct);
            return settings == null ? new SmtpSettingsDto() : settings.Adapt<SmtpSettingsDto>();
        }, cancellationToken: cancellationToken);
    }

    public async Task<bool> SaveSettingsAsync(UpdateSmtpSettingsRequest request, CancellationToken cancellationToken = default)
    {
        return await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var settings = await _settingsRepo.GetSettingsAsync(cancellationToken);

            if (settings == null)
            {
                settings = new SmtpSettings
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = DateTime.UtcNow
                };
                await _settingsRepo.AddAsync(settings, cancellationToken);
            }

            settings.Host = request.Host;
            settings.Port = request.Port;
            settings.EnableSsl = request.EnableSsl;
            settings.Username = request.Username;
            settings.FromAddress = request.FromAddress;
            settings.FromName = request.FromName;
            settings.ReplyToAddress = string.IsNullOrWhiteSpace(request.ReplyToAddress)
                ? null
                : request.ReplyToAddress.Trim();
            settings.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(request.Password))
            {
                settings.Password = _passwordProtector.Protect(request.Password);
            }

            settings.IsConfigured = !string.IsNullOrWhiteSpace(settings.Host)
                                    && !string.IsNullOrWhiteSpace(settings.FromAddress);

            _logger.LogInformation("SMTP settings saved");
            await _cache.RemoveAsync(CacheKey, cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<EmailTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var settings = await _settingsRepo.GetSettingsAsync(cancellationToken);

            if (settings == null || string.IsNullOrEmpty(settings.Host))
            {
                return new EmailTestResult
                {
                    Success = false,
                    Message = Messages.Get("smtp.notConfiguredSaveFirst")
                };
            }

            try
            {
                using var client = new SmtpClient(settings.Host, settings.Port)
                {
                    EnableSsl = settings.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 10000
                };

                if (!string.IsNullOrEmpty(settings.Username) && !string.IsNullOrEmpty(settings.Password))
                {
                    var plaintext = _passwordProtector.Unprotect(settings.Password);
                    if (plaintext is null)
                    {
                        settings.LastTestedAt = DateTime.UtcNow;
                        settings.LastTestResult = "Stored password could not be decrypted — re-enter SMTP password";
                        settings.IsConfigured = false;
                        return new EmailTestResult
                        {
                            Success = false,
                            Message = Messages.Get("smtp.passwordDecryptFailed")
                        };
                    }
                    client.Credentials = new NetworkCredential(settings.Username, plaintext);
                }

                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = "Connection test passed";
                settings.IsConfigured = true;

                _logger.LogInformation("SMTP connection test successful");
                return new EmailTestResult
                {
                    Success = true,
                    Message = Messages.Get("smtp.testPassed")
                };
            }
            catch (SmtpException ex)
            {
                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = $"Failed: {ex.Message}";
                settings.IsConfigured = false;

                _logger.LogWarning(ex, "SMTP connection test failed");
                return new EmailTestResult
                {
                    Success = false,
                    Message = $"Connection failed: {ex.Message}"
                };
            }
            catch (IOException ex)
            {
                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = $"Failed: {ex.Message}";
                settings.IsConfigured = false;

                _logger.LogWarning(ex, "SMTP connection test failed");
                return new EmailTestResult
                {
                    Success = false,
                    Message = $"Connection failed: {ex.Message}"
                };
            }
        }, cancellationToken);
    }

    public async Task<EmailTestResult> SendTestEmailAsync(string recipientEmail, CancellationToken cancellationToken = default)
    {
        return await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var settings = await _settingsRepo.GetSettingsAsync(cancellationToken);

            if (settings == null || string.IsNullOrEmpty(settings.Host))
            {
                return new EmailTestResult
                {
                    Success = false,
                    Message = Messages.Get("smtp.notConfigured")
                };
            }

            try
            {
                using var client = new SmtpClient(settings.Host, settings.Port)
                {
                    EnableSsl = settings.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000
                };

                if (!string.IsNullOrEmpty(settings.Username) && !string.IsNullOrEmpty(settings.Password))
                {
                    var plaintext = _passwordProtector.Unprotect(settings.Password);
                    if (plaintext is null)
                    {
                        settings.LastTestedAt = DateTime.UtcNow;
                        settings.LastTestResult = "Stored password could not be decrypted — re-enter SMTP password";
                        settings.IsConfigured = false;
                        return new EmailTestResult
                        {
                            Success = false,
                            Message = Messages.Get("smtp.passwordDecryptFailed")
                        };
                    }
                    client.Credentials = new NetworkCredential(settings.Username, plaintext);
                }

                using var message = new MailMessage
                {
                    From = new MailAddress(settings.FromAddress, settings.FromName),
                    Subject = "CalluApp - Test Email",
                    Body = EmailTemplates.GetTestEmail(),
                    IsBodyHtml = true
                };
                message.To.Add(recipientEmail);

                await client.SendMailAsync(message, cancellationToken);

                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = $"Test email sent to {recipientEmail}";
                settings.IsConfigured = true;

                _logger.LogInformation("Test email sent successfully to {Recipient}", recipientEmail);
                return new EmailTestResult
                {
                    Success = true,
                    Message = $"Test email sent successfully to {recipientEmail}"
                };
            }
            catch (SmtpException ex)
            {
                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = $"Failed to send: {ex.Message}";
                settings.IsConfigured = false;

                _logger.LogError(ex, "Failed to send test email to {Recipient}", recipientEmail);
                return new EmailTestResult
                {
                    Success = false,
                    Message = $"Failed to send: {ex.Message}"
                };
            }
            catch (IOException ex)
            {
                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = $"Failed to send: {ex.Message}";
                settings.IsConfigured = false;

                _logger.LogError(ex, "Failed to send test email to {Recipient}", recipientEmail);
                return new EmailTestResult
                {
                    Success = false,
                    Message = $"Failed to send: {ex.Message}"
                };
            }
        }, cancellationToken);
    }

    public async Task<EmailTestResult> SendTestEmailAsync(string recipientEmail, string templateType, CancellationToken cancellationToken = default)
    {
        return await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var settings = await _settingsRepo.GetSettingsAsync(cancellationToken);

            if (settings == null || string.IsNullOrEmpty(settings.Host))
            {
                return new EmailTestResult
                {
                    Success = false,
                    Message = Messages.Get("smtp.notConfigured")
                };
            }

            try
            {
                using var client = new SmtpClient(settings.Host, settings.Port)
                {
                    EnableSsl = settings.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 30000
                };

                if (!string.IsNullOrEmpty(settings.Username) && !string.IsNullOrEmpty(settings.Password))
                {
                    var plaintext = _passwordProtector.Unprotect(settings.Password);
                    if (plaintext is null)
                    {
                        settings.LastTestedAt = DateTime.UtcNow;
                        settings.LastTestResult = "Stored password could not be decrypted — re-enter SMTP password";
                        settings.IsConfigured = false;
                        return new EmailTestResult
                        {
                            Success = false,
                            Message = Messages.Get("smtp.passwordDecryptFailed")
                        };
                    }
                    client.Credentials = new NetworkCredential(settings.Username, plaintext);
                }

                var (subject, body) = templateType switch
                {
                    "invitation" => (
                        "You've been invited to CalluApp",
                        EmailTemplates.GetInvitationEmail("Test User", "https://calluapp.example.com/invite/test-token")
                    ),
                    "password_reset" => (
                        "Reset your CalluApp password",
                        EmailTemplates.GetPasswordResetEmail("https://calluapp.example.com/reset/test-token")
                    ),
                    "oncall_notification" => (
                        "🚨 Incident Alert: Test Incident",
                        EmailTemplates.GetOnCallNotificationEmail("Test Incident - Database Connection Failed", "Critical", "https://calluapp.example.com/incidents/test-123")
                    ),
                    _ => (
                        "CalluApp - Test Email",
                        EmailTemplates.GetTestEmail()
                    )
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(settings.FromAddress, settings.FromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                message.To.Add(recipientEmail);

                await client.SendMailAsync(message, cancellationToken);

                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = $"Test email ({templateType}) sent to {recipientEmail}";
                settings.IsConfigured = true;

                _logger.LogInformation("Test email ({TemplateType}) sent successfully to {Recipient}", templateType, recipientEmail);
                return new EmailTestResult
                {
                    Success = true,
                    Message = $"Test email ({templateType}) sent successfully to {recipientEmail}"
                };
            }
            catch (SmtpException ex)
            {
                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = $"Failed to send: {ex.Message}";
                settings.IsConfigured = false;

                _logger.LogError(ex, "Failed to send test email ({TemplateType}) to {Recipient}", templateType, recipientEmail);
                return new EmailTestResult
                {
                    Success = false,
                    Message = $"Failed to send: {ex.Message}"
                };
            }
            catch (IOException ex)
            {
                settings.LastTestedAt = DateTime.UtcNow;
                settings.LastTestResult = $"Failed to send: {ex.Message}";
                settings.IsConfigured = false;

                _logger.LogError(ex, "Failed to send test email ({TemplateType}) to {Recipient}", templateType, recipientEmail);
                return new EmailTestResult
                {
                    Success = false,
                    Message = $"Failed to send: {ex.Message}"
                };
            }
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public string GetTemplatePreviewHtml(string templateType)
    {
        return templateType switch
        {
            "invitation" => EmailTemplates.GetInvitationEmail(
                "John Doe", 
                "https://app.callu.io/invitation/sample-token"),
            "password_reset" => EmailTemplates.GetPasswordResetEmail(
                "https://app.callu.io/reset-password/sample-token"),
            "oncall_notification" => EmailTemplates.GetOnCallNotificationEmail(
                "Database Connection Failed", 
                "Critical", 
                "https://app.callu.io/incidents/sample-id"),
            _ => EmailTemplates.GetTestEmail()
        };
    }
}
