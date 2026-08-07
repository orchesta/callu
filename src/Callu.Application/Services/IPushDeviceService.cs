using Callu.Shared.Models.Devices;

namespace Callu.Application.Services;

public interface IPushDeviceService
{
    Task<PushDeviceDto> RegisterAsync(
        string userId, RegisterPushDeviceRequest request, CancellationToken cancellationToken = default);

    Task UnregisterAsync(
        string userId, UnregisterPushDeviceRequest? request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PushDeviceDto>> ListAsync(string userId, CancellationToken cancellationToken = default);
}
