using System.ComponentModel.DataAnnotations;
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

    /// <summary>The page attempt this call was placed for, echoed back by the scenario.</summary>
    // Empty on a call placed by a script older than contract 1.6; the guard that reads it treats
    // "not told" as "cannot tell" rather than as "no call went out".
    [JsonPropertyName("attempt_id")]
    [StringLength(36)]
    public string? AttemptId { get; set; }

    /// <summary>Conference identifier from the conference scenario, used as a fallback when the incident id is empty.</summary>
    [JsonPropertyName("conference_id")]
    public string? ConferenceId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
    
    [JsonPropertyName("duration")]
    public int Duration { get; set; }
    
    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }
}
