namespace Callu.Shared.Models.Communication;

/// <summary>
/// Request to make an outbound voice call
/// </summary>
public class MakeCallRequest
{
    public string Destination { get; set; } = string.Empty;
    public string? CallerId { get; set; }
    public string? CustomData { get; set; }
    public string? VoiceId { get; set; }

    public Guid? IncidentId { get; set; }
    public string? IncidentTitle { get; set; }
    public string? Severity { get; set; }
    public string? ServiceName { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Announcement language (voice + TTS templates), e.g. "en-US". Optional: when unset the
    /// provider falls back to <see cref="DataLanguage"/>, so setting only DataLanguage is enough.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>Language the incident text itself is written in — drives the per-segment TTS voice.</summary>
    public string? DataLanguage { get; set; }

    /// <summary>Identity of the page attempt behind this dial, for providers that refuse the same attempt twice.</summary>
    public Guid? AttemptId { get; set; }
}
