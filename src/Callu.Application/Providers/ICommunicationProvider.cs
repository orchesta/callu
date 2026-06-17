using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Communication;

namespace Callu.Application.Providers;

/// <summary>
/// Interface for communication providers (Voximplant, Verimor, Twilio, etc.)
/// </summary>
public interface ICommunicationProvider
{
    /// <summary>
    /// Provider type identifier (voximplant, verimor, twilio)
    /// </summary>
    string ProviderType { get; }
    
    /// <summary>
    /// Capabilities this provider implementation supports
    /// </summary>
    CommunicationCapability Capabilities { get; }
    
    /// <summary>
    /// Initialize the provider with configuration
    /// </summary>
    Task InitializeAsync(string configJson, SipTrunkSettings? sipTrunk);
    
    /// <summary>
    /// Test connection to the provider
    /// </summary>
    Task<(bool Success, string Message)> TestConnectionAsync();
    
    #region Voice Calls
    
    /// <summary>
    /// Make an outbound voice call
    /// </summary>
    Task<CallResult> MakeCallAsync(MakeCallRequest request);
    
    /// <summary>
    /// Hangup an active call
    /// </summary>
    Task HangupCallAsync(string callId);
    
    #endregion
    
    #region Messaging
    
    /// <summary>
    /// Send an SMS message
    /// </summary>
    Task<SmsResult> SendSmsAsync(SendSmsRequest request);
    
    /// <summary>
    /// Send a WhatsApp message
    /// </summary>
    Task<SmsResult> SendWhatsAppAsync(SendSmsRequest request);
    
    #endregion
    
    #region Conference
    
    /// <summary>
    /// Create a conference room
    /// </summary>
    Task<ConferenceResult> CreateConferenceAsync(CreateConferenceRequest request);
    
    /// <summary>
    /// Add a participant to conference
    /// </summary>
    Task AddParticipantAsync(string conferenceId, string destination);
    
    /// <summary>
    /// End a conference
    /// </summary>
    Task EndConferenceAsync(string conferenceId);
    
    #endregion
    
    #region Speech
    
    /// <summary>
    /// Synthesize speech from text
    /// </summary>
    Task<byte[]> SynthesizeSpeechAsync(TTSRequest request);
    
    /// <summary>
    /// Recognize speech from audio
    /// </summary>
    Task<string> RecognizeSpeechAsync(byte[] audio, string? language);
    
    #endregion
}
