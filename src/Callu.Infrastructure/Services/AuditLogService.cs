using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Application.Services;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Audit;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Models.Audit;
using Callu.Shared.Results;
using Callu.Shared;
using System.Runtime.CompilerServices;

namespace Callu.Infrastructure.Services;

public class AuditLogService(
    IAuditLogRepository auditLogRepo,
    IHttpContextAccessor httpContextAccessor,
    ITransactionManager transactionManager,
    IEnumerable<IAuditSink>? sinks = null,
    ILogger<AuditLogService>? logger = null,
    IUserContactRepository? userContacts = null) : IAuditLogService
{
    /// <summary>Who the actor was, by name, recorded as it stood at the time.</summary>
    // The signed-in name covers the panel. A keypress on a call arrives on a token-authenticated
    // callback with no signed-in user, so without the lookup those rows — the ones an auditor most
    // wants to read — show a bare identifier.
    private async Task<string?> ActorNameAsync(string? userId, HttpContext? http, CancellationToken cancellationToken)
    {
        if (http?.User?.Identity?.Name is { Length: > 0 } signedIn) return signedIn;
        if (userId is not { Length: > 0 } || userContacts is null) return null;

        try
        {
            return (await userContacts.GetContactByIdAsync(userId, cancellationToken))?.DisplayName;
        }
        catch (Exception ex)
        {
            // A name is worth having, never worth losing the audit row for.
            logger?.LogWarning(ex, "Could not resolve a display name for actor {ActorId}; recording the id alone", userId);
            return null;
        }
    }

    public async Task LogAsync(
        string? userId,
        AuditAction action,
        string entityName,
        string entityId,
        string? oldValues = null,
        string? newValues = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var http = httpContextAccessor.HttpContext;
        AuditLog? written = null;

        // Resolved before the transaction opens. Inside one, a failed read aborts the transaction on
        // Postgres, so catching it would not save the row it was meant to protect.
        var actorName = await ActorNameAsync(userId, http, cancellationToken);

        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            Guid? resourceGuid = null;
            if (Guid.TryParse(entityId, out var parsedGuid))
            {
                resourceGuid = parsedGuid;
            }

            var trace = AuditTraceContext.Current();
            var log = new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = userId,
                ActorDisplayName = Truncate(actorName, 100),
                ActorType = userId is null ? AuditActorType.System : AuditActorType.User,
                Action = action,
                EventName = AuditEventNaming.ToEventName(entityName, action),
                EventCategory = AuditEventNaming.ToEventCategory(entityName),
                Outcome = AuditEventNaming.ToOutcome(action),
                ResourceType = entityName,
                ResourceId = resourceGuid,
                Summary = Truncate(description, 500),
                ChangeBefore = oldValues,
                ChangeAfter = newValues,
                RequestIpAddress = Truncate(http?.Connection?.RemoteIpAddress?.ToString(), 50),
                RequestUserAgent = Truncate(http?.Request.Headers.UserAgent.ToString(), 500),
                RequestRoute = Truncate(http?.Request.Path.Value, 500),
                RequestId = Truncate(http?.TraceIdentifier, AuditLog.MaxCorrelationIdLength),
                CorrelationId = Truncate(AuditCorrelationScope.CorrelationId, AuditLog.MaxCorrelationIdLength),
                TraceId = trace.TraceId,
                SpanId = trace.SpanId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await auditLogRepo.AddAsync(log, cancellationToken);
            written = log;
        }, cancellationToken);

        if (sinks is null || written is null) return;

        // The table is the record; these are copies for whatever the operator ships logs with. One
        // of them failing must never be the reason an audited operation fails, and must not stop
        // the others.
        foreach (var sink in sinks)
        {
            try
            {
                sink.Emit(written);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex,
                    "Audit sink {Sink} did not accept entry {AuditId}", sink.GetType().Name, written.Id);
            }
        }
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
            query = query.Where(l => l.ResourceType == entityName);
        }

        if (!string.IsNullOrEmpty(entityId) && Guid.TryParse(entityId, out var guid))
        {
            query = query.Where(l => l.ResourceId == guid);
        }

        return await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<AuditLogDto>> SearchAsync(
        AuditLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, AppConstants.Pagination.MaxPageSize);

        var query = Filtered(filter);

        var total = await query.CountAsync(cancellationToken);

        var items = await Ordered(query, filter.SortAscending)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new AuditLogDto
            {
                Id = l.Id,
                CreatedAt = l.CreatedAt,
                ActorId = l.ActorId,
                ActorDisplayName = l.ActorDisplayName,
                ActorType = l.ActorType,
                Action = l.Action,
                EventName = l.EventName,
                EventCategory = l.EventCategory,
                Outcome = l.Outcome,
                ResourceType = l.ResourceType,
                ResourceId = l.ResourceId,
                Summary = l.Summary,
                ChangeBefore = l.ChangeBefore,
                ChangeAfter = l.ChangeAfter,
                RequestIpAddress = l.RequestIpAddress,
                RequestUserAgent = l.RequestUserAgent,
                RequestRoute = l.RequestRoute,
                RequestId = l.RequestId,
                CorrelationId = l.CorrelationId,
                TraceId = l.TraceId,
                SpanId = l.SpanId,
                Sequence = l.Sequence,
                PrevHash = l.PrevHash,
                RowHash = l.RowHash,
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>(items, total, page, pageSize);
    }

    /// <summary>The order both the paged read and the export apply, so the two cannot drift.</summary>
    // Id breaks the tie: two rows in the same millisecond would otherwise swap between pages.
    private static IQueryable<AuditLog> Ordered(IQueryable<AuditLog> query, bool ascending) =>
        ascending
            ? query.OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            : query.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id);

    /// <summary>The filter both the paged read and the export apply, so the two cannot drift.</summary>
    private IQueryable<AuditLog> Filtered(AuditLogFilter filter)
    {
        var query = auditLogRepo.GetQueryable().AsNoTracking();

        if (filter.From is { } from) query = query.Where(l => l.CreatedAt >= from);
        if (filter.To is { } to) query = query.Where(l => l.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(filter.ActorId)) query = query.Where(l => l.ActorId == filter.ActorId);
        if (filter.Action is { } action) query = query.Where(l => l.Action == action);
        if (!string.IsNullOrWhiteSpace(filter.ResourceType)) query = query.Where(l => l.ResourceType == filter.ResourceType);
        if (filter.ResourceTypes is { Count: > 0 } types) query = query.Where(l => types.Contains(l.ResourceType));
        if (filter.ResourceId is { } resourceId) query = query.Where(l => l.ResourceId == resourceId);
        if (!string.IsNullOrWhiteSpace(filter.RequestIpAddress)) query = query.Where(l => l.RequestIpAddress == filter.RequestIpAddress);

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var q = filter.Query.Trim();
            query = query.Where(l =>
                (l.ActorDisplayName != null && EF.Functions.ILike(l.ActorDisplayName, $"%{q}%"))
                || (l.Summary != null && EF.Functions.ILike(l.Summary, $"%{q}%"))
                || (l.RequestRoute != null && EF.Functions.ILike(l.RequestRoute, $"%{q}%")));
        }

        return query;
    }

    private static AuditLogDto ToDto(AuditLog l) => new()
    {
        Id = l.Id,
        CreatedAt = l.CreatedAt,
        ActorId = l.ActorId,
        ActorDisplayName = l.ActorDisplayName,
        ActorType = l.ActorType,
        Action = l.Action,
        EventName = l.EventName,
        EventCategory = l.EventCategory,
        Outcome = l.Outcome,
        ResourceType = l.ResourceType,
        ResourceId = l.ResourceId,
        Summary = l.Summary,
        ChangeBefore = l.ChangeBefore,
        ChangeAfter = l.ChangeAfter,
        RequestIpAddress = l.RequestIpAddress,
        RequestUserAgent = l.RequestUserAgent,
        RequestRoute = l.RequestRoute,
        RequestId = l.RequestId,
        CorrelationId = l.CorrelationId,
        TraceId = l.TraceId,
        SpanId = l.SpanId,
        Sequence = l.Sequence,
        PrevHash = l.PrevHash,
        RowHash = l.RowHash,
        CanonicalizationVersion = l.CanonicalizationVersion,
    };

    /// <summary>How many rows one round trip fetches while streaming an export.</summary>
    private const int ExportBatchSize = 500;

    public async IAsyncEnumerable<AuditLogDto> StreamAsync(
        AuditLogFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Keyset, not Skip: an export can run to the end of a five-year trail, and OFFSET makes the
        // last pages walk everything before them. Nothing here materialises the whole result.
        DateTime? lastCreatedAt = null;
        Guid? lastId = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var page = Filtered(filter);

            if (lastCreatedAt is { } cursorAt && lastId is { } cursorId)
            {
                page = filter.SortAscending
                    ? page.Where(l => l.CreatedAt > cursorAt || (l.CreatedAt == cursorAt && l.Id.CompareTo(cursorId) > 0))
                    : page.Where(l => l.CreatedAt < cursorAt || (l.CreatedAt == cursorAt && l.Id.CompareTo(cursorId) < 0));
            }

            var batch = await Ordered(page, filter.SortAscending)
                .Take(ExportBatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0) yield break;

            foreach (var row in batch)
                yield return ToDto(row);

            if (batch.Count < ExportBatchSize) yield break;

            lastCreatedAt = batch[^1].CreatedAt;
            lastId = batch[^1].Id;
        }
    }
}
