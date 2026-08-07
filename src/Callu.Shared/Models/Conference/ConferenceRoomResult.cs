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

    /// <summary>How many participants were actually reached by SMS or email on this call; -1 when this call sent none because the room already existed.</summary>
    public int InvitesSentCount { get; set; } = -1;
}
