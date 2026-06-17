namespace Callu.Domain.Enums;

/// <summary>
/// Integration mode determining feature set and requirements
/// </summary>
public enum IntegrationMode
{
    /// <summary>
    /// Webhook Only - receive alerts via webhook, no API connection required
    /// </summary>
    WebhookOnly = 0,

    /// <summary>
    /// Full API - bidirectional sync with polling and acknowledgement feedback
    /// </summary>
    FullApi = 1,

    /// <summary>
    /// Auto-Setup - Full API plus automatic remote system configuration
    /// </summary>
    AutoSetup = 2
}
