using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

public interface IFirebaseSettingsRepository : IRepository<FirebaseSettings>
{
    Task<FirebaseSettings?> GetSettingsAsync(CancellationToken cancellationToken = default);
}
