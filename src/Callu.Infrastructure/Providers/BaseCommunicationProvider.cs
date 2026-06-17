using Callu.Domain.Entities;
using Callu.Domain.Enums;
using Callu.Shared.Models.Communication;

namespace Callu.Infrastructure.Providers;

/// <summary>
/// Base class for communication providers with common functionality
/// </summary>
public abstract class BaseCommunicationProvider : Application.Providers.ICommunicationProvider
{
    protected string? ConfigJson { get; private set; }
    protected SipTrunkSettings? SipTrunk { get; private set; }
    
    public abstract string ProviderType { get; }
    public abstract CommunicationCapability Capabilities { get; }
    
    public virtual Task InitializeAsync(string configJson, SipTrunkSettings? sipTrunk)
    {
        ConfigJson = configJson;
        SipTrunk = sipTrunk;
        return Task.CompletedTask;
    }
    
    public abstract Task<(bool Success, string Message)> TestConnectionAsync();
    
    #region Voice Calls - Default NotSupported
    
    public virtual Task<CallResult> MakeCallAsync(MakeCallRequest request)
    {
        ThrowIfNotSupported(CommunicationCapability.VoiceCalls);
        return Task.FromResult(new CallResult { Success = false, ErrorMessage = "Not implemented" });
    }
    
    public virtual Task HangupCallAsync(string callId)
    {
        ThrowIfNotSupported(CommunicationCapability.VoiceCalls);
        return Task.CompletedTask;
    }
    
    #endregion
    
    #region Messaging - Default NotSupported
    
    public virtual Task<SmsResult> SendSmsAsync(SendSmsRequest request)
    {
        ThrowIfNotSupported(CommunicationCapability.Sms);
        return Task.FromResult(new SmsResult { Success = false, ErrorMessage = "Not implemented" });
    }
    
    public virtual Task<SmsResult> SendWhatsAppAsync(SendSmsRequest request)
    {
        ThrowIfNotSupported(CommunicationCapability.WhatsApp);
        return Task.FromResult(new SmsResult { Success = false, ErrorMessage = "Not implemented" });
    }
    
    #endregion
    
    #region Conference - Default NotSupported
    
    public virtual Task<ConferenceResult> CreateConferenceAsync(CreateConferenceRequest request)
    {
        ThrowIfNotSupported(CommunicationCapability.VideoConference);
        return Task.FromResult(new ConferenceResult { Success = false, ErrorMessage = "Not implemented" });
    }
    
    public virtual Task AddParticipantAsync(string conferenceId, string destination)
    {
        ThrowIfNotSupported(CommunicationCapability.VideoConference);
        return Task.CompletedTask;
    }
    
    public virtual Task EndConferenceAsync(string conferenceId)
    {
        ThrowIfNotSupported(CommunicationCapability.VideoConference);
        return Task.CompletedTask;
    }
    
    #endregion
    
    #region Speech - Default NotSupported
    
    public virtual Task<byte[]> SynthesizeSpeechAsync(TTSRequest request)
    {
        ThrowIfNotSupported(CommunicationCapability.TTS);
        return Task.FromResult(Array.Empty<byte>());
    }
    
    public virtual Task<string> RecognizeSpeechAsync(byte[] audio, string? language)
    {
        ThrowIfNotSupported(CommunicationCapability.ASR);
        return Task.FromResult(string.Empty);
    }
    
    #endregion
    
    protected void ThrowIfNotSupported(CommunicationCapability capability)
    {
        if (!Capabilities.HasFlag(capability))
        {
            throw new NotSupportedException($"{ProviderType} does not support {capability}");
        }
    }
    
    protected T? GetConfig<T>() where T : class
    {
        if (string.IsNullOrEmpty(ConfigJson))
            return null;
            
        return System.Text.Json.JsonSerializer.Deserialize<T>(ConfigJson);
    }
}
