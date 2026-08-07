using System.Text.Json;
using System.Text.Json.Serialization;

namespace Callu.Infrastructure.Providers.CalluVoice;

/// <summary>One piece of speakable text and the language it is spoken in.</summary>
public sealed record CalluVoiceSegment(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("lang")] string Lang);

/// <summary>What is spoken back to confirm a keypress.</summary>
public sealed record CalluVoiceResponses(
    [property: JsonPropertyName("acknowledged")] IReadOnlyList<CalluVoiceSegment> Acknowledged,
    [property: JsonPropertyName("escalated")] IReadOnlyList<CalluVoiceSegment> Escalated,
    [property: JsonPropertyName("conference")] IReadOnlyList<CalluVoiceSegment> Conference,
    [property: JsonPropertyName("invalid_key")] IReadOnlyList<CalluVoiceSegment> InvalidKey);

/// <summary>The DTMF key each action is bound to.</summary>
public sealed record CalluVoiceKeys(
    [property: JsonPropertyName("acknowledge")] string Acknowledge,
    [property: JsonPropertyName("escalate")] string Escalate,
    [property: JsonPropertyName("repeat")] string Repeat,
    [property: JsonPropertyName("conference")] string Conference);

/// <summary>The body of POST /calls.</summary>
public sealed record CalluVoiceCallRequest(
    [property: JsonPropertyName("call_id")] string CallId,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("announcement")] IReadOnlyList<CalluVoiceSegment> Announcement,
    [property: JsonPropertyName("prompt")] IReadOnlyList<CalluVoiceSegment> Prompt,
    [property: JsonPropertyName("responses")] CalluVoiceResponses Responses,
    [property: JsonPropertyName("keys")] CalluVoiceKeys Keys)
{
    [JsonPropertyName("voice")]
    public string? Voice { get; init; }

    [JsonPropertyName("callback_url")]
    public string? CallbackUrl { get; init; }
}

/// <summary>Serialization for the callu-voice wire format.</summary>
public static class CalluVoiceJson
{
    /// <summary>Nulls are dropped because callu-voice rejects a body carrying a field it does not know,
    /// and an absent optional is the only way to ask for its own default.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
