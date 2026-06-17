namespace Callu.Shared.Models.Conference;

/// <summary>
/// Result of joining a conference, includes Voximplant connection details.
/// </summary>
public class JoinResultDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }

    public string? VoximplantLoginKey { get; set; }
    public string? VoximplantAppName { get; set; }
    public string? VoximplantAccountName { get; set; }
    public string? VoximplantUsername { get; set; }

    public string? TwilioAccessToken { get; set; }
    public string? TwilioRoomName { get; set; }
    
    public string DisplayName { get; set; } = "";
}
