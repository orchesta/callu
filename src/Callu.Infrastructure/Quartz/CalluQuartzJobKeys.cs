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

    public static readonly JobKey VoiceCallStuckSweepJob = new("VoiceCallStuckSweepJob", Group);
    public static readonly TriggerKey VoiceCallStuckSweepTrigger = new("VoiceCallStuckSweepTrigger", Group);

    public static readonly JobKey UnconfirmedVoiceCallSweepJob = new("UnconfirmedVoiceCallSweepJob", Group);
    public static readonly TriggerKey UnconfirmedVoiceCallSweepTrigger = new("UnconfirmedVoiceCallSweepTrigger", Group);

    public static readonly JobKey VoiceCarrierReconciliationJob = new("VoiceCarrierReconciliationJob", Group);
    public static readonly TriggerKey VoiceCarrierReconciliationTrigger = new("VoiceCarrierReconciliationTrigger", Group);

    public static readonly JobKey ConferenceRoomExpiryJob = new("ConferenceRoomExpiryJob", Group);
    public static readonly TriggerKey ConferenceRoomExpiryTrigger = new("ConferenceRoomExpiryTrigger", Group);

    public static readonly JobKey WebhookDeliveryRetryJob = new("WebhookDeliveryRetryJob", Group);
    public static readonly TriggerKey WebhookDeliveryRetryTrigger = new("WebhookDeliveryRetryTrigger", Group);

    public static readonly JobKey RefreshTokenCleanupJob = new("RefreshTokenCleanupJob", Group);
    public static readonly TriggerKey RefreshTokenCleanupTrigger = new("RefreshTokenCleanupTrigger", Group);

    public static readonly JobKey NotificationChannelDeliveryRetryJob = new("NotificationChannelDeliveryRetryJob", Group);
    public static readonly TriggerKey NotificationChannelDeliveryRetryTrigger = new("NotificationChannelDeliveryRetryTrigger", Group);

    public static readonly JobKey RetentionPruneJob = new("RetentionPruneJob", Group);
    public static readonly TriggerKey RetentionPruneTrigger = new("RetentionPruneTrigger", Group);

    public static readonly JobKey AuditArchiveJob = new("AuditArchiveJob", Group);
    public static readonly TriggerKey AuditArchiveTrigger = new("AuditArchiveTrigger", Group);

    public static readonly JobKey AuditChainSealJob = new("AuditChainSealJob", Group);
    public static readonly TriggerKey AuditChainSealTrigger = new("AuditChainSealTrigger", Group);

    public static readonly JobKey AuditChainVerifyJob = new("AuditChainVerifyJob", Group);
    public static readonly TriggerKey AuditChainVerifyTrigger = new("AuditChainVerifyTrigger", Group);
}
