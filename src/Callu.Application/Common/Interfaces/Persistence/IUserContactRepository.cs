using Callu.Application.Common.Models.Persistence;

namespace Callu.Application.Common.Interfaces.Persistence;

public interface IUserContactRepository
{
    Task<IReadOnlyList<UserContactSnapshot>> GetContactsByIdsAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);

    Task<UserContactSnapshot?> GetContactByIdAsync(string userId, CancellationToken cancellationToken = default);
}
