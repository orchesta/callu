namespace Callu.Domain.Enums;

/// <summary>
/// Communication capabilities supported by providers
/// </summary>
[Flags]
public enum CommunicationCapability
{
    None = 0,
    
    /// <summary>
    /// SIP/PSTN outbound voice calls
    /// </summary>
    VoiceCalls = 1 << 0,
    
    /// <summary>
    /// SMS text messaging
    /// </summary>
    Sms = 1 << 1,
    
    /// <summary>
    /// WhatsApp messaging
    /// </summary>
    WhatsApp = 1 << 2,
    
    /// <summary>
    /// Multi-party video conference rooms
    /// </summary>
    VideoConference = 1 << 3,
    
    /// <summary>
    /// Text-to-Speech synthesis
    /// </summary>
    TTS = 1 << 4,
    
    /// <summary>
    /// Automatic Speech Recognition
    /// </summary>
    ASR = 1 << 5,
    
    /// <summary>
    /// Call recording capability
    /// </summary>
    Recording = 1 << 6,
    
    /// <summary>
    /// Answering machine / voicemail detection
    /// </summary>
    VoicemailDetection = 1 << 9
}
