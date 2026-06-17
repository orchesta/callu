using System.Text.Json.Serialization;

namespace Callu.Shared.Models.Communication;

/// <summary>
/// Status callback from VoxEngine script.
/// Properties use JsonPropertyName to match the snake_case keys sent by VoxEngine scripts.
/// </summary>
public class VoxCallbackRequest
{
    [JsonPropertyName("call_token")]
    public string CallToken { get; set; } = string.Empty;

    /// <summary>
    /// Per-VoxEngine session UUID (sent by scenario). Used to upsert one CallLog row per live call.
    /// </summary>
    [JsonPropertyName("call_session_id")]
    public string? CallSessionId { get; set; }
    
    [JsonPropertyName("incident_id")]
    public string IncidentId { get; set; } = string.Empty;

    /// <summary>
    /// Voximplant conference identifier sent by the conference scenario. The backend
    /// uses this as a fallback to resolve IncidentId when <see cref="IncidentId"/> is
    /// empty (e.g. Web-SDK-driven joins where no call_token is available).
    /// </summary>
    [JsonPropertyName("conference_id")]
    public string? ConferenceId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonPropertyName("duration")]
    public int Duration { get; set; }
    
    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }
}
