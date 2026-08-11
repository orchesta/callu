using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Email;
using Callu.Shared.Logging;

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
    private readonly IDbEmailTemplateResolver? _templateResolver;

    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);

    public SmtpEmailService(
        ISmtpSettingsRepository settingsRepo,
        ITransactionManager transactionManager,
        SmtpPasswordProtector passwordProtector,
        IOrganizationSettingsService organizationSettingsService,
        ILogger<SmtpEmailService> logger,
        IDbEmailTemplateResolver? templateResolver = null)
    {
        _settingsRepo = settingsRepo;
        _transactionManager = transactionManager;
        _passwordProtector = passwordProtector;
        _organizationSettingsService = organizationSettingsService;
        _logger = logger;
        _templateResolver = templateResolver;
    }

    /// <summary>
    /// B4: operator-edited DB templates take precedence over the built-in file templates.
    /// Returns null when no active DB row exists for the key - the caller uses its file path.
    /// </summary>
    private async Task<RenderedEmailTemplate?> TryDbTemplateAsync(
        string key, IReadOnlyDictionary<string, string> variables, CancellationToken ct)
    {
        if (_templateResolver is null) return null;
        return await _templateResolver.TryRenderAsync(key, variables, ct);
    }

    public async Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var orgSettings = await _organizationSettingsService.GetSettingsAsync(cancellationToken);
        if (orgSettings is { EmailNotificationsEnabled: false })
        {
            _logger.LogInformation("Email notifications disabled by org setting; suppressing send to {Recipient} ({Subject})", PiiRedactor.Email(to), subject);
            return false;
        }

        var settings = await _settingsRepo.GetSettingsAsync(cancellationToken);

        if (settings == null || !settings.IsConfigured)
        {
            _logger.LogWarning("SMTP is not configured. Cannot send email to {Recipient}", PiiRedactor.Email(to));
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

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendCts.CancelAfter(SendTimeout);

            await client.SendMailAsync(message, sendCts.Token);
            _logger.LogInformation("Email sent successfully to {Recipient}", PiiRedactor.Email(to));
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("Sending email to {Recipient} timed out after {Seconds}s", PiiRedactor.Email(to), SendTimeout.TotalSeconds);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to send email to {Recipient}", PiiRedactor.Email(to));
            return false;
        }
    }

    public async Task<bool> SendInvitationAsync(string email, string userName, string inviteLink, CancellationToken cancellationToken = default)
    {
        var db = await TryDbTemplateAsync("invitation", new Dictionary<string, string>
        {
            ["UserName"] = userName,
            ["InviteLink"] = inviteLink,
        }, cancellationToken);
        if (db is not null)
            return await SendAsync(email, db.Subject, db.HtmlBody, cancellationToken);

        var subject = "You've been invited to CalluApp";
        var body = EmailTemplates.GetInvitationEmail(userName, inviteLink);
        return await SendAsync(email, subject, body, cancellationToken);
    }

    public async Task<bool> SendPasswordResetAsync(string email, string resetLink, CancellationToken cancellationToken = default)
    {
        var db = await TryDbTemplateAsync("password_reset", new Dictionary<string, string>
        {
            ["ResetLink"] = resetLink,
        }, cancellationToken);
        if (db is not null)
            return await SendAsync(email, db.Subject, db.HtmlBody, cancellationToken);

        var subject = "Reset your CalluApp password";
        var body = EmailTemplates.GetPasswordResetEmail(resetLink);
        return await SendAsync(email, subject, body, cancellationToken);
    }

    public async Task<bool> SendOnCallNotificationAsync(string email, string incidentTitle, string incidentSeverity, string incidentUrl, CancellationToken cancellationToken = default)
    {
        var db = await TryDbTemplateAsync("oncall_notification", new Dictionary<string, string>
        {
            ["IncidentTitle"] = incidentTitle,
            ["Severity"] = incidentSeverity,
            ["SeverityStyle"] = EmailTemplates.GetSeverityStyle(incidentSeverity),
            ["IncidentUrl"] = incidentUrl,
        }, cancellationToken);
        if (db is not null)
            return await SendAsync(email, db.Subject, db.HtmlBody, cancellationToken);

        var subject = $"Incident Alert: {incidentTitle}";
        var body = EmailTemplates.GetOnCallNotificationEmail(incidentTitle, incidentSeverity, incidentUrl);
        return await SendAsync(email, subject, body, cancellationToken);
    }

    public async Task<bool> SendConferenceInviteAsync(string email, string incidentTitle, string incidentSeverity, string conferenceUrl, int durationMinutes, CancellationToken cancellationToken = default)
    {
        var db = await TryDbTemplateAsync("conference_invite", new Dictionary<string, string>
        {
            ["IncidentTitle"] = incidentTitle,
            ["Severity"] = incidentSeverity,
            ["SeverityStyle"] = EmailTemplates.GetSeverityStyle(incidentSeverity),
            ["ConferenceUrl"] = conferenceUrl,
            ["DurationMinutes"] = durationMinutes.ToString(),
        }, cancellationToken);
        if (db is not null)
            return await SendAsync(email, db.Subject, db.HtmlBody, cancellationToken);

        var subject = $"Video Conference: {incidentTitle}";
        var body = EmailTemplates.GetConferenceInviteEmail(incidentTitle, incidentSeverity, conferenceUrl, durationMinutes);
        return await SendAsync(email, subject, body, cancellationToken);
    }

    public async Task<bool> SendStatusPageSubscriptionConfirmationAsync(string email, string pageName, string confirmLink, CancellationToken cancellationToken = default)
    {
        var db = await TryDbTemplateAsync("status_page_subscription_confirmation", new Dictionary<string, string>
        {
            ["PageName"] = pageName,
            ["ConfirmLink"] = confirmLink,
        }, cancellationToken);
        if (db is not null)
            return await SendAsync(email, db.Subject, db.HtmlBody, cancellationToken);

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

        if (settings.Port == 465)
            return (false, "Port 465 (implicit TLS/SMTPS) is not supported by the built-in SMTP client; " +
                           "email will not send. Use port 587 with STARTTLS instead.");

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
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = (int)SendTimeout.TotalMilliseconds
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
