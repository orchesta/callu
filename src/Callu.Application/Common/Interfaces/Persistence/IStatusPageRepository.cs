using Callu.Domain.Entities;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// StatusPage-specific repository interface
/// </summary>
public interface IStatusPageRepository : IRepository<StatusPage>
{
    Task<StatusPage?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<StatusPage?> GetDetailByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<StatusPage?> GetDetailBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Public-safe: bypasses tenant filter, only returns IsPublic pages.</summary>
    Task<StatusPage?> GetBySlugPublicAsync(string slug, CancellationToken cancellationToken = default);
    Task<StatusPage?> GetDetailBySlugPublicAsync(string slug, CancellationToken cancellationToken = default);
}
