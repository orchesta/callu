using Quartz;

namespace Callu.Infrastructure.Quartz;

/// <summary>Stable job/trigger identities for optional persistent store.</summary>
public static class CalluQuartzJobKeys
{
    public const string Group = "callu";

    public static readonly JobKey EscalationJob = new("EscalationProcessingJob", Group);
    public static readonly TriggerKey EscalationTrigger = new("EscalationTrigger", Group);

    public static readonly JobKey NotificationJob = new("NotificationQueueJob", Group);
    public static readonly TriggerKey NotificationTrigger = new("NotificationTrigger", Group);

    public static readonly JobKey HealthCheckJob = new("HealthCheckJob", Group);
    public static readonly TriggerKey HealthCheckTrigger = new("HealthCheckTrigger", Group);

    public static readonly JobKey ScheduleMaterializationJob = new("ScheduleMaterializationJob", Group);
    public static readonly TriggerKey ScheduleMaterializationTrigger = new("ScheduleMaterializationTrigger", Group);

    public static readonly JobKey VoiceCallRetryJob = new("VoiceCallRetryJob", Group);
    public static readonly TriggerKey VoiceCallRetryTrigger = new("VoiceCallRetryTrigger", Group);

    public static readonly JobKey ConferenceRoomExpiryJob = new("ConferenceRoomExpiryJob", Group);
    public static readonly TriggerKey ConferenceRoomExpiryTrigger = new("ConferenceRoomExpiryTrigger", Group);

    public static readonly JobKey WebhookDeliveryRetryJob = new("WebhookDeliveryRetryJob", Group);
    public static readonly TriggerKey WebhookDeliveryRetryTrigger = new("WebhookDeliveryRetryTrigger", Group);

    public static readonly JobKey RefreshTokenCleanupJob = new("RefreshTokenCleanupJob", Group);
    public static readonly TriggerKey RefreshTokenCleanupTrigger = new("RefreshTokenCleanupTrigger", Group);

    public static readonly JobKey NotificationChannelDeliveryRetryJob = new("NotificationChannelDeliveryRetryJob", Group);
    public static readonly TriggerKey NotificationChannelDeliveryRetryTrigger = new("NotificationChannelDeliveryRetryTrigger", Group);
}
