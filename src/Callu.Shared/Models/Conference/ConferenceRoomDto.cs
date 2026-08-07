namespace Callu.Shared.Models.Conference;

public class ConferenceRoomDto
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public string IncidentTitle { get; set; } = string.Empty;
    public string RoomToken { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ParticipantCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool RecordingEnabled { get; set; }
    public string? RecordingUrl { get; set; }
    public string? VoximplantConferenceId { get; set; }
}
