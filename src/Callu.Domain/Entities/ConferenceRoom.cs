using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;
using Callu.Domain.Enums;

namespace Callu.Domain.Entities;

/// <summary>
/// A video conference room created during an incident call.
/// When a responder presses 999, a room is created and unique links are sent to team members.
/// </summary>
public class ConferenceRoom : BaseEntity
{
    /// <summary>
    /// The incident that triggered this conference
    /// </summary>
    public Guid IncidentId { get; set; }

    /// <summary>
    /// Navigation property for incident
    /// </summary>
    public virtual Incident? Incident { get; set; }

    /// <summary>
    /// Unique room token for admin/internal reference
    /// </summary>
    [Required]
    [StringLength(64)]
    public string RoomToken { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the conference room
    /// </summary>
    public ConferenceRoomStatus Status { get; set; } = ConferenceRoomStatus.Active;

    /// <summary>
    /// Maximum duration in minutes (default: 60)
    /// </summary>
    public int MaxDurationMinutes { get; set; } = 60;

    /// <summary>
    /// Whether recording is enabled for this conference
    /// </summary>
    public bool RecordingEnabled { get; set; }

    /// <summary>
    /// URL to the recording after conference ends (if recorded)
    /// </summary>
    [StringLength(500)]
    public string? RecordingUrl { get; set; }

    /// <summary>
    /// When the conference room expires (CreatedAt + MaxDurationMinutes)
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When the conference actually ended
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Voximplant conference ID (from Management API)
    /// </summary>
    [StringLength(200)]
    public string? VoximplantConferenceId { get; set; }

    /// <summary>
    /// Participants in this conference
    /// </summary>
    public virtual ICollection<ConferenceParticipant> Participants { get; set; } = new List<ConferenceParticipant>();
}
