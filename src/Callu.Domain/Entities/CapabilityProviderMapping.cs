using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// Maps a capability to a specific provider for routing
/// </summary>
public class CapabilityProviderMapping : BaseEntity
{
    /// <summary>
    /// The capability being mapped
    /// </summary>
    public CommunicationCapability Capability { get; set; }
    
    /// <summary>
    /// The provider to use for this capability
    /// </summary>
    public Guid ProviderId { get; set; }
    public CommunicationProvider Provider { get; set; } = null!;
    
    /// <summary>
    /// Priority for fallback chain (lower = higher priority)
    /// </summary>
    public int Priority { get; set; } = 0;
    
    /// <summary>
    /// Whether this mapping is active
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
