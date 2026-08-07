using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Audit;
using Callu.Shared.Results;

namespace Callu.Application.Services;

public interface IAuditLogService
{
    Task LogAsync(string? userId, AuditAction action, string entityName, string entityId, string? oldValues = null, string? newValues = null, string? description = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetLogsAsync(string? entityName = null, string? entityId = null, int count = 100, CancellationToken cancellationToken = default);

    /// <summary>The filtered, paged trail an auditor reads.</summary>
    Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogFilter filter, CancellationToken cancellationToken = default);

    /// <summary>The same trail as a stream, for an export that must not be held in memory.</summary>
    IAsyncEnumerable<AuditLogDto> StreamAsync(AuditLogFilter filter, CancellationToken cancellationToken = default);
}
