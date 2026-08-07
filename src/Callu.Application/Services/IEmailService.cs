namespace Callu.Application.Services;

/// <summary>
/// Service interface for sending emails
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Send a generic email
    /// </summary>
    Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send user invitation email
    /// </summary>
    Task<bool> SendInvitationAsync(string email, string userName, string inviteLink, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send password reset email
    /// </summary>
    Task<bool> SendPasswordResetAsync(string email, string resetLink, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send on-call notification email
    /// </summary>
    Task<bool> SendOnCallNotificationAsync(string email, string incidentTitle, string incidentSeverity, string incidentUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a video-conference invite (join link for an incident's war-room)
    /// </summary>
    Task<bool> SendConferenceInviteAsync(string email, string incidentTitle, string incidentSeverity, string conferenceUrl, int durationMinutes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a status-page subscription confirmation (double opt-in).
    /// </summary>
    Task<bool> SendStatusPageSubscriptionConfirmationAsync(string email, string pageName, string confirmLink, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if email service is configured
    /// </summary>
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    /// <summary>Open a connection to the configured SMTP relay and authenticate; (false, message) when not
    /// configured or unreachable. Throttled by the caller.</summary>
    Task<(bool Ok, string Message)> ProbeAsync(CancellationToken cancellationToken = default);
}
