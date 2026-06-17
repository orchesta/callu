namespace Callu.Shared.Models.Communication;

/// <summary>
/// Data passed to VoxEngine for an incident call (fetched via HTTP, not customData)
/// </summary>
public class VoxCallData
{
    public string IncidentId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;

    public string SipServer { get; set; } = string.Empty;
    public string SipUsername { get; set; } = string.Empty;
    public string SipPassword { get; set; } = string.Empty;
    public string CallerId { get; set; } = string.Empty;

    public string Language { get; set; } = "tr-TR";

    public Dictionary<string, string>? TtsMessages { get; set; }

    public string? ConferenceId { get; set; }
    public int MaxParticipants { get; set; } = 50;
    public bool Record { get; set; }
}
