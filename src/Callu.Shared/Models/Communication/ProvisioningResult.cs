namespace Callu.Shared.Models.Communication;

/// <summary>
/// Result of a provisioning operation (creating app, scenarios, rules, system user)
/// </summary>
public record ProvisioningResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public List<string> CreatedResources { get; init; } = new();
    public List<string> ExistingResources { get; init; } = new();
}
