using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Email;

namespace Callu.Infrastructure.Services;

/// <summary>
/// SMTP-based email service implementation
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly ISmtpSettingsRepository _settingsRepo;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly SmtpPasswordProtector _passwordProtector;
    private readonly IOrganizationSettingsService _organizationSettingsService;

    public SmtpEmailService(
        ISmtpSettingsRepository settingsRepo,
        ITransactionManager transactionManager,
        SmtpPasswordProtector passwordProtector,
        IOrganizationSettingsService organizationSettingsService,
        ILogger<SmtpEmailService> logger)
    {
        _settingsRepo = settingsRepo;
        _transactionManager = transactionManager;
        _passwordProtector = passwordProtector;
        _organizationSettingsService = organizationSettingsService;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var orgSettings = await _organizationSettingsService.GetSettingsAsync(cancellationToken);
        if (orgSettings is { EmailNotificationsEnabled: false })
        {
            _logger.LogInformation("Email notifications disabled by org setting; suppressing send to {Recipient} ({Subject})", to, subject);
            return false;
        }

        var settings = await _settingsRepo.GetSettingsAsync(cancellationToken);

        if (settings == null || !settings.IsConfigured)
        {
            _logger.LogWarning("SMTP is not configured. Cannot send email to {Recipient}", to);
            return false;
        }

        try
        {
            using var client = CreateSmtpClient(settings);
            using var message = new MailMessage
            {
                From = new MailAddress(settings.FromAddress, settings.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(to);

            if (!string.IsNullOrWhiteSpace(settings.ReplyToAddress))
            {
                try
                {
                    message.ReplyToList.Add(new MailAddress(settings.ReplyToAddress));
                }
                catch (FormatException ex)
                {
                    _logger.LogWarning(ex, "Invalid Reply-To address '{ReplyTo}' ignored", settings.ReplyToAddress);
                }
            }

            await client.SendMailAsync(message, cancellationToken);
            _logger.LogInformation("Email sent successfully to {Recipient}", to);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient}", to);
            return false;
        }
    }

    public async Task<bool> SendInvitationAsync(string email, string userName, string inviteLink, CancellationToken cancellationToken = default)
    {
        var subject = "You've been invited to CalluApp";
        var body = EmailTemplates.GetInvitationEmail(userName, inviteLink);
        return await SendAsync(email, subject, body, cancellationToken);
    }

    public async Task<bool> SendPasswordResetAsync(string email, string resetLink, CancellationToken cancellationToken = default)
    {
        var subject = "Reset your CalluApp password";
        var body = EmailTemplates.GetPasswordResetEmail(resetLink);
        return await SendAsync(email, subject, body, cancellationToken);
    }

    public async Task<bool> SendOnCallNotificationAsync(string email, string incidentTitle, string incidentSeverity, string incidentUrl, CancellationToken cancellationToken = default)
    {
        var subject = $"Incident Alert: {incidentTitle}";
        var body = EmailTemplates.GetOnCallNotificationEmail(incidentTitle, incidentSeverity, incidentUrl);
        return await SendAsync(email, subject, body, cancellationToken);
    }

    public async Task<bool> SendStatusPageSubscriptionConfirmationAsync(string email, string pageName, string confirmLink, CancellationToken cancellationToken = default)
    {
        var subject = $"Confirm your subscription to {pageName}";
        var body = EmailTemplates.GetStatusPageSubscriptionConfirmationEmail(pageName, confirmLink);
        return await SendAsync(email, subject, body, cancellationToken);
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync(cancellationToken);
        return settings?.IsConfigured ?? false;
    }

    public async Task<(bool Ok, string Message)> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsRepo.GetSettingsAsync(cancellationToken);
        if (settings is null || string.IsNullOrWhiteSpace(settings.Host))
            return (false, "SMTP not configured.");

        try
        {
            using var client = CreateSmtpClient(settings);
            using var tcp = new System.Net.Sockets.TcpClient();
            var connectTask = tcp.ConnectAsync(settings.Host, settings.Port);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            var completed = await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, timeoutCts.Token));
            if (completed != connectTask)
                return (false, $"SMTP {settings.Host}:{settings.Port} connect timed out after 5s.");
            await connectTask;
            return (true, "SMTP relay reachable.");
        }
        catch (Exception ex)
        {
            return (false, $"SMTP probe failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private SmtpClient CreateSmtpClient(SmtpSettings settings)
    {
        var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrEmpty(settings.Username) && !string.IsNullOrEmpty(settings.Password))
        {
            var plaintext = _passwordProtector.Unprotect(settings.Password);
            if (!string.IsNullOrEmpty(plaintext))
                client.Credentials = new NetworkCredential(settings.Username, plaintext);
        }

        return client;
    }
}
