namespace Callu.Shared.Models.Communication;

/// <summary>
/// Request to create a conference
/// </summary>
public class CreateConferenceRequest
{
    public string Name { get; set; } = string.Empty;
    public List<string> Participants { get; set; } = new();
    public bool EnableVideo { get; set; }
    public bool EnableRecording { get; set; }
}
