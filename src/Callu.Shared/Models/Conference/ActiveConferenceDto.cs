namespace Callu.Shared.Models.Conference;

/// <summary>
/// Returns active video conference information along with the user's specific participant token to join.
/// </summary>
public class ActiveConferenceDto
{
    public Guid RoomId { get; set; }
    public string? RoomToken { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ParticipantCount { get; set; }
    public string? UserParticipantToken { get; set; }
    public DateTime ExpiresAt { get; set; }
}
