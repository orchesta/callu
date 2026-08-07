using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// SIP trunk configuration (shared across multiple providers)
/// </summary>
public class SipTrunkSettings : BaseEntity
{
    /// <summary>
    /// Display name for this SIP trunk
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// SIP server hostname (e.g., sip.verimor.com.tr)
    /// </summary>
    [Required]
    [StringLength(255)]
    public string Server { get; set; } = string.Empty;
    
    /// <summary>
    /// SIP server port (default 5060 for UDP, 5061 for TLS)
    /// </summary>
    public int Port { get; set; } = 5060;
    
    /// <summary>
    /// SIP authentication username
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Username { get; set; } = string.Empty;
    
    /// <summary>
    /// SIP authentication password (stored encrypted)
    /// </summary>
    [StringLength(500)]
    public string? Password { get; set; }
    
    /// <summary>
    /// Optional separate auth user (if different from username)
    /// </summary>
    [StringLength(100)]
    public string? AuthUser { get; set; }
    
    /// <summary>
    /// Caller ID for outbound calls
    /// </summary>
    [StringLength(50)]
    public string? CallerId { get; set; }
    
    /// <summary>
    /// Display name for outbound calls
    /// </summary>
    [StringLength(100)]
    public string? DisplayName { get; set; }
    
    /// <summary>
    /// Use TLS transport (sips:)
    /// </summary>
    public bool UseTls { get; set; }
    
    /// <summary>
    /// Use TCP transport instead of UDP
    /// </summary>
    public bool UseTcp { get; set; }
    
    /// <summary>
    /// Whether this trunk is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// Providers using this trunk
    /// </summary>
    public ICollection<CommunicationProvider> Providers { get; set; } = new List<CommunicationProvider>();
}
