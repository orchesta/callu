using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Configured communication provider instance (Voximplant, Verimor, etc.)
/// </summary>
public class CommunicationProvider : BaseEntity
{
    /// <summary>
    /// Display name for this provider configuration
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Provider type identifier (voximplant, verimor, twilio)
    /// </summary>
    [Required]
    [StringLength(50)]
    public string ProviderType { get; set; } = string.Empty;
    
    /// <summary>
    /// Capabilities this provider supports
    /// </summary>
    public CommunicationCapability Capabilities { get; set; }
    
    /// <summary>
    /// Provider-specific configuration as JSON. Secret values inside (api_key, service-account
    /// JSON, scenario key, SMS credentials) are DataProtection-encrypted at rest via
    /// ProviderSecretProtector; non-secret fields stay plaintext. Sized to fit an encrypted
    /// service-account JSON (PEM RSA key) plus the rest of the config.
    /// </summary>
    [StringLength(16000)]
    public string? ConfigJson { get; set; }
    
    /// <summary>
    /// Associated SIP trunk (optional, for providers using external SIP)
    /// </summary>
    public Guid? SipTrunkId { get; set; }
    public SipTrunkSettings? SipTrunk { get; set; }
    
    /// <summary>
    /// Whether this provider is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// Priority for capability routing (lower = higher priority)
    /// </summary>
    public int Priority { get; set; } = 0;
    
    /// <summary>
    /// Last connection test timestamp
    /// </summary>
    public DateTime? LastTestedAt { get; set; }
    
    /// <summary>
    /// Last test result message
    /// </summary>
    [StringLength(500)]
    public string? LastTestResult { get; set; }
}
