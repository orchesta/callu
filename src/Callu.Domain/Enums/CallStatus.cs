namespace Callu.Domain.Enums;

/// <summary>
/// Status of a VoxEngine call attempt
/// </summary>
public enum CallStatus
{
    /// <summary>Call has been initiated but not yet connected</summary>
    Initiated = 0,

    /// <summary>Call connected, ringing or in progress</summary>
    Connected = 1,

    /// <summary>Responder acknowledged the incident (pressed 1)</summary>
    Acknowledged = 2,

    /// <summary>Responder requested escalation (pressed 2)</summary>
    Escalated = 3,

    /// <summary>Call failed (SIP error, network issue)</summary>
    Failed = 4,

    /// <summary>No one answered the call</summary>
    NoAnswer = 5,

    /// <summary>Voicemail detected</summary>
    Voicemail = 6,

    /// <summary>Call timed out (max duration reached)</summary>
    Timeout = 7,

    /// <summary>Conference was created during the call</summary>
    ConferenceCreated = 8
}
