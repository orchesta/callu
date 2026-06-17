namespace Callu.Shared.Models.Communication;

/// <summary>
/// Request for text-to-speech
/// </summary>
public class TTSRequest
{
    public string Text { get; set; } = string.Empty;
    public string? VoiceId { get; set; }
    public string? Language { get; set; }
}
