using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Enums;
using Callu.Shared.Models.Communication;
using Callu.Shared.Models.Incidents;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Service for querying call logs and incident timeline events.
/// </summary>
public class CallLogService(
    ICallLogRepository callLogRepo,
    IIncidentTimelineEventRepository timelineRepo,
    ITransactionManager transactionManager) : ICallLogService
{
    /// <inheritdoc />
    public async Task<IEnumerable<CallLogDto>> GetCallLogsByIncidentIdAsync(
        Guid incidentId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var callLogs = await callLogRepo.GetQueryable()
                .AsNoTracking()
                .Where(cl => cl.IncidentId == incidentId)
                .OrderByDescending(cl => cl.InitiatedAt)
                .Select(cl => new CallLogDto
                {
                    Id = cl.Id,
                    IncidentId = cl.IncidentId,
                    PhoneNumber = cl.PhoneNumber,
                    CalledPersonName = cl.CalledPersonName,
                    Status = cl.Status.ToString(),
                    DurationSeconds = cl.DurationSeconds,
                    AttemptNumber = cl.AttemptNumber,
                    FailureReason = cl.FailureReason,
                    InitiatedAt = cl.InitiatedAt,
                    CompletedAt = cl.CompletedAt
                })
                .ToListAsync(cancellationToken);
            return (IEnumerable<CallLogDto>)callLogs;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<(IEnumerable<CallLogDto> Items, int TotalCount)> GetCallLogsPagedAsync(
        int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var query = callLogRepo.GetQueryable()
                .Include(cl => cl.Incident)
                .AsNoTracking()
                .OrderByDescending(cl => cl.InitiatedAt);

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(cl => new CallLogDto
                {
                    Id = cl.Id,
                    IncidentId = cl.IncidentId,
                    IncidentTitle = cl.Incident != null ? cl.Incident.Title : "",
                    PhoneNumber = cl.PhoneNumber,
                    CalledPersonName = cl.CalledPersonName,
                    Status = cl.Status.ToString(),
                    DurationSeconds = cl.DurationSeconds,
                    AttemptNumber = cl.AttemptNumber,
                    FailureReason = cl.FailureReason,
                    InitiatedAt = cl.InitiatedAt,
                    CompletedAt = cl.CompletedAt
                })
                .ToListAsync(cancellationToken);

            return ((IEnumerable<CallLogDto>)items, totalCount);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<IncidentTimelineEventDto>> GetTimelineEventsAsync(
        Guid incidentId, CancellationToken cancellationToken = default)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var events = await timelineRepo.GetQueryable()
                .AsNoTracking()
                .Where(e => e.IncidentId == incidentId)
                .OrderBy(e => e.CreatedAt)
                .Select(e => new IncidentTimelineEventDto
                {
                    Id = e.Id,
                    IncidentId = e.IncidentId,
                    EventType = e.EventType.ToString(),
                    Title = e.Title,
                    Description = e.Description,
                    ActorName = e.ActorName,
                    CreatedAt = e.CreatedAt
                })
                .ToListAsync(cancellationToken);
            return (IEnumerable<IncidentTimelineEventDto>)events;
        }, cancellationToken);
    }
}
