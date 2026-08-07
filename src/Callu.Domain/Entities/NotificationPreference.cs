using System.ComponentModel.DataAnnotations;

namespace Callu.Domain.Entities;

/// <summary>
/// User notification preferences
/// </summary>
public class NotificationPreference
{
    [Key]
    public Guid Id { get; set; }
    
    /// <summary>
    /// User ID (from AspNetUsers)
    /// </summary>
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;
    
    /// <summary>
    /// Receive notifications via email
    /// </summary>
    public bool EmailEnabled { get; set; } = true;
    
    /// <summary>
    /// Receive notifications via SMS
    /// </summary>
    public bool SmsEnabled { get; set; } = false;
    
    /// <summary>Receive notifications via voice call; off by default, so a phone page is an explicit opt-in.</summary>
    public bool VoiceEnabled { get; set; } = false;
    
    /// <summary>
    /// Receive notifications via push notification
    /// </summary>
    public bool PushEnabled { get; set; } = true;
    
    /// <summary>
    /// Quiet hours start (e.g., "22:00")
    /// </summary>
    [MaxLength(5)]
    public string? QuietHoursStart { get; set; }
    
    /// <summary>
    /// Quiet hours end (e.g., "08:00")
    /// </summary>
    [MaxLength(5)]
    public string? QuietHoursEnd { get; set; }
    
    /// <summary>
    /// User's timezone for quiet hours
    /// </summary>
    [MaxLength(50)]
    public string Timezone { get; set; } = "UTC";
    
    /// <summary>
    /// When preferences were created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When preferences were last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
