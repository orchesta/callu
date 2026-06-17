namespace Callu.Shared.Models.Conference;

/// <summary>
/// Result of creating a conference room
/// </summary>
public class ConferenceRoomResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Guid RoomId { get; set; }
    public string RoomToken { get; set; } = string.Empty;
    public string ConferenceUrl { get; set; } = string.Empty;
    public int ParticipantCount { get; set; }
}
