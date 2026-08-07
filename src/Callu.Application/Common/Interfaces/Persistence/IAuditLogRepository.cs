using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Application.Common.Interfaces.Persistence;

/// <summary>
/// AuditLog-specific repository interface
/// </summary>
public interface IAuditLogRepository : IRepository<AuditLog>
{
    Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, Guid entityId, int limit = 200, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetByUserAsync(string userId, int limit = 100, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetByActionAsync(AuditAction action, int limit = 200, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default);
}
