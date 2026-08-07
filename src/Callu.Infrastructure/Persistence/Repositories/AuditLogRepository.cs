using Callu.Application.Common.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Logging;
using Callu.Domain.Entities;
using Callu.Domain.Enums;

namespace Callu.Infrastructure.Persistence.Repositories;

/// <summary>
/// AuditLog repository implementation
/// </summary>
public class AuditLogRepository(ApplicationDbContext context, ILogger<AuditLogRepository> logger)
    : Repository<AuditLog>(context, logger), IAuditLogRepository
{
    public async Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, Guid entityId, int limit = 200, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(a => a.ResourceType == entityType && a.ResourceId == entityId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetByUserAsync(string userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(a => a.ActorId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetByActionAsync(AuditAction action, int limit = 200, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(a => a.Action == action)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
