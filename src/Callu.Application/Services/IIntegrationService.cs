using Callu.Shared.Models.Integrations;

namespace Callu.Application.Services;

/// <summary>
/// Manages inbound integrations — one named webhook endpoint per external monitor, feeding one service.
/// </summary>
public interface IIntegrationService
{
    Task<IReadOnlyList<IntegrationDto>> GetAllAsync(Guid? serviceId = null, CancellationToken cancellationToken = default);
    Task<IntegrationDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates the integration and mints its token + API key; the secrets are returned once.</summary>
    Task<IntegrationSecretsDto> CreateAsync(CreateIntegrationRequest request, CancellationToken cancellationToken = default);

    Task<IntegrationDto> UpdateAsync(Guid id, UpdateIntegrationRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Mints a new token and API key, invalidating the previous pair immediately.</summary>
    Task<IntegrationSecretsDto> RotateCredentialsAsync(Guid id, CancellationToken cancellationToken = default);
}
