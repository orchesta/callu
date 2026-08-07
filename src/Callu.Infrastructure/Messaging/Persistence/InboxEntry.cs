using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Infrastructure.Messaging.Persistence;

public enum InboxEntryStatus
{
    Retrying = 0,
    Consumed = 1,
    Failed = 2,
}

/// <summary>A delivery already seen, keyed by the publisher's message id, so a redelivery is not handled twice.</summary>
public class InboxEntry : BaseEntity
{
    /// <summary>Attempts before a delivery is given up on; read from here, never retyped.</summary>
    public const int MaxAttempts = 6;

    public static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(10);

    /// <summary>Backoff for the attempt that just failed: 10s, 20s, 40s, 80s, 160s — about 2m20s over the ladder.</summary>
    public static TimeSpan BackoffFor(int attemptCount) =>
        RetryBaseDelay * Math.Pow(2, Math.Max(0, Math.Min(attemptCount - 1, 4)));

    [Required]
    [StringLength(OutboxEntry.MaxMessageTypeLength)]
    public string MessageType { get; set; } = string.Empty;

    /// <summary>Kept so a retry runs from the row: the message is acked once this row commits.</summary>
    [Required]
    public string Payload { get; set; } = string.Empty;

    public InboxEntryStatus Status { get; set; } = InboxEntryStatus.Retrying;

    public int AttemptCount { get; set; }

    public DateTime NextAttemptAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    private string? _lastError;

    [StringLength(OutboxEntry.MaxLastErrorLength)]
    public string? LastError
    {
        get => _lastError;
        set => _lastError = OutboxEntry.Clamp(value, OutboxEntry.MaxLastErrorLength);
    }

    private string? _traceParent;

    [StringLength(OutboxEntry.MaxTraceParentLength)]
    public string? TraceParent
    {
        get => _traceParent;
        set => _traceParent = OutboxEntry.Clamp(value, OutboxEntry.MaxTraceParentLength);
    }
}
