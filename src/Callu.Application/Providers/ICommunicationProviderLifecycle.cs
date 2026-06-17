using Callu.Shared.Models.Communication;

namespace Callu.Application.Providers;

/// <summary>
/// Provider-specific lifecycle management.
/// Each provider type implements this to handle provisioning and user sync.
/// Providers that don't support certain operations (e.g., Twilio has no user concept) 
/// simply return no-op results.
/// </summary>
public interface ICommunicationProviderLifecycle
{
    /// <summary>
    /// Provider type identifier (voximplant, verimor, twilio)
    /// </summary>
    string ProviderType { get; }
    
    /// <summary>
    /// Initial setup: create application, scenarios, rules, system user.
    /// Idempotent — skips resources that already exist.
    /// </summary>
    Task<ProvisioningResult> ProvisionAsync(Guid providerId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check current provisioning status
    /// </summary>
    Task<ProvisioningStatus> GetStatusAsync(Guid providerId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Team member added — provider-specific action (Vox: create user, Twilio: no-op)
    /// </summary>
    Task OnTeamMemberAddedAsync(Guid providerId, string userId, string displayName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Team member removed — provider-specific action (Vox: delete user, Twilio: no-op)
    /// </summary>
    Task OnTeamMemberRemovedAsync(Guid providerId, string userId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Bulk sync: compare all Callu team members with provider users, add missing, remove extras
    /// </summary>
    Task<SyncResult> SyncUsersAsync(Guid providerId, CancellationToken cancellationToken = default);
}
