using Callu.Application.Common.Models.Persistence;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>Identity-backed user listing for tenant admin screens.</summary>
public interface ITenantUserReadRepository
{
    Task<IReadOnlyList<TenantUserDirectoryRow>> GetDirectoryUsersAsync(CancellationToken cancellationToken = default);
}
