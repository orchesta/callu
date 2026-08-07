using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

public interface IUserPushDeviceRepository : IRepository<UserPushDevice>
{
    Task<UserPushDevice?> GetByTokenAsync(string pushToken, CancellationToken cancellationToken = default);

    /// <summary>The user's devices, most recently seen first.</summary>
    Task<IReadOnlyList<UserPushDevice>> GetActiveByUserIdAsync(
        string userId, CancellationToken cancellationToken = default);
}
