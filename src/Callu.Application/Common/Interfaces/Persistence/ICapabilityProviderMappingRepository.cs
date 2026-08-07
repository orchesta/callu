using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// CapabilityProviderMapping-specific repository interface
/// </summary>
public interface ICapabilityProviderMappingRepository : IRepository<CapabilityProviderMapping>
{
    Task<CapabilityProviderMapping?> GetByCapabilityAsync(CommunicationCapability capability, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CapabilityProviderMapping>> ListEnabledForRegistryReloadAsync(CancellationToken cancellationToken = default);
}
