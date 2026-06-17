namespace Callu.Shared.Models.Conference;

/// <summary>
/// Information about a conference participant, returned during token validation.
/// </summary>
public class ParticipantInfoDto
{
    public string ParticipantToken { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public Guid RoomId { get; set; }
    /// <summary>
    /// Voximplant conference "number" passed to Web SDK <c>callConference</c> (must match routing rule / scenario).
    /// </summary>
    public string VoximplantConferenceId { get; set; } = "";
    public Guid IncidentId { get; set; }
    public string IncidentTitle { get; set; } = "";
    public string IncidentSeverity { get; set; } = "";
    public string RoomStatus { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int ActiveParticipants { get; set; }
    public bool IsAlreadyActive { get; set; }
}
