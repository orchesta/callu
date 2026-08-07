namespace Callu.Shared.Models.Auth;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record NotificationPreferencesDto
{
    public bool EmailEnabled { get; init; } = true;
    public bool SmsEnabled { get; init; }
    public bool VoiceEnabled { get; init; }
    public bool PushEnabled { get; init; } = true;
    public string? QuietHoursStart { get; init; }
    public string? QuietHoursEnd { get; init; }
    public string? Timezone { get; init; }
}
