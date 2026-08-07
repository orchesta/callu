namespace Callu.Domain.Enums;

/// <summary>
/// Status of a video conference room
/// </summary>
public enum ConferenceRoomStatus
{
    /// <summary>
    /// Conference is active and accepting participants
    /// </summary>
    Active,
    
    /// <summary>
    /// Conference has ended normally
    /// </summary>
    Ended,
    
    /// <summary>
    /// Conference expired (max duration reached)
    /// </summary>
    Expired
}
