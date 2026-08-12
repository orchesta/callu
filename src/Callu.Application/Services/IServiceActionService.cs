using Callu.Shared.Models.Services;

namespace Callu.Application.Services;

/// <summary>Manages the operator-defined outbound actions of a service.</summary>
public interface IServiceActionService
{
    Task<List<ServiceActionDto>> GetForServiceAsync(Guid serviceId, bool includeSensitive, CancellationToken cancellationToken = default);
    Task<ServiceActionDto> CreateAsync(Guid serviceId, CreateServiceActionRequest request, CancellationToken cancellationToken = default);
    Task<ServiceActionDto> UpdateAsync(Guid serviceId, Guid actionId, UpdateServiceActionRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid serviceId, Guid actionId, CancellationToken cancellationToken = default);
}
