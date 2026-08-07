namespace Callu.Infrastructure.Configuration;

/// <summary>Binds to the <c>Callu:Retention</c> section; every window defaults to 0, meaning keep forever.</summary>
public class RetentionOptions
{
    public const string SectionName = "Callu:Retention";

    public int NotificationDays { get; set; }
    public int AuditLogDays { get; set; }
    public int WebhookCaptureDays { get; set; }
    public int CallLogDays { get; set; }
    public int StatusPageViewDays { get; set; }
    public int IncidentTimelineEventDays { get; set; }

    public bool AnyEnabled =>
        NotificationDays > 0 || AuditLogDays > 0 || WebhookCaptureDays > 0
        || CallLogDays > 0 || StatusPageViewDays > 0 || IncidentTimelineEventDays > 0;
}
