using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>One row per outbound notification-channel send (Slack / Teams / webhook / channel-email) for an incident event.</summary>
public class NotificationChannelDelivery : BaseEntity
{
    /// <summary>The NotificationChannel config this attempt targeted.</summary>
    public Guid ChannelId { get; set; }

    public Guid IncidentId { get; set; }
    public Guid? ServiceId { get; set; }

    /// <summary>"incident.created" | "incident.acknowledged" | "incident.resolved".</summary>
    [Required, StringLength(50)]
    public string EventKey { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string Title { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Severity { get; set; }

    [Required, StringLength(2000)]
    public string MessageText { get; set; } = string.Empty;

    public int? HttpStatus { get; set; }

    public const int MaxErrorLength = 1000;

    private string? _error;

    /// <summary>Provider/HTTP failure detail, clamped to the column width without splitting a surrogate pair.</summary>
    [StringLength(MaxErrorLength)]
    public string? Error
    {
        get => _error;
        set
        {
            if (value is null || value.Length <= MaxErrorLength)
            {
                _error = value;
                return;
            }

            var cut = MaxErrorLength;
            if (char.IsHighSurrogate(value[cut - 1]))
                cut--;
            _error = value[..cut];
        }
    }

    public int AttemptCount { get; set; } = 1;

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the retry job should re-fire; null once terminal.</summary>
    public DateTime? NextRetryAt { get; set; }

    /// <summary>Stored as its string name (varchar) via a value converter — see ApplicationDbContext.</summary>
    public NotificationChannelDeliveryStatus Status { get; set; } = NotificationChannelDeliveryStatus.Retrying;
}
