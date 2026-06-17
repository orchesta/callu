namespace Callu.Shared.Models.Communication;

/// <summary>
/// Request to make an outbound voice call
/// </summary>
public class MakeCallRequest
{
    public string Destination { get; set; } = string.Empty;
    public string? CallerId { get; set; }
    public string? CustomData { get; set; }
    public string? VoiceId { get; set; }

    public Guid? IncidentId { get; set; }
    public string? IncidentTitle { get; set; }
    public string? Severity { get; set; }
    public string? ServiceName { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
    public string? DataLanguage { get; set; }
}
