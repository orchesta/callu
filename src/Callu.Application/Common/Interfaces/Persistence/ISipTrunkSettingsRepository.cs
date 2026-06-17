using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// SipTrunkSettings-specific repository interface
/// </summary>
public interface ISipTrunkSettingsRepository : IRepository<SipTrunkSettings>
{
    Task<IEnumerable<SipTrunkSettings>> GetEnabledAsync(CancellationToken cancellationToken = default);

    Task<SipTrunkSettings?> GetByIdIgnoringFiltersNoTrackingAsync(Guid id, CancellationToken cancellationToken = default);
}
