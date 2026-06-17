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
                    .WithCronSchedule("0 0 3 * * ?")
                    .WithDescription("Rematerialize every schedule's occurrences 30 days forward"),
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
                    .WithCronSchedule("0 0 2 * * ?")
                    .WithDescription("Delete expired refresh tokens"),
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
        });

        services.AddQuartzHostedService(o =>
        {
            o.WaitForJobsToComplete = true;
            o.AwaitApplicationStarted = true;
        });

        return services;
    }
}
