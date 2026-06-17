namespace Callu.Shared.Models.Communication;

/// <summary>
/// Current provisioning status for a provider
/// </summary>
public record ProvisioningStatus
{
    public bool IsProvisioned { get; init; }
    public VoxAccountInfoDto? AccountInfo { get; init; }
    public List<ProvisioningResource> Resources { get; init; } = new();
    public int ProviderUserCount { get; init; }
    public int CalluUserCount { get; init; }
    public bool UsersInSync { get; init; }
    public List<string> Issues { get; init; } = new();
}
