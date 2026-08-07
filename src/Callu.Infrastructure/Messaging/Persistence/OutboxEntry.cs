using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Infrastructure.Messaging.Persistence;

public enum OutboxEntryStatus
{
    Pending = 0,
    Claimed = 1,
    Sent = 2,
    Failed = 3,
}

/// <summary>A message staged inside the domain transaction, for the dispatcher to publish afterwards.</summary>
public class OutboxEntry : BaseEntity
{
    public const int MaxMessageTypeLength = 128;
    public const int MaxLastErrorLength = 1000;
    public const int MaxTraceParentLength = 64;

    /// <summary>Stable wire name from <see cref="CalluTopology"/>, never a CLR type name.</summary>
    [Required]
    [StringLength(MaxMessageTypeLength)]
    public string MessageType { get; set; } = string.Empty;

    [Required]
    public string Payload { get; set; } = string.Empty;

    public OutboxEntryStatus Status { get; set; } = OutboxEntryStatus.Pending;

    public int AttemptCount { get; set; }

    /// <summary>Never null: a null would drop the row out of the dispatcher's `&lt;= now` filter for good.</summary>
    public DateTime NextAttemptAt { get; set; }

    /// <summary>Claim lease, so a row held by a host that died returns on expiry.</summary>
    public DateTime? ClaimedUntil { get; set; }

    public DateTime? SentAt { get; set; }

    private string? _lastError;

    /// <summary>Clamped on write; a provider message longer than the column would fail the commit.</summary>
    [StringLength(MaxLastErrorLength)]
    public string? LastError
    {
        get => _lastError;
        set => _lastError = Clamp(value, MaxLastErrorLength);
    }

    private string? _traceParent;

    /// <summary>W3C traceparent captured while staging, so the publish joins the request's trace.</summary>
    [StringLength(MaxTraceParentLength)]
    public string? TraceParent
    {
        get => _traceParent;
        set => _traceParent = Clamp(value, MaxTraceParentLength);
    }

    internal static string? Clamp(string? value, int max) =>
        value is not null && value.Length > max ? value[..max] : value;
}
