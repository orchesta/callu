namespace Callu.Infrastructure.Configuration;

/// <summary>
/// Binds to the <c>CommunicationSettings</c> configuration section.
/// </summary>
public class CommunicationSettingsOptions
{
    public const string SectionName = "CommunicationSettings";

    /// <summary>
    /// Dev-only: relax TLS checks for provider HTTP clients.
    /// </summary>
    public bool DisableSslValidation { get; set; }

    /// <summary>
    /// Optional platform SIP trunk (SipTrunkSettings row id).
    /// Used when a voice-capable communication provider has no trunk linked.
    /// </summary>
    public Guid? SystemSipTrunkId { get; set; }

    /// <summary>Allows the HTTP-SMS provider to reach a private/loopback address; off by default.</summary>
    public bool AllowPrivateSmsEndpoint { get; set; }

    /// <summary>Allows notification webhook dispatch to target a private-network address; off by default.</summary>
    public bool AllowPrivateWebhookEndpoint { get; set; }

    /// <summary>Allows status-page uptime checks to probe a private-network address; off by default.</summary>
    public bool AllowPrivateHealthCheckEndpoint { get; set; }
}
