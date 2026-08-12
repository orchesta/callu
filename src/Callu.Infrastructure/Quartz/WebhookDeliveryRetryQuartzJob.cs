using Callu.Application.Plugins;
using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Infrastructure.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Re-fires due <see cref="WebhookDelivery"/> retries, claiming each row before dispatch so a
/// slow ACK cannot fire twice; at most one row per (incident, ack type) chain may be armed.</summary>
[DisallowConcurrentExecution]
public sealed class WebhookDeliveryRetryQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookDeliveryRetryQuartzJob> logger)
    : IJob
{
    private const int BatchSize = 50;

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;
        var dueRows = await db.Set<WebhookDelivery>()
            .Where(d => d.Status == WebhookDeliveryStatus.Retrying &&
                        d.NextRetryAt != null &&
                        d.NextRetryAt <= now)
            .OrderBy(d => d.NextRetryAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (dueRows.Count == 0) return;

        logger.LogInformation("WebhookDeliveryRetry processing {Count} rows", dueRows.Count);

        foreach (var row in dueRows)
        {
            if (ct.IsCancellationRequested) break;

            if (string.IsNullOrEmpty(row.AckType))
            {
                CloseOut(row, "Retry row has no AckType");
                await TrySaveAsync(db, row, ct);
                continue;
            }

            if (row.AttemptCount >= IncidentEventDispatcher.MaxAttempts)
            {
                CloseOut(row, reason: null);
                if (await TrySaveAsync(db, row, ct))
                    await WriteChainStoppedTraceAsync(scope.ServiceProvider, db, row, ct);
                continue;
            }

            // A newer attempt row for this chain exists, so this row lost ownership — its close-out
            // never committed. Dispatching from here as well would fork the chain.
            if (await SupersededAsync(db, row, ct))
            {
                logger.LogWarning(
                    "WebhookDeliveryRetry: delivery {DeliveryId} (incident {IncidentId}) was superseded by a newer attempt; "
                    + "closing it out instead of dispatching a duplicate ACK",
                    row.Id, row.IncidentId);
                CloseOut(row, "Superseded by a newer delivery attempt for the same ACK");
                await TrySaveAsync(db, row, ct);
                continue;
            }

            var attemptCountBeforeClaim = row.AttemptCount;
            var attemptedAtBeforeClaim = row.AttemptedAt;

            Claim(row);
            if (!await TrySaveAsync(db, row, ct))
                continue;

            AckDispatchOutcome outcome;
            try
            {
                using var dispatchScope = scopeFactory.CreateScope();
                var dispatcher = dispatchScope.ServiceProvider.GetRequiredService<IIncidentEventDispatcher>();
                outcome = await dispatcher.SendServiceAckAsync(row.IncidentId, row.AckType, ct);
            }
            catch (Exception ex) when (!IsShutdown(ex, ct))
            {
                logger.LogError(ex, "Retry dispatch threw for incident {IncidentId}", row.IncidentId);
                NoteUnrecorded(row, ex.Message);
                if (await TrySaveAsync(db, row, ct) && row.NextRetryAt is null)
                    await WriteChainStoppedTraceAsync(scope.ServiceProvider, db, row, ct);
                continue;
            }

            switch (outcome)
            {
                case AckDispatchOutcome.Recorded:
                    row.AttemptCount = attemptCountBeforeClaim;
                    row.AttemptedAt = attemptedAtBeforeClaim;
                    CloseOut(row, reason: null);
                    await TrySaveAsync(db, row, ct);
                    break;

                case AckDispatchOutcome.Skipped:
                    NoteUnrecorded(row, "ACK is not configured for this service");
                    if (await TrySaveAsync(db, row, ct) && row.NextRetryAt is null)
                        await WriteChainStoppedTraceAsync(scope.ServiceProvider, db, row, ct);
                    break;

                default:
                    NoteUnrecorded(row, "Retry attempt could not be recorded");
                    if (await TrySaveAsync(db, row, ct) && row.NextRetryAt is null)
                        await WriteChainStoppedTraceAsync(scope.ServiceProvider, db, row, ct);
                    break;
            }
        }
    }

    /// <summary>Whether a newer attempt row exists for the same chain, in which case that row owns it and
    /// this one is done; newness is decided by <c>CreatedAt</c>.</summary>
    private static Task<bool> SupersededAsync(ApplicationDbContext db, WebhookDelivery row, CancellationToken ct) =>
        db.Set<WebhookDelivery>().AnyAsync(
            d => d.Id != row.Id &&
                 d.IncidentId == row.IncidentId &&
                 d.AckType == row.AckType &&
                 d.CreatedAt > row.CreatedAt,
            ct);

    /// <summary>
    /// Take the row out of the due window while it is being dispatched, in a way that
    /// survives losing the database (or the process) for the rest of this sweep.
    /// </summary>
    private static void Claim(WebhookDelivery row)
    {
        row.AttemptCount++;
        row.AttemptedAt = DateTime.UtcNow;

        if (row.AttemptCount >= IncidentEventDispatcher.MaxAttempts)
        {
            row.Status = WebhookDeliveryStatus.Failed;
            row.NextRetryAt = null;
        }
        else
        {
            row.Status = WebhookDeliveryStatus.Retrying;
            row.NextRetryAt = DateTime.UtcNow + IncidentEventDispatcher.BackoffFor(row.AttemptCount);
        }

        row.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Terminal Failed. A null reason keeps the error that put the row into retry in the first place.</summary>
    private static void CloseOut(WebhookDelivery row, string? reason)
    {
        row.Status = WebhookDeliveryStatus.Failed;
        row.NextRetryAt = null;
        if (reason is not null)
            row.Error = IncidentEventDispatcher.ClampError(reason);
        row.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// The dispatch produced no attempt row, so this row is still the chain. The claim already
    /// decided whether it is requeued or out of attempts; record why and say which it was.
    /// </summary>
    private void NoteUnrecorded(WebhookDelivery row, string error)
    {
        row.Error = IncidentEventDispatcher.ClampError(error);
        row.UpdatedAt = DateTime.UtcNow;

        if (row.NextRetryAt is null)
            logger.LogWarning(
                "Delivery {DeliveryId} (incident {IncidentId}) reached the {Max}-attempt limit without a recorded ACK; chain stopped: {Error}",
                row.Id, row.IncidentId, IncidentEventDispatcher.MaxAttempts, error);
        else
            logger.LogWarning(
                "Delivery {DeliveryId} (incident {IncidentId}) recorded no attempt; requeued for {NextRetryAt} (attempt {Attempt}/{Max}): {Error}",
                row.Id, row.IncidentId, row.NextRetryAt, row.AttemptCount, IncidentEventDispatcher.MaxAttempts, error);
    }

    /// <summary>Timeline, audit and metric for a chain that stopped without a recorded attempt; never throws.</summary>
    private async Task WriteChainStoppedTraceAsync(
        IServiceProvider services, ApplicationDbContext db, WebhookDelivery row, CancellationToken ct)
    {
        if (row.AckType?.StartsWith("manual:", StringComparison.Ordinal) == true) return;

        try
        {
            db.Set<IncidentTimelineEvent>().Add(new IncidentTimelineEvent
            {
                IncidentId = row.IncidentId,
                EventType = TimelineEventType.ActionFailed,
                Title = "ACK callback failed",
                Description = $"Callback '{row.AckType}' to {row.Url} stopped after {row.AttemptCount} attempt(s): {row.Error}",
                ActorUserId = "system:action",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);

            services.GetService<Telemetry.CalluMetrics>()?.ServiceActionExecution("event", "failed");

            var audit = services.GetService<Callu.Application.Services.IAuditLogService>();
            if (audit is not null)
                await audit.LogAsync(
                    "system:action", AuditAction.ServiceActionFailed, "Incident", row.IncidentId.ToString(),
                    null, IncidentEventDispatcher.ClampError(row.Error),
                    description: $"ACK callback '{row.AckType}' failed permanently",
                    cancellationToken: ct);
        }
        catch (Exception ex) when (!IsShutdown(ex, ct))
        {
            logger.LogWarning(ex,
                "Could not write the failure trace for stopped ACK chain {DeliveryId} (incident {IncidentId})",
                row.Id, row.IncidentId);
        }
    }

    /// <summary>
    /// Persist one row. On failure the row is detached so it cannot fail the next row's save
    /// too; what is already committed for it stays its state, and the claim keeps it alive.
    /// </summary>
    private async Task<bool> TrySaveAsync(ApplicationDbContext db, WebhookDelivery row, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (!IsShutdown(ex, ct))
        {
            db.Entry(row).State = EntityState.Detached;
            logger.LogError(ex,
                "WebhookDeliveryRetry: could not persist delivery {DeliveryId}; leaving it to a later sweep", row.Id);
            return false;
        }
    }

    private static bool IsShutdown(Exception ex, CancellationToken ct) =>
        ex is OperationCanceledException && ct.IsCancellationRequested;
}
