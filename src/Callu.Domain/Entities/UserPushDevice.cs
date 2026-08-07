using System.ComponentModel.DataAnnotations;
using Callu.Domain.Base;

namespace Callu.Domain.Entities;

public class UserPushDevice : BaseEntity
{
    public const int MaxPushTokenLength = 4096;
    public const int MaxPlatformLength = 20;
    public const int MaxDevicesPerUser = 10;

    [Required]
    [StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(MaxPlatformLength)]
    public string Platform { get; set; } = string.Empty;

    [Required]
    [StringLength(MaxPushTokenLength)]
    public string PushToken { get; set; } = string.Empty;

    public DateTime LastSeenAt { get; set; }
}
