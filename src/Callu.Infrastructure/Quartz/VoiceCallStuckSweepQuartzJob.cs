using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Audit;
using Callu.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Sweeps a <see cref="CallLog"/> stuck non-terminal past its grace period into failed, with a fresh attempt armed.</summary>
[DisallowConcurrentExecution]
public sealed class VoiceCallStuckSweepQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<VoiceCallStuckSweepQuartzJob> logger)
    : IJob
{
    private const int BatchSize = 25;

    /// <summary>Five minutes, comfortably past a 120s call plus its ~36s callback-delivery budget.</summary>
    private static readonly TimeSpan StuckGracePeriod = TimeSpan.FromMinutes(5);

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        using var correlation = AuditCorrelationScope.Begin($"voice-call-stuck-sweep:{context.FireInstanceId}");

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var cutoff = DateTime.UtcNow.Subtract(StuckGracePeriod);
        var stuck = await db.CallLogs
            .Where(c => c.CompletedAt == null &&
                        c.NextRetryAt == null &&
                        c.InitiatedAt <= cutoff &&
                        !c.IsDeleted)
            .OrderBy(c => c.InitiatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (stuck.Count == 0) return;

        logger.LogWarning(
            "VoiceCallStuckSweep: {Count} call(s) never reported a terminal status within {Grace}",
            stuck.Count, StuckGracePeriod);

        foreach (var callLog in stuck)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ResolveAsync(db, audit, callLog, ct);
            }
            catch (Exception ex) when (!IsShutdown(ex, ct))
            {
                logger.LogError(ex, "VoiceCallStuckSweep: could not resolve stuck CallLog {CallLogId}", callLog.Id);
            }
        }
    }

    private async Task ResolveAsync(
        ApplicationDbContext db, IAuditLogService audit, CallLog callLog, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var lastStatus = callLog.Status;
        var recipient = Recipient(callLog);

        callLog.Status = CallStatus.Failed;
        callLog.CompletedAt = now;
        callLog.FailureReason = $"No terminal callback was ever received (last known status: {lastStatus})";
        callLog.NextRetryAt = now;
        callLog.DialOutFailingSince = null;
        callLog.DialOutFailureKind = null;
        callLog.UpdatedAt = now;

        db.Set<IncidentTimelineEvent>().Add(new IncidentTimelineEvent
        {
            IncidentId = callLog.IncidentId,
            EventType = TimelineEventType.CallFailed,
            Title = "Voice call lost track",
            Description =
                $"The call to {recipient} never reported a final outcome (last known: {lastStatus}) — the callback "
                + "may have been lost to a restart or network interruption. Nobody is recorded as reached on this "
                + "attempt, and a further attempt has been armed.",
            ActorUserId = "system"
        });

        await db.SaveChangesAsync(ct);

        // Outside the same commit and swallowed on failure: a failing audit sink must not undo the
        // fresh attempt the row was just armed for. The timeline event above already carries the fact.
        try
        {
            await audit.LogAsync(
                userId: null,
                AuditAction.VoiceCallLost,
                "Incident",
                callLog.IncidentId.ToString(),
                oldValues: $"Status: {lastStatus}",
                newValues: $"Status: {CallStatus.Failed}",
                description: $"Voice call to {recipient} never reported a final status; marked failed and a further attempt armed.",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "VoiceCallStuckSweep: could not write the audit row for CallLog {CallLogId}; the timeline event and the fresh attempt were still applied",
                callLog.Id);
        }
    }

    private static string Recipient(CallLog callLog) =>
        string.IsNullOrWhiteSpace(callLog.CalledPersonName)
            ? callLog.PhoneNumber
            : $"{callLog.CalledPersonName} ({callLog.PhoneNumber})";

    private static bool IsShutdown(Exception ex, CancellationToken ct) =>
        ex is OperationCanceledException && ct.IsCancellationRequested;
}
