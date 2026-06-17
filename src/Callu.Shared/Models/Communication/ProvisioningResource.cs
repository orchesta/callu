namespace Callu.Shared.Models.Communication;

/// <summary>
/// Individual resource status in provisioning
/// </summary>
public record ProvisioningResource(string Name, string Type, bool Exists, long? ResourceId = null);
