using System.Text.Json.Serialization;

namespace Callu.Shared.Models.Communication;

/// <summary>One call status event as callu-voice posts it.</summary>
public sealed class CalluVoiceCallbackRequest
{
    [JsonPropertyName("call_id")]
    public string? CallId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("duration_s")]
    public double DurationSeconds { get; set; }

    [JsonPropertyName("data")]
    public Dictionary<string, string>? Data { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }
}

/// <summary>The call a callu-voice callback token was minted for.</summary>
public sealed record CalluVoiceCallbackTicket(Guid IncidentId, string CallId, string PhoneNumber);
