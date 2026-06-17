namespace Callu.Domain.Enums;

/// <summary>
/// Status of a captured webhook request
/// </summary>
public enum WebhookCaptureStatus
{
    /// <summary>
    /// Newly captured, not yet reviewed
    /// </summary>
    Captured,
    
    /// <summary>
    /// User has reviewed this capture
    /// </summary>
    Reviewed,
    
    /// <summary>
    /// Used to create a template
    /// </summary>
    UsedForTemplate,
    
    /// <summary>
    /// User ignored this capture
    /// </summary>
    Ignored
}
