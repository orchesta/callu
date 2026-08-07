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

/// <summary>Reports a voice page the provider accepted for which no call was ever recorded.</summary>
// A provider answering "accepted" is not a phone ringing. Every record of what the call did comes from
// the provider's callback, so a callback that never arrives leaves the page looking delivered with
// nothing on the incident at all — and the stuck sweep cannot help, because it reads call rows and
// there are none. Deliberately does not re-dial: the call may well have happened and only the report
// been lost, and ringing a responder again on that guess is worse than telling an operator.
[DisallowConcurrentExecution]
public sealed class UnconfirmedVoiceCallSweepQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<UnconfirmedVoiceCallSweepQuartzJob> logger)
    : IJob
{
    private const int BatchSize = 25;

    /// <summary>Comfortably past a dial-out: the first callback fires when the phone starts ringing.</summary>
    internal static readonly TimeSpan ConfirmationGracePeriod = TimeSpan.FromMinutes(10);

    /// <summary>How far back a page is still worth reporting.</summary>
    // Long enough to outlast a Worker that was down overnight, short enough that the first run after
    // an upgrade reports recent pages rather than walking the whole history.
    internal static readonly TimeSpan Lookback = TimeSpan.FromHours(24);

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        using var correlation = AuditCorrelationScope.Begin($"unconfirmed-voice-call-sweep:{context.FireInstanceId}");

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var now = DateTime.UtcNow;
        var cutoff = now.Subtract(ConfirmationGracePeriod);
        var oldest = now.Subtract(Lookback);

        var candidates = db.Set<Notification>()
            .Where(n => n.Type == NotificationType.VoiceCall
                        && n.DeliveryStatus == NotificationDeliveryStatus.Delivered
                        && n.UnconfirmedCallReportedAt == null
                        && n.IncidentId != null
                        && n.LastAttemptAt != null
                        && n.LastAttemptAt <= cutoff
                        && n.LastAttemptAt > oldest
                        && !db.CallLogs.Any(c => c.AttemptId == n.Id));

        var unconfirmed = await candidates
            .OrderBy(n => n.LastAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (unconfirmed.Count == 0) return;

        // Counted separately when the batch fills, so an operator reads how many pages are unconfirmed
        // rather than how many this run happened to take.
        var total = unconfirmed.Count < BatchSize
            ? unconfirmed.Count
            : await candidates.CountAsync(ct);

        logger.LogError(
            "UnconfirmedVoiceCallSweep: {Count} voice page(s) were accepted by the provider more than {Grace} ago "
            + "and no call has ever been recorded for them; {Batch} are being reported this run. Nobody is confirmed "
            + "to have been called — check the voice provider's callback configuration.",
            total, ConfirmationGracePeriod, unconfirmed.Count);

        foreach (var page in unconfirmed)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ReportAsync(db, audit, page, ct);
            }
            catch (Exception ex) when (!IsShutdown(ex, ct))
            {
                logger.LogError(ex,
                    "UnconfirmedVoiceCallSweep: could not report the unconfirmed page {NotificationId}", page.Id);
            }
        }
    }

    private async Task ReportAsync(
        ApplicationDbContext db, IAuditLogService audit, Notification page, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        page.UnconfirmedCallReportedAt = now;
        page.UpdatedAt = now;

        db.Set<IncidentTimelineEvent>().Add(new IncidentTimelineEvent
        {
            IncidentId = page.IncidentId!.Value,
            EventType = TimelineEventType.CallFailed,
            Title = "Voice call never confirmed",
            Description =
                "The voice provider accepted this call but has never reported anything about it, so there is no "
                + "record that the phone rang and nobody is confirmed to have been reached. The call may still "
                + "have gone out — no further call was placed on that chance. Check the voice provider's callback "
                + "address (Settings → Communications), and page someone by hand if this incident is still open.",
            ActorUserId = "system",
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);

        // Outside the commit and swallowed: a failing audit sink must not undo the report the timeline
        // already carries, nor cause the same page to be reported again on the next run.
        try
        {
            await audit.LogAsync(
                userId: null,
                AuditAction.VoiceCallNeverConfirmed,
                "Incident",
                page.IncidentId!.Value.ToString(),
                oldValues: null,
                newValues: $"Notification: {page.Id}",
                description:
                    $"A voice page to user {page.UserId} was accepted by the provider but no call was ever recorded "
                    + "for it; nobody is confirmed to have been reached.",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "UnconfirmedVoiceCallSweep: could not write the audit row for page {NotificationId}; the timeline "
                + "event was still applied", page.Id);
        }
    }

    private static bool IsShutdown(Exception ex, CancellationToken ct) =>
        ex is OperationCanceledException && ct.IsCancellationRequested;
}
