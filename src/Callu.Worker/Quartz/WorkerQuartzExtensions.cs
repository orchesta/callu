using Callu.Infrastructure.Quartz;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Serialization.SystemTextJson;

namespace Callu.Worker.Quartz;

/// <summary>
/// Quartz.NET scheduler for periodic tenant work (replaces timer BackgroundService loops on the Worker).
/// </summary>
public static class WorkerQuartzExtensions
{
    public static IServiceCollection AddCalluWorkerQuartzScheduling(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var usePersistentStore = configuration.GetValue("Quartz:UsePersistentStore", false);

        // Deliberately fatal. Falling back to the in-memory store would give each worker its own
        // scheduler, so every job fires once per worker — an escalation step paged twice, silently.
        if (usePersistentStore && string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Quartz:UsePersistentStore is true but ConnectionStrings:DefaultConnection is empty. " +
                "Set the connection string (the qrtz_* tables live in that database), or set " +
                "Quartz:UsePersistentStore to false to run a single worker on the in-memory store.");

        services.Configure<QuartzOptions>(o =>
        {
            o.Scheduling.IgnoreDuplicates = true;
            o.Scheduling.OverWriteExistingData = true;
        });

        services.AddQuartz(q =>
        {
            q.SchedulerName = "CalluWorker";
            q.SchedulerId = "AUTO";
            q.UseSimpleTypeLoader();
            q.UseDefaultThreadPool(tp => { tp.MaxConcurrency = 10; });

            if (usePersistentStore && !string.IsNullOrWhiteSpace(connectionString))
            {
                q.UsePersistentStore(store =>
                {
                    store.PerformSchemaValidation = true;
                    store.UseProperties = true;
                    store.RetryInterval = TimeSpan.FromSeconds(15);
                    store.UseGenericDatabase("Npgsql", db =>
                    {
                        db.ConnectionString = connectionString;
                        db.TablePrefix = "qrtz_";
                    });
                    store.UseSystemTextJsonSerializer();
                    store.UseClustering(c =>
                    {
                        c.CheckinInterval = TimeSpan.FromSeconds(10);
                        c.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                    });
                });
            }
            else
            {
                q.UseInMemoryStore();
            }

            q.ScheduleJob<EscalationProcessingQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.EscalationTrigger)
                    .WithCronSchedule("0/10 * * * * ?")
                    .WithDescription("Escalation orchestration per active organization"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.EscalationJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<NotificationQueueQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.NotificationTrigger)
                    .WithCronSchedule("0/10 * * * * ?")
                    .WithDescription("Notification retry queue per organization"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.NotificationJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<HealthCheckQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.HealthCheckTrigger)
                    .WithCronSchedule("0/15 * * * * ?")
                    .WithDescription("Status page HTTP checks per organization"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.HealthCheckJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<ScheduleMaterializationQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.ScheduleMaterializationTrigger)
                    .WithCronSchedule("0 0 3 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc))
                    .WithDescription("Rematerialize every schedule's occurrences 30 days forward (03:00 UTC)"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.ScheduleMaterializationJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<VoiceCallRetryQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.VoiceCallRetryTrigger)
                    .WithCronSchedule("0/15 * * * * ?")
                    .WithDescription("Retry voice calls whose NextRetryAt deadline has elapsed"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.VoiceCallRetryJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<VoiceCallStuckSweepQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.VoiceCallStuckSweepTrigger)
                    .WithSimpleSchedule(x => x.WithIntervalInMinutes(1).RepeatForever())
                    .WithDescription("Resolve a voice call stuck non-terminal past its grace period"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.VoiceCallStuckSweepJob)
                    .DisallowConcurrentExecution());

            // Every two minutes: this is the only thing that notices a provider which accepts calls and
            // reports nothing, and an operator has to learn that during the incident, not after it.
            q.ScheduleJob<UnconfirmedVoiceCallSweepQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.UnconfirmedVoiceCallSweepTrigger)
                    .WithSimpleSchedule(x => x.WithIntervalInMinutes(2).RepeatForever())
                    .WithDescription("Report a voice page the provider accepted but never reported a call for"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.UnconfirmedVoiceCallSweepJob)
                    .DisallowConcurrentExecution());

            // Five minutes: one page's escalation is the window that matters, and a carrier that
            // went missing on a restart must be back before the next incident, not after it.
            q.ScheduleJob<VoiceCarrierReconciliationQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.VoiceCarrierReconciliationTrigger)
                    .WithSimpleSchedule(x => x.WithIntervalInMinutes(5).RepeatForever())
                    .WithDescription("Re-send the SIP carrier to a self-hosted voice service that has lost it"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.VoiceCarrierReconciliationJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<ConferenceRoomExpiryQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.ConferenceRoomExpiryTrigger)
                    .WithCronSchedule("0 * * * * ?")
                    .WithDescription("Sweep conference rooms past their ExpiresAt"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.ConferenceRoomExpiryJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<WebhookDeliveryRetryQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.WebhookDeliveryRetryTrigger)
                    .WithCronSchedule("0 * * * * ?")
                    .WithDescription("Retry outbound webhook deliveries past NextRetryAt"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.WebhookDeliveryRetryJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<RefreshTokenCleanupQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.RefreshTokenCleanupTrigger)
                    .WithCronSchedule("0 0 2 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc))
                    .WithDescription("Delete expired refresh tokens (02:00 UTC)"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.RefreshTokenCleanupJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<NotificationChannelDeliveryRetryQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.NotificationChannelDeliveryRetryTrigger)
                    .WithCronSchedule("0 * * * * ?")
                    .WithDescription("Retry outbound notification-channel deliveries past NextRetryAt"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.NotificationChannelDeliveryRetryJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<RetentionPruneQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.RetentionPruneTrigger)
                    .WithCronSchedule("0 0 4 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc))
                    .WithDescription("Prune operational/log tables past their configured retention (04:00 UTC; off unless Callu:Retention set)"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.RetentionPruneJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<AuditArchiveQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.AuditArchiveTrigger)
                    .WithCronSchedule("0 30 3 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc))
                    .WithDescription("Archive the audit trail's closed days (03:30 UTC, before retention; off unless Callu:AuditArchive:Path set)"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.AuditArchiveJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<AuditChainSealQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.AuditChainSealTrigger)
                    .WithSimpleSchedule(x => x.WithIntervalInMinutes(5).RepeatForever())
                    .WithDescription("Chain the audit entries written since the last run"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.AuditChainSealJob)
                    .DisallowConcurrentExecution());

            q.ScheduleJob<AuditChainVerifyQuartzJob>(
                t => t
                    .WithIdentity(CalluQuartzJobKeys.AuditChainVerifyTrigger)
                    .WithCronSchedule("0 30 4 * * ?", x => x.InTimeZone(TimeZoneInfo.Utc))
                    .WithDescription("Replay the audit chain and report a break (04:30 UTC, after retention)"),
                j => j
                    .WithIdentity(CalluQuartzJobKeys.AuditChainVerifyJob)
                    .DisallowConcurrentExecution());
        });

        services.AddQuartzHostedService(o =>
        {
            o.WaitForJobsToComplete = true;
            o.AwaitApplicationStarted = true;
        });

        return services;
    }
}
