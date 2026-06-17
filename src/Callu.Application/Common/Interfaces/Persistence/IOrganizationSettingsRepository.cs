using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// Repository for the single-row OrganizationSettings table.
/// </summary>
public interface IOrganizationSettingsRepository : IRepository<OrganizationSettings>
{
    Task<OrganizationSettings?> GetSettingsAsync(CancellationToken cancellationToken = default);
}
