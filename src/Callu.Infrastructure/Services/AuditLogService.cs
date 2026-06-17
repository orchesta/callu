using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence.Transactions;

namespace Callu.Infrastructure.Services;

public class AuditLogService(
    IAuditLogRepository auditLogRepo,
    IHttpContextAccessor httpContextAccessor,
    ITransactionManager transactionManager) : IAuditLogService
{
    public async Task LogAsync(
        string? userId,
        string action,
        string entityName,
        string entityId,
        string? oldValues = null,
        string? newValues = null,
        CancellationToken cancellationToken = default)
    {
        var http = httpContextAccessor.HttpContext;

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            string? description = null;
            if (!Enum.TryParse<AuditAction>(action, true, out var auditAction))
            {
                auditAction = AuditAction.Updated;
                description = action;
            }

            Guid? entityGuid = null;
            if (Guid.TryParse(entityId, out var parsedGuid))
            {
                entityGuid = parsedGuid;
            }

            var log = new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                UserName = Truncate(http?.User?.Identity?.Name, 100),
                Action = auditAction,
                EntityType = entityName,
                EntityId = entityGuid,
                Description = Truncate(description, 500),
                OldValues = oldValues,
                NewValues = newValues,
                IpAddress = Truncate(http?.Connection?.RemoteIpAddress?.ToString(), 50),
                UserAgent = Truncate(http?.Request.Headers.UserAgent.ToString(), 500),
                RequestPath = Truncate(http?.Request.Path.Value, 500),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await auditLogRepo.AddAsync(log, cancellationToken);
        }, cancellationToken);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    public async Task<IEnumerable<AuditLog>> GetLogsAsync(
        string? entityName = null, 
        string? entityId = null, 
        int count = 100, 
        CancellationToken cancellationToken = default)
    {
        var query = auditLogRepo.GetQueryable().AsNoTracking();

        if (!string.IsNullOrEmpty(entityName))
        {
            query = query.Where(l => l.EntityType == entityName);
        }

        if (!string.IsNullOrEmpty(entityId) && Guid.TryParse(entityId, out var guid))
        {
            query = query.Where(l => l.EntityId == guid);
        }

        return await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
}
