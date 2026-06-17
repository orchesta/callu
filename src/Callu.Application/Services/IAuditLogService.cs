using Callu.Domain.Entities;

namespace Callu.Application.Services;

public interface IAuditLogService
{
    Task LogAsync(string? userId, string action, string entityName, string entityId, string? oldValues = null, string? newValues = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetLogsAsync(string? entityName = null, string? entityId = null, int count = 100, CancellationToken cancellationToken = default);
}
