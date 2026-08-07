using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

/// <summary>
/// A participant in a video conference, each with a unique access token.
/// Each token allows only one concurrent connection.
/// </summary>
public class ConferenceParticipant : BaseEntity
{
    /// <summary>
    /// The conference room this participant belongs to
    /// </summary>
    public Guid ConferenceRoomId { get; set; }

    /// <summary>
    /// Navigation property for conference room
    /// </summary>
    public virtual ConferenceRoom ConferenceRoom { get; set; } = null!;

    /// <summary>
    /// The user ID of this participant (Identity user)
    /// </summary>
    [Required]
    [StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Unique token for this participant's access link.
    /// Each person gets a different token → `/conference/{ParticipantToken}`
    /// </summary>
    [Required]
    [StringLength(64)]
    public string ParticipantToken { get; set; } = string.Empty;

    /// <summary>
    /// Display name shown in conference UI
    /// </summary>
    [Required]
    [StringLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Phone number for SMS delivery of the conference link
    /// </summary>
    [StringLength(20)]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// When participant joined the conference
    /// </summary>
    public DateTime? JoinedAt { get; set; }

    /// <summary>
    /// When participant left the conference
    /// </summary>
    public DateTime? LeftAt { get; set; }

    /// <summary>
    /// Whether participant is currently active in the conference
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>Source IP of the most recent join — forensics for the anonymous bridge. (VOX-4)</summary>
    [StringLength(64)]
    public string? LastJoinIp { get; set; }

    /// <summary>User-Agent of the most recent join. (VOX-4)</summary>
    [StringLength(500)]
    public string? LastJoinUserAgent { get; set; }

    /// <summary>How many times this token has been used to join. (VOX-4)</summary>
    public int JoinCount { get; set; }
}
