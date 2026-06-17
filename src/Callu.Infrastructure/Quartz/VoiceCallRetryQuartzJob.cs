using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Providers;
using Callu.Domain.Enums;
using Callu.Infrastructure.Persistence;
using Callu.Shared.Models.Communication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>
/// Fires any voice call retries whose <see cref="Domain.Entities.CallLog.NextRetryAt"/>
/// deadline has elapsed. Replaces the previous in-process <c>Task.Delay</c> retry in
/// <c>VoximplantCallDataService.ScheduleRetryAsync</c>, which (a) lost pending retries
/// on process restart and (b) targeted the wrong user. This job is durable because the
/// retry state lives in the database.
///
/// Correctness guards:
/// * Terminal incidents (Resolved / Closed) drop their pending retries — responders
///   do not want the phone ringing about something that was already fixed.
///   Acknowledged is intentionally NOT terminal here so an Acknowledged → Reopened
///   transition re-pages the responder. The acknowledge path is responsible for
///   clearing NextRetryAt explicitly (it does — see VoximplantVoiceCallbackPersistence).
///
/// * NextRetryAt is cleared BEFORE the outgoing call so a slow provider response cannot
///   cause the same retry to fire twice on the next tick.
/// * Each successful retry writes a follow-up Notification(VoiceCall) row so the bell
///   history and dispatch metrics stay consistent across the two pipelines. The
///   retryGeneration component of DedupeKey is the CallLog.AttemptNumber, so a fresh
///   retry never collides with the original dispatch row.
/// </summary>
[DisallowConcurrentExecution]
public sealed class VoiceCallRetryQuartzJob(
    IServiceScopeFactory scopeFactory,
    ILogger<VoiceCallRetryQuartzJob> logger)
    : IJob
{
    private const int BatchSize = 25;

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var providerRegistry = scope.ServiceProvider.GetRequiredService<ICommunicationProviderRegistry>();

        var now = DateTime.UtcNow;
        var due = await db.CallLogs
            .Include(c => c.Incident)
            .Where(c => c.NextRetryAt != null &&
                        c.NextRetryAt <= now &&
                        !c.IsDeleted)
            .OrderBy(c => c.NextRetryAt)
            .Take(BatchSize)
            .ToListAsync(context.CancellationToken);

        if (due.Count == 0) return;

        logger.LogInformation("VoiceCallRetry: {Count} due retries", due.Count);

        foreach (var callLog in due)
        {
            try
            {
                if (callLog.Incident is null ||
                    callLog.Incident.Status.IsTerminal())
                {
                    callLog.NextRetryAt = null;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(callLog.PhoneNumber))
                {
                    logger.LogWarning(
                        "VoiceCallRetry: dropping retry for CallLog {CallLogId} — no phone number", callLog.Id);
                    callLog.NextRetryAt = null;
                    continue;
                }

                callLog.NextRetryAt = null;
                await db.SaveChangesAsync(context.CancellationToken);

                var provider = providerRegistry.GetProvider(CommunicationCapability.VoiceCalls);
                if (provider is null)
                {
                    logger.LogWarning("VoiceCallRetry: no voice provider configured; skipping retry for {IncidentId}",
                        callLog.IncidentId);
                    continue;
                }

                var incident = callLog.Incident!;
                var result = await provider.MakeCallAsync(new MakeCallRequest
                {
                    Destination = callLog.PhoneNumber,
                    IncidentId = incident.Id,
                    IncidentTitle = incident.Title,
                    Severity = incident.Severity.ToString(),
                    Description = incident.Description,
                    DataLanguage = incident.DataLanguage
                });

                if (!result.Success)
                {
                    logger.LogWarning(
                        "VoiceCallRetry failed for incident {IncidentId} ({Phone}): {Error}",
                        callLog.IncidentId, callLog.PhoneNumber, result.ErrorMessage);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "VoiceCallRetry: unhandled error processing CallLog {CallLogId}", callLog.Id);
            }
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}
